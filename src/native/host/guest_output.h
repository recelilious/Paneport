// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

// The capture of a Windows program's standard output and standard error (paneport/session.h).
// The host gives the program the write ends of two pipes as its standard handles, and a reader
// thread of the host reads the other ends, splits what arrives into lines, and queues each line
// for the log callback, marked with its source and the process's handle. The splitter is the
// same whatever the chunks: a line ends at a line feed, a carriage return right before it goes,
// and a line longer than a log line holds is split into pieces at character boundaries, each
// piece after the first flagged as a continuation (PP_LOG_FLAG_CONTINUATION), never marked in
// its text. A log line's text ends at its first NUL, so the reader turns each NUL byte into
// U+FFFD, the replacement character, and a line that the replacements make too long goes in
// pieces too; no other byte is lost.
//
// The lines wait in the host's log queue, bounded to PP_GUEST_BUFFER_LINES at a time. When the
// bound is reached the reader waits up to PP_GUEST_WAIT_MILLISECONDS for room, so that a burst
// slows the program down and normal output loses nothing. When the wait ends the reader drops
// lines, counting them, and delivers one report of the count (PP_LOG_FLAG_DROPPED) in order,
// at the place of the gap, as soon as room returns (docs/design/runtime-ios.md section 8).

#ifndef PANEPORT_HOST_GUEST_OUTPUT_H
#define PANEPORT_HOST_GUEST_OUTPUT_H

#include <stdbool.h>
#include <stddef.h>
#include <stdint.h>

#include "event_thread.h"
#include "paneport/common.h"

// The longest piece of a line that one log line carries, without its NUL.
#define PP_GUEST_LINE_MAX (PP_LOG_TEXT_SIZE - 1U)

// How many lines of Windows programs' output wait for the log callback at most, all processes
// together: half of the log queue, so that the host's own lines and the runtime's always find
// room. A line takes 552 bytes of the queue, so the bound holds 138 KiB.
#define PP_GUEST_BUFFER_LINES (PP_LOG_QUEUE_CAPACITY / 2U)

// How long the reader waits for room, at most, before it drops a line, and how often it tries
// to deliver a report of dropped lines while no line arrives. 250 ms is far beyond what a
// callback needs to take one line, and short enough that a program that outruns a stuck
// callback sees at most a pause of that length before its first line is dropped; once lines
// are being dropped the reader no longer waits, so a burst is not slowed by the drops.
#define PP_GUEST_WAIT_MILLISECONDS 250U
#define PP_GUEST_REPORT_RETRY_MILLISECONDS 10U

// Receives one line, without its line break, which need not be NUL-terminated. continuation is
// true for a piece of a line that is not its first.
typedef void (*pp_line_sink_t)(const char *line, size_t length, bool continuation, void *context);

// Splits a stream of bytes into lines. A carriage return waits in carriage_return until the
// next byte says whether it ends the line, and continued says that a piece of the current line
// went out already.
typedef struct pp_line_splitter_t {
    char buffer[PP_GUEST_LINE_MAX];
    size_t used;
    bool carriage_return;
    bool continued;
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
// end stays open: it takes what each pipe holds without waiting, at most 64 KiB of each, also
// while a writer keeps a pipe from running dry, queues the lines and the last piece of each
// stream, and ends. Any thread, before pp_guest_output_join; nothing for NULL.
void pp_guest_output_wake(pp_guest_output_t *reader);

// How many lines the reader has dropped so far, all streams of the reader, whether or not their
// reports arrived. Any thread.
uint64_t pp_guest_output_dropped(const pp_guest_output_t *reader);

// Waits for the reader to end, joins its thread and frees it; nothing for NULL.
void pp_guest_output_join(pp_guest_output_t *reader);

#endif // PANEPORT_HOST_GUEST_OUTPUT_H
