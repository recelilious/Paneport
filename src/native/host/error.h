// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

// The message that pp_last_error() returns: one buffer per thread, which every exported
// function except pp_abi_version, pp_last_error and pp_free clears when it starts, and fills
// when it fails. The buffers are the host's only thread-local state.

#ifndef PANEPORT_HOST_ERROR_H
#define PANEPORT_HOST_ERROR_H

#include "paneport/common.h"

// The size of a message, including its NUL. Messages are plain ASCII sentences.
#define PP_ERROR_MESSAGE_SIZE 256

// Clears the calling thread's message, as a call that succeeds leaves it.
void pp_error_clear(void);

// Sets the calling thread's message and returns status, so that a function can end with
// "return pp_error_set(...)". Never call it from a signal handler.
pp_status_t pp_error_set(pp_status_t status, const char *format, ...)
    __attribute__((format(printf, 2, 3)));

#endif // PANEPORT_HOST_ERROR_H
