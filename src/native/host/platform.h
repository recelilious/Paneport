// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

// The few calls whose spelling differs between systems. The host builds for Apple platforms
// only, so platform_darwin.c is the one implementation.

#ifndef PANEPORT_HOST_PLATFORM_H
#define PANEPORT_HOST_PLATFORM_H

#include <stdbool.h>
#include <stddef.h>
#include <stdint.h>

// Returns the system's identifier of the calling thread, as log lines carry it.
uint64_t pp_platform_thread_id(void);

// Returns whether the calling thread is the process's main thread.
bool pp_platform_is_main_thread(void);

// Names the calling thread, for debuggers and crash reports.
void pp_platform_name_thread(const char *name);

// Reads the device's model identifier, the system's hw.machine value, into buffer when it fits,
// NUL-terminated. Sets *needed to the bytes it takes with its NUL, and returns false when the
// system does not report it.
bool pp_platform_device_model(char *buffer, size_t size, size_t *needed);

#endif // PANEPORT_HOST_PLATFORM_H
