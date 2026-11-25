// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

// The capture of a Windows program's standard output and standard error (paneport/session.h).
// The host gives the program the write ends of two pipes as its standard handles, and a reader
// thread of the host reads the other ends, splits what arrives into lines, and queues each line
// for the log callback, marked with its source and the process's handle. The splitter is the
// same whatever the chunks: a line ends at a line feed, a carriage return right before it goes,
// and a line longer than a log line holds is split into pieces at character boundaries, so no
// byte is lost. When the log queue is full, the reader waits for room, so that a burst slows the
// program down instead of losing lines (docs/design/single-process-wine.md section 8).

#ifndef PANEPORT_HOST_GUEST_OUTPUT_H
#define PANEPORT_HOST_GUEST_OUTPUT_H

#include <stdbool.h>
#include <stddef.h>
#include <stdint.h>

#include "event_thread.h"
#include "paneport/common.h"

// The longest piece of a line that one log line carries, without its NUL.
#define PP_GUEST_LINE_MAX (PP_LOG_TEXT_SIZE - 1U)

// Receives one line, without its line break, which need not be NUL-terminated.
typedef void (*pp_line_sink_t)(const char *line, size_t length, void *context);

// Splits a stream of bytes into lines. A carriage return waits in carriage_return until the
// next byte says whether it ends the line.
typedef struct pp_line_splitter_t {
    char buffer[PP_GUEST_LINE_MAX];
    size_t used;
    bool carriage_return;
} pp_line_splitter_t;

// Starts a splitter with nothing buffered.
void pp_line_splitter_init(pp_line_splitter_t *splitter);

// Feeds bytes, and hands every line that they complete, and every full piece of a line that
// is too long, to sink.
void pp_line_splitter_feed(pp_line_splitter_t *splitter, const char *data, size_t length,
                           pp_line_sink_t sink, void *context);

// Hands what is left, a last line without a line feed, to sink, unless nothing is left.
void pp_line_splitter_finish(pp_line_splitter_t *splitter, pp_line_sink_t sink, void *context);

// The reader of one process's two pipes.
typedef struct pp_guest_output_t pp_guest_output_t;

// Receives the end of a reader: every line that it read has been queued.
typedef void (*pp_guest_output_end_t)(uint64_t process);

// Starts a reader thread for the read ends of a process's output and error pipes, either of
// which may be -1. It takes ownership of both descriptors and closes them when it ends, which it
// does once both pipes reached their end, or once pp_guest_output_wake woke it; it then calls
// on_end on its own thread. Returns PP_OK, PP_E_OUT_OF_MEMORY or PP_E_SYSTEM; on an error it has
// closed both and calls nothing.
pp_status_t pp_guest_output_start(int output_fd, int errors_fd, uint64_t process,
                                  pp_guest_output_end_t on_end, pp_guest_output_t **reader);

// Wakes the reader through a pipe of its own, so that it ends although a copy of a pipe's write
// end stays open: it takes what each pipe holds without waiting, at most 64 KiB, queues the
// lines and the last piece of each stream, and ends. Any thread, before pp_guest_output_join;
// nothing for NULL.
void pp_guest_output_wake(pp_guest_output_t *reader);

// Waits for the reader to end, joins its thread and frees it; nothing for NULL.
void pp_guest_output_join(pp_guest_output_t *reader);

#endif // PANEPORT_HOST_GUEST_OUTPUT_H
