// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

#include "guest_output.h"

#include <errno.h>
#include <fcntl.h>
#include <poll.h>
#include <pthread.h>
#include <stdatomic.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <time.h>
#include <unistd.h>

#include "log.h"
#include "paneport/host.h"
#include "platform.h"

void pp_line_splitter_init(pp_line_splitter_t *splitter) {
    splitter->used = 0U;
    splitter->carriage_return = false;
    splitter->continued = false;
}

// Hands out the first piece of a full buffer: the longest part that ends at a character
// boundary.
static void emit_piece(pp_line_splitter_t *splitter, pp_line_sink_t sink, void *context) {
    size_t cut = pp_utf8_cut(splitter->buffer, splitter->used);
    if (cut == 0U) {
        // No character boundary at all, so the bytes are not UTF-8: cut them as they are.
        cut = splitter->used;
    }
    sink(splitter->buffer, cut, splitter->continued, context);
    splitter->continued = true;
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
            sink(splitter->buffer, splitter->used, splitter->continued, context);
            splitter->used = 0U;
            splitter->continued = false;
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
        sink(splitter->buffer, splitter->used, splitter->continued, context);
        splitter->used = 0U;
    }
    splitter->continued = false;
}

enum { PP_GUEST_STREAMS = 2 };

// How much of each stream the reader takes at most once it is woken, what a pipe holds, also
// while a writer keeps the pipe from running dry: the limit bounds the join of pp_session_stop.
#define PP_GUEST_FLUSH_LIMIT ((size_t)64U * 1024U)

// Where the lines of one stream go.
typedef struct stream_t {
    int fd;
    uint32_t source;
    uint64_t process;
    pp_line_splitter_t splitter;
    // The lines dropped since the last report that arrived: while it is above 0, the stream is
    // in a gap, and no line goes out before the gap's report does.
    uint64_t dropped;
    _Atomic(uint64_t) *dropped_total;
} stream_t;

struct pp_guest_output_t {
    pthread_t thread;
    pp_guest_output_end_t on_end;
    uint64_t process;
    _Atomic(uint64_t) dropped_total;
    // The pipe through which pp_guest_output_wake ends the reader: wake[0] is in its poll set.
    int wake[2];
    stream_t streams[PP_GUEST_STREAMS];
};

// U+FFFD, the replacement character, in UTF-8.
static const char k_replacement[] = "\xef\xbf\xbd";
#define PP_REPLACEMENT_SIZE (sizeof k_replacement - 1U)

// What one try to queue a record came to.
typedef enum post_result_t {
    POST_QUEUED,
    // The bound or the queue is full.
    POST_FULL,
    // The host stopped, and nobody reads the lines any more.
    POST_STOPPED,
} post_result_t;

static post_result_t post_once(const pp_log_entry_t *entry) {
    const pp_status_t status = pp_event_thread_try_post_guest_line(entry, PP_GUEST_BUFFER_LINES);
    if (status == PP_OK) {
        return POST_QUEUED;
    }
    return status == PP_E_BUSY ? POST_FULL : POST_STOPPED;
}

// Tries to queue a record, and while it finds no room, pauses 1 ms and tries again, for at most
// wait_milliseconds pauses.
static post_result_t post_within(const pp_log_entry_t *entry, uint32_t wait_milliseconds) {
    const struct timespec pause = {.tv_sec = 0, .tv_nsec = 1000000L};
    post_result_t result = post_once(entry);
    for (uint32_t waited = 0U; result == POST_FULL && waited < wait_milliseconds; waited++) {
        (void)nanosleep(&pause, NULL);
        result = post_once(entry);
    }
    return result;
}

// Delivers the report of the lines that the stream dropped, if there is room, and says whether
// the stream's gap is closed. With wait_milliseconds above 0 it waits for room that long.
static bool report_gap(stream_t *stream, uint32_t wait_milliseconds) {
    if (stream->dropped == 0U) {
        return true;
    }
    char text[64];
    const int written =
        snprintf(text, sizeof text, "%llu lines dropped", (unsigned long long)stream->dropped);
    pp_log_entry_t entry;
    pp_log_text(&entry, PP_LOG_WARNING, stream->source, stream->process, text,
                written > 0 ? (size_t)written : 0U);
    entry.flags = (uint32_t)PP_LOG_FLAG_DROPPED;
    entry.dropped = stream->dropped;
    if (post_within(&entry, wait_milliseconds) == POST_FULL) {
        return false;
    }
    // Queued, or the host stopped: either way nothing more is owed.
    stream->dropped = 0U;
    return true;
}

// Queues one piece of a line. Once the bound is reached it waits up to
// PP_GUEST_WAIT_MILLISECONDS for room, and drops the piece if none returns; from then on, until
// the report of the gap is queued, a piece goes only if room is there at once, behind the
// report, so a burst is not slowed by its drops.
static void post_piece(stream_t *stream, const char *text, size_t length, bool continuation) {
    if (!report_gap(stream, 0U)) {
        stream->dropped++;
        (void)atomic_fetch_add(stream->dropped_total, 1U);
        return;
    }
    pp_log_entry_t entry;
    pp_log_text(&entry, PP_LOG_INFO, stream->source, stream->process, text, length);
    entry.flags = 0U;
    if (continuation) {
        entry.flags = (uint32_t)PP_LOG_FLAG_CONTINUATION;
    }
    if (post_within(&entry, PP_GUEST_WAIT_MILLISECONDS) == POST_FULL) {
        stream->dropped++;
        (void)atomic_fetch_add(stream->dropped_total, 1U);
    }
}

// Queues a line of at most PP_GUEST_LINE_MAX bytes. A log line's text ends at its first NUL, so
// each NUL byte becomes U+FFFD, and a line that the replacements make longer than a log line
// holds goes in pieces cut at character boundaries, as the splitter cuts, each after the first
// flagged as a continuation.
static void post_line(const char *line, size_t length, bool continuation, void *context) {
    stream_t *stream = context;
    if (memchr(line, '\0', length) == NULL) {
        post_piece(stream, line, length, continuation);
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
        // Every piece after the first continues the line.
        bool continues = continuation;
        if (offset > 0U) {
            continues = true;
        }
        post_piece(stream, text + offset, piece, continues);
        offset += piece;
    }
}

static void close_stream(stream_t *stream) {
    pp_line_splitter_finish(&stream->splitter, post_line, stream);
    // The last report waits for room as a line does, since nothing comes after it.
    (void)report_gap(stream, PP_GUEST_WAIT_MILLISECONDS);
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
                // No read goes past the limit.
                size_t wanted = PP_GUEST_FLUSH_LIMIT - taken;
                if (wanted > size) {
                    wanted = size;
                }
                const ssize_t read_count = read(stream->fd, buffer, wanted);
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

// Tries again the reports that found no room yet, so that a gap shows although the program
// writes nothing more, and returns how long the reader may wait for its pipes: -1 for as long
// as it takes, or a short time while a report is still owed.
static int retry_reports(stream_t *streams[PP_GUEST_STREAMS], nfds_t count) {
    int timeout = -1;
    for (nfds_t index = 0U; index < count; index++) {
        if (!report_gap(streams[index], 0U)) {
            timeout = (int)PP_GUEST_REPORT_RETRY_MILLISECONDS;
        }
    }
    return timeout;
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
        if (poll(waits, count + 1U, retry_reports(streams, count)) < 0) {
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
    atomic_init(&created->dropped_total, 0U);
    const uint32_t sources[PP_GUEST_STREAMS] = {
        PP_LOG_SOURCE_GUEST_OUTPUT,
        PP_LOG_SOURCE_GUEST_ERRORS,
    };
    for (size_t index = 0U; index < PP_GUEST_STREAMS; index++) {
        created->streams[index].fd = fds[index];
        created->streams[index].source = sources[index];
        created->streams[index].process = process;
        created->streams[index].dropped = 0U;
        created->streams[index].dropped_total = &created->dropped_total;
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

uint64_t pp_guest_output_dropped(const pp_guest_output_t *reader) {
    return reader == NULL ? 0U : atomic_load(&reader->dropped_total);
}

void pp_guest_output_join(pp_guest_output_t *reader) {
    if (reader == NULL) {
        return;
    }
    (void)pthread_join(reader->thread, NULL);
    close_both(reader->wake);
    free(reader);
}
