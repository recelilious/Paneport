// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

#include "guest_output.h"

#include <errno.h>
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
    stream_t streams[PP_GUEST_STREAMS];
};

static void post_line(const char *line, size_t length, void *context) {
    const stream_t *stream = context;
    pp_log_entry_t entry;
    pp_log_text(&entry, PP_LOG_INFO, stream->source, stream->process, line, length);
    // Once the host stops, nobody reads the lines, so they go.
    (void)pp_event_thread_post_log_waiting(&entry);
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

static void *read_output(void *argument) {
    pp_guest_output_t *reader = argument;
    pp_platform_name_thread("paneport.guest-output");
    char buffer[16384];
    for (;;) {
        struct pollfd waits[PP_GUEST_STREAMS];
        stream_t *streams[PP_GUEST_STREAMS];
        const nfds_t count = open_streams(reader, waits, streams);
        if (count == 0U) {
            reader->on_end(reader->process);
            return NULL;
        }
        if (poll(waits, count, -1) < 0) {
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

pp_status_t pp_guest_output_start(int output_fd, int errors_fd, uint64_t process,
                                  pp_guest_output_end_t on_end, pp_guest_output_t **reader) {
    pp_guest_output_t *created = calloc(1U, sizeof *created);
    if (created == NULL) {
        if (output_fd >= 0) {
            (void)close(output_fd);
        }
        if (errors_fd >= 0) {
            (void)close(errors_fd);
        }
        return PP_E_OUT_OF_MEMORY;
    }
    created->on_end = on_end;
    created->process = process;
    const int fds[PP_GUEST_STREAMS] = {output_fd, errors_fd};
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
        for (size_t index = 0U; index < PP_GUEST_STREAMS; index++) {
            if (fds[index] >= 0) {
                (void)close(fds[index]);
            }
        }
        free(created);
        return PP_E_SYSTEM;
    }
    *reader = created;
    return PP_OK;
}

void pp_guest_output_join(pp_guest_output_t *reader) {
    if (reader == NULL) {
        return;
    }
    (void)pthread_join(reader->thread, NULL);
    free(reader);
}
