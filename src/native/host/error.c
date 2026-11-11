// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

#include "error.h"

#include <stdarg.h>
#include <stdio.h>

#include "paneport/host.h"

// The calling thread's message. It lives as long as the thread, so the pointer that
// pp_last_error() returns stays valid; its text changes only with the thread's next call.
static _Thread_local char t_message[PP_ERROR_MESSAGE_SIZE];

void pp_error_clear(void) { t_message[0] = '\0'; }

pp_status_t pp_error_set(pp_status_t status, const char *format, ...) {
    va_list arguments;
    va_start(arguments, format);
    const int written = vsnprintf(t_message, sizeof t_message, format, arguments);
    va_end(arguments);
    if (written < 0) {
        t_message[0] = '\0';
    }
    return status;
}

PP_EXPORT const char *pp_last_error(void) { return t_message; }
