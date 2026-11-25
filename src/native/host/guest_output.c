// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

#include "guest_output.h"

#include <errno.h>
#include <fcntl.h>
#include <poll.h>
#include <pthread.h>
#include <stdlib.h>
#include <string.h>
#include <unistd.h>

#include "log.h"
#include "paneport/host.h"
#include "platform.h"

void pp_line_splitter_init(pp_line_splitter_t *splitter) {
    splitter->used = 0U;
    splitter->carriage_return = false;
}

// Hands out the first piece of a full buffer: the longest part that ends at a character
// boundary.
static void emit_piece(pp_line_splitter_t *splitter, pp_line_sink_t sink, void *context) {
    size_t cut = pp_utf8_cut(splitter->buffer, splitter->used);
    if (cut == 0U) {
        // No character boundary at all, so the bytes are not UTF-8: cut them as they are.
        cut = splitter->used;
    }
    sink(splitter->buffer, cut, context);
    memmove(splitter->buffer, splitter->buffer + cut, splitter->used - cut);
    splitter->used -= cut;
}

static void append(pp_line_splitter_t *splitter, char byte, pp_line_sink_t sink, void *context) {
    if (splitter->used == sizeof splitter->buffer) {
        emit_piece(splitter, sink, context);
    }
    splitter->buffer[splitter->used] = byte;
    splitter->used++;
}

void pp_line_splitter_feed(pp_line_splitter_t *splitter, const char *data, size_t length,
                           pp_line_sink_t sink, void *context) {
    for (size_t index = 0U; index < length; index++) {
        const char byte = data[index];
        if (byte == '\n') {
            // A carriage return right before the line feed goes with it.
            splitter->carriage_return = false;
            sink(splitter->buffer, splitter->used, context);
            splitter->used = 0U;
            continue;
        }
        if (splitter->carriage_return) {
            splitter->carriage_return = false;
            append(splitter, '\r', sink, context);
        }
        if (byte == '\r') {
            splitter->carriage_return = true;
            continue;
        }
        append(splitter, byte, sink, context);
    }
}

void pp_line_splitter_finish(pp_line_splitter_t *splitter, pp_line_sink_t sink, void *context) {
    // A carriage return at the very end goes, as before a line feed.
    splitter->carriage_return = false;
    if (splitter->used > 0U) {
        sink(splitter->buffer, splitter->used, context);
        splitter->used = 0U;
    }
}

enum { PP_GUEST_STREAMS = 2 };

// How much of a stream the reader takes once it is woken: what a pipe holds, at most.
#define PP_GUEST_FLUSH_LIMIT ((size_t)64U * 1024U)

// Where the lines of one stream go.
typedef struct stream_t {
    int fd;
    uint32_t source;
    uint64_t process;
    pp_line_splitter_t splitter;
} stream_t;

struct pp_guest_output_t {
    pthread_t thread;
    pp_guest_output_end_t on_end;
    uint64_t process;
    // The pipe through which pp_guest_output_wake ends the reader: wake[0] is in its poll set.
    int wake[2];
    stream_t streams[PP_GUEST_STREAMS];
};

// U+FFFD, the replacement character, in UTF-8.
static const char k_replacement[] = "\xef\xbf\xbd";
#define PP_REPLACEMENT_SIZE (sizeof k_replacement - 1U)

static void post_piece(const stream_t *stream, const char *text, size_t length) {
    pp_log_entry_t entry;
    pp_log_text(&entry, PP_LOG_INFO, stream->source, stream->process, text, length);
    // Once the host stops, nobody reads the lines, so they go.
    (void)pp_event_thread_post_log_waiting(&entry);
}

// Queues a line of at most PP_GUEST_LINE_MAX bytes. A log line's text ends at its first NUL, so
// each NUL byte becomes U+FFFD, and a line that the replacements make longer than a log line
// holds goes in pieces cut at character boundaries, as the splitter cuts.
static void post_line(const char *line, size_t length, void *context) {
    const stream_t *stream = context;
    if (memchr(line, '\0', length) == NULL) {
        post_piece(stream, line, length);
        return;
    }
    char text[PP_GUEST_LINE_MAX * PP_REPLACEMENT_SIZE];
    size_t used = 0U;
    for (size_t index = 0U; index < length; index++) {
        if (line[index] == '\0') {
            memcpy(text + used, k_replacement, PP_REPLACEMENT_SIZE);
            used += PP_REPLACEMENT_SIZE;
        } else {
            text[used] = line[index];
            used++;
        }
    }
    size_t offset = 0U;
    while (offset < used) {
        size_t piece = used - offset;
        if (piece > PP_GUEST_LINE_MAX) {
            piece = pp_utf8_cut(text + offset, PP_GUEST_LINE_MAX);
            if (piece == 0U) {
                piece = PP_GUEST_LINE_MAX;
            }
        }
        post_piece(stream, text + offset, piece);
        offset += piece;
    }
}

static void close_stream(stream_t *stream) {
    pp_line_splitter_finish(&stream->splitter, post_line, stream);
    (void)close(stream->fd);
    stream->fd = -1;
}

// Fills waits and streams with the streams that are still open, and returns their number.
static nfds_t open_streams(pp_guest_output_t *reader, struct pollfd waits[PP_GUEST_STREAMS],
                           stream_t *streams[PP_GUEST_STREAMS]) {
    nfds_t count = 0U;
    for (size_t index = 0U; index < PP_GUEST_STREAMS; index++) {
        if (reader->streams[index].fd >= 0) {
            waits[count].fd = reader->streams[index].fd;
            waits[count].events = POLLIN;
            waits[count].revents = 0;
            streams[count] = &reader->streams[index];
            count++;
        }
    }
    return count;
}

// Ends the streams once the reader was woken: what each pipe holds still becomes lines, read
// without waiting, though a copy of the write end stays open somewhere.
static void flush_streams(pp_guest_output_t *reader, char *buffer, size_t size) {
    for (size_t index = 0U; index < PP_GUEST_STREAMS; index++) {
        stream_t *stream = &reader->streams[index];
        if (stream->fd < 0) {
            continue;
        }
        const int flags = fcntl(stream->fd, F_GETFL);
        if (flags >= 0 &&
            fcntl(stream->fd, F_SETFL, (int)((unsigned)flags | (unsigned)O_NONBLOCK)) == 0) {
            size_t taken = 0U;
            while (taken < PP_GUEST_FLUSH_LIMIT) {
                const ssize_t read_count = read(stream->fd, buffer, size);
                if (read_count <= 0) {
                    break;
                }
                pp_line_splitter_feed(&stream->splitter, buffer, (size_t)read_count, post_line,
                                      stream);
                taken += (size_t)read_count;
            }
        }
        close_stream(stream);
    }
}

static void *read_output(void *argument) {
    pp_guest_output_t *reader = argument;
    pp_platform_name_thread("paneport.guest-output");
    char buffer[16384];
    for (;;) {
        // The open streams, and the wake pipe after them.
        struct pollfd waits[PP_GUEST_STREAMS + 1];
        stream_t *streams[PP_GUEST_STREAMS];
        const nfds_t count = open_streams(reader, waits, streams);
        if (count == 0U) {
            reader->on_end(reader->process);
            return NULL;
        }
        waits[count].fd = reader->wake[0];
        waits[count].events = POLLIN;
        waits[count].revents = 0;
        if (poll(waits, count + 1U, -1) < 0) {
            if (errno == EINTR) {
                continue;
            }
            // poll fails only on arguments that cannot be wrong here; end both streams.
            for (nfds_t index = 0U; index < count; index++) {
                close_stream(streams[index]);
            }
            reader->on_end(reader->process);
            return NULL;
        }
        if (waits[count].revents != 0) {
            flush_streams(reader, buffer, sizeof buffer);
            reader->on_end(reader->process);
            return NULL;
        }
        for (nfds_t index = 0U; index < count; index++) {
            if (waits[index].revents == 0) {
                continue;
            }
            const ssize_t read_count = read(waits[index].fd, buffer, sizeof buffer);
            if (read_count > 0) {
                pp_line_splitter_feed(&streams[index]->splitter, buffer, (size_t)read_count,
                                      post_line, streams[index]);
            } else if (read_count == 0 || (errno != EINTR && errno != EAGAIN)) {
                close_stream(streams[index]);
            }
        }
    }
}

static void close_both(const int fds[2]) {
    for (size_t index = 0U; index < 2U; index++) {
        if (fds[index] >= 0) {
            (void)close(fds[index]);
        }
    }
}

// Opens the wake pipe, whose ends reach no program that the host starts.
static bool open_wake_pipe(int wake[2]) {
    if (pipe(wake) != 0) {
        return false;
    }
    if (fcntl(wake[0], F_SETFD, FD_CLOEXEC) != 0 || fcntl(wake[1], F_SETFD, FD_CLOEXEC) != 0) {
        close_both(wake);
        return false;
    }
    return true;
}

pp_status_t pp_guest_output_start(int output_fd, int errors_fd, uint64_t process,
                                  pp_guest_output_end_t on_end, pp_guest_output_t **reader) {
    const int fds[PP_GUEST_STREAMS] = {output_fd, errors_fd};
    pp_guest_output_t *created = calloc(1U, sizeof *created);
    if (created == NULL) {
        close_both(fds);
        return PP_E_OUT_OF_MEMORY;
    }
    if (!open_wake_pipe(created->wake)) {
        close_both(fds);
        free(created);
        return PP_E_SYSTEM;
    }
    created->on_end = on_end;
    created->process = process;
    const uint32_t sources[PP_GUEST_STREAMS] = {
        PP_LOG_SOURCE_GUEST_OUTPUT,
        PP_LOG_SOURCE_GUEST_ERRORS,
    };
    for (size_t index = 0U; index < PP_GUEST_STREAMS; index++) {
        created->streams[index].fd = fds[index];
        created->streams[index].source = sources[index];
        created->streams[index].process = process;
        pp_line_splitter_init(&created->streams[index].splitter);
    }
    if (pthread_create(&created->thread, NULL, read_output, created) != 0) {
        close_both(fds);
        close_both(created->wake);
        free(created);
        return PP_E_SYSTEM;
    }
    *reader = created;
    return PP_OK;
}

void pp_guest_output_wake(pp_guest_output_t *reader) {
    if (reader == NULL) {
        return;
    }
    static const char k_byte = 1;
    ssize_t written = 0;
    do {
        written = write(reader->wake[1], &k_byte, 1U);
    } while (written < 0 && errno == EINTR);
}

void pp_guest_output_join(pp_guest_output_t *reader) {
    if (reader == NULL) {
        return;
    }
    (void)pthread_join(reader->thread, NULL);
    close_both(reader->wake);
    free(reader);
}
