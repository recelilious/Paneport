// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

// The log pipeline: the only path for the host's own log lines (C-0010 section 3). A line is
// formatted on the calling thread, cut to PP_LOG_TEXT_SIZE at a character boundary, and
// queued for the event thread, which hands it to the log callback. No line may contain a
// password, token or other secret (C-0010 section 2); the host logs no path either.

#ifndef PANEPORT_HOST_LOG_H
#define PANEPORT_HOST_LOG_H

#include <stdarg.h>
#include <stddef.h>
#include <stdint.h>

#include "event_thread.h"
#include "paneport/common.h"

// Logs a line. Any thread, but never a signal handler. Returns PP_OK, PP_E_NOT_INITIALIZED
// when the event thread is not running, or PP_E_BUSY when the log queue is full.
pp_status_t pp_log(int32_t level, const char *format, ...) __attribute__((format(printf, 2, 3)));

// Fills a queue entry: the level, the calling thread, and the formatted text, cut when it
// does not fit. A cut line ends with "...".
void pp_log_format(pp_log_entry_t *entry, int32_t level, const char *format, va_list arguments)
    __attribute__((format(printf, 3, 0)));

// Returns the longest length up to length at which text can be cut without splitting a UTF-8
// character. Async-signal-safe.
size_t pp_utf8_cut(const char *text, size_t length);

#endif // PANEPORT_HOST_LOG_H
