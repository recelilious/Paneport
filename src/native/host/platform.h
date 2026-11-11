// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

// The few calls whose spelling differs between systems. The host builds for Apple platforms
// only, so platform_darwin.c is the one implementation.

#ifndef PANEPORT_HOST_PLATFORM_H
#define PANEPORT_HOST_PLATFORM_H

#include <stdbool.h>
#include <stdint.h>

// Returns the system's identifier of the calling thread, as log lines carry it.
uint64_t pp_platform_thread_id(void);

// Returns whether the calling thread is the process's main thread.
bool pp_platform_is_main_thread(void);

// Names the calling thread, for debuggers and crash reports.
void pp_platform_name_thread(const char *name);

#endif // PANEPORT_HOST_PLATFORM_H
