// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

// The host event thread: the only thread that runs the .NET side's callbacks (C-0005
// section 1). Two bounded queues feed it, one for events and one for log lines, so that a
// flood of log lines never fills the events' queue. Each event arrives after the lines whose
// posts returned before its post, and lines posted after it do not hold it back.
// docs/design/native-host.md section 5 describes how it sleeps, wakes, orders lines and events,
// buffers early log lines and reports overflow.

#ifndef PANEPORT_HOST_EVENT_THREAD_H
#define PANEPORT_HOST_EVENT_THREAD_H

#include <stdbool.h>
#include <stdint.h>

#include "paneport/host.h"

// The capacity of each queue, and of the buffer for log lines that arrive before a log
// callback is set.
#define PP_EVENT_QUEUE_CAPACITY 256U
#define PP_LOG_QUEUE_CAPACITY 512U
#define PP_PENDING_LOG_LINES 64U

// The size of a log line's text, including its NUL. A longer line is cut (log.c).
#define PP_LOG_TEXT_SIZE 512U

// A log line as it waits in the queue: its PP_LOG_ level, its PP_LOG_SOURCE_ source, the thread
// that logged it, and the handle of the Windows process that wrote it, or 0 (paneport/host.h).
typedef struct pp_log_entry_t {
    int32_t level;
    uint32_t source;
    uint64_t thread_id;
    uint64_t process;
    char text[PP_LOG_TEXT_SIZE];
} pp_log_entry_t;

// Allocates the queues and starts the thread. The caller is pp_init.
pp_status_t pp_event_thread_start(void);

// Refuses new posts, delivers what is queued, stops and joins the thread, clears both
// callbacks and frees the queues. Once it returns, no callback runs again. The caller is
// pp_shutdown.
void pp_event_thread_stop(void);

// Queues an event, with the log queue's tail, so that the lines whose posts returned before this
// call arrive before the event. Any thread. Returns PP_OK, PP_E_NOT_INITIALIZED when the thread
// is not running, or PP_E_BUSY when the queue is full. Not async-signal-safe: waking the thread
// may take a lock.
pp_status_t pp_event_thread_post_event(const pp_event_t *event);

// Queues a log line. Any thread. The same results and rule as pp_event_thread_post_event.
pp_status_t pp_event_thread_post_log(const pp_log_entry_t *entry);

// Queues a log line as pp_event_thread_post_log does, but waits for room while the queue is
// full instead of refusing the line, so that a reader of a Windows program's output slows the
// program down instead of losing lines (guest_output.h). Any thread except the event thread
// itself. Returns PP_OK or PP_E_NOT_INITIALIZED when the thread does not run or stops.
pp_status_t pp_event_thread_post_log_waiting(const pp_log_entry_t *entry);

// Replace a callback. Once they return, the previous callback is not running and is never
// called again. The main thread.
void pp_event_thread_set_event_callback(pp_event_callback_t callback, void *user_data);
void pp_event_thread_set_log_callback(pp_log_callback_t callback, void *user_data);

// Returns whether the calling thread is the host event thread. Async-signal-safe on Darwin,
// where pthread_self reads a register.
bool pp_event_thread_is_current(void);

#ifdef PANEPORT_TEST_BUILD
// A function that a post calls between its check that the thread runs and its push.
typedef void (*pp_post_hook_t)(void *argument);

// Test builds only: makes every later post call hook with argument there, or no hook for NULL,
// so that a test can hold a post while the host stops (tests/native/test_event_thread.c).
void pp_event_thread_set_post_hook(pp_post_hook_t hook, void *argument);

// Test builds only: how many times the thread began to wait for a line that a producer claimed
// below an event's lines_end and had not published yet, which no event needs while no producer
// is inside a push (tests/native/test_event_thread.c). Any thread.
uint64_t pp_event_thread_line_waits(void);
#endif

#endif // PANEPORT_HOST_EVENT_THREAD_H
