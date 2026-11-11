// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

// The crash record of C-0005 section 2: a fixed binary layout of 128 bytes, little-endian,
// which the app reads on its next launch. docs/design/native-host.md section 8 documents
// every byte, and the reader in Paneport.Core follows that table, not this header. The
// writer, in crash_record_writer.c, uses only async-signal-safe calls, so the signal
// dispatcher (M2.2) can call it from a fault handler; crash_record.c prepares its folder.

#ifndef PANEPORT_HOST_CRASH_RECORD_H
#define PANEPORT_HOST_CRASH_RECORD_H

#include <limits.h>
#include <stdint.h>

#include "paneport/common.h"

#define PP_CRASH_RECORD_MAGIC UINT32_C(0x52435050) // "PPCR" in file order.
#define PP_CRASH_RECORD_VERSION 1U
#define PP_CRASH_RECORD_SIZE 128U
#define PP_CRASH_RECORD_MESSAGE_OFFSET 64U
#define PP_CRASH_RECORD_MESSAGE_SIZE 64U // Including the NUL.
#define PP_CRASH_RECORD_FLAG_TEST 1U

// The size of a path, including its NUL.
#define PP_CRASH_RECORD_PATH_SIZE PATH_MAX

// The names of the record and of the file it is written to before it is renamed, inside the
// folder of pp_init_config_t.
#define PP_CRASH_RECORD_FILE_NAME "crash-record.bin"
#define PP_CRASH_RECORD_TEMPORARY_NAME "crash-record.tmp"

// The roles a thread can have when it faults. Later stages add guest and translator threads.
enum {
    PP_THREAD_ROLE_OTHER = 0,
    PP_THREAD_ROLE_MAIN = 1,
    PP_THREAD_ROLE_EVENT = 2,
};

// What a record says about a fault.
typedef struct pp_crash_info_t {
    int32_t signal;           // The signal number; 0 for a test record.
    int32_t code;             // The signal's si_code.
    uint64_t fault_address;   // si_addr.
    uint64_t program_counter; // The faulting instruction.
    uint32_t thread_role;     // A PP_THREAD_ROLE_ value.
    uint8_t is_test;          // 1 for a record that a test wrote.
    const char *message;      // A short UTF-8 text, or NULL.
} pp_crash_info_t;

// Checks the folder, which must be an absolute path, and hands the two paths in it to the
// writer. Called by pp_init, before any thread can fault. Returns PP_OK,
// PP_E_INVALID_ARGUMENT or PP_E_IO, with the error message set.
pp_status_t pp_crash_record_prepare(const char *folder);

// Gives the writer the paths of the record and of its temporary file. pp_crash_record_prepare
// calls it.
void pp_crash_record_set_paths(const char *record_path, const char *temporary_path);

// Takes the paths away again, so that the writer reports PP_E_NOT_INITIALIZED, as before
// pp_crash_record_prepare. pp_init calls it when it fails after preparing the folder.
void pp_crash_record_reset(void);

// Encodes a record into exactly PP_CRASH_RECORD_SIZE bytes. Async-signal-safe.
void pp_crash_record_encode(const pp_crash_info_t *info, int64_t seconds, int32_t nanoseconds,
                            unsigned char *record);

// Writes the record: to the temporary file first, which is then renamed, so a reader never
// sees half a record. Async-signal-safe; it keeps errno and never sets pp_last_error.
// Returns PP_OK; PP_E_NOT_INITIALIZED before pp_crash_record_prepare or after
// pp_crash_record_reset; PP_E_BUSY while another thread writes; PP_E_IO.
pp_status_t pp_crash_record_write(const pp_crash_info_t *info);

#endif // PANEPORT_HOST_CRASH_RECORD_H
