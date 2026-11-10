// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

/**
 * @file
 * The exports of the host's test builds: the presets ios-device-test, macos-test,
 * asan-ubsan and tsan. The framework that the main app ships (ios-device) exports none of
 * them. They serve the interface conformance test of C-0005 section 5, which runs on macOS
 * in CI and on the device, and the binding tests of Paneport.Interop.
 */

#ifndef PANEPORT_TESTING_H
#define PANEPORT_TESTING_H

#include <stdint.h>

#include "paneport/common.h"

PP_EXTERN_C_BEGIN

/** One member of a struct in the layout table. */
typedef struct pp_test_layout_member_t {
    /** The member's name as the header spells it, UTF-8 and NUL-terminated. */
    const char *name;
    /** The member's offset from the start of the struct, in bytes. */
    uint32_t offset;
    /** The member's size, in bytes. */
    uint32_t size;
} pp_test_layout_member_t;

/** One struct of the interface in the layout table. */
typedef struct pp_test_layout_struct_t {
    /** The struct's type name, such as "pp_memory_budget_t", UTF-8 and NUL-terminated. */
    const char *name;
    /** sizeof the struct, in bytes. */
    uint32_t size;
    /** How many members follow. */
    uint32_t member_count;
    /** The members, in declaration order. */
    const pp_test_layout_member_t *members;
} pp_test_layout_struct_t;

/**
 * Returns the size and the member offsets of every struct that crosses the interface,
 * including the two structs of this table, so that the .NET side can compare its
 * definitions with the host's (C-0005 section 5).
 *
 * Thread rule: [any]. Memory: the table is static host memory; never free it.
 *
 * @param table Receives the first entry.
 * @param count Receives the number of entries.
 * @return PP_OK; PP_E_INVALID_ARGUMENT for NULL.
 */
PP_EXPORT pp_status_t pp_test_get_layout_table(const pp_test_layout_struct_t **table,
                                               uint32_t *count);

/**
 * Writes a crash record that is marked as a test, with signal 0, through the same writer
 * that a fatal fault uses. It needs pp_init() with a crash record folder.
 *
 * Thread rule: [any]. Memory: the caller keeps ownership of message.
 *
 * @param message A short UTF-8 message, NUL-terminated; it is cut at a character boundary
 *                when it does not fit the record.
 * @return PP_OK; PP_E_INVALID_ARGUMENT for NULL; PP_E_NOT_INITIALIZED without a folder;
 *         PP_E_BUSY while another record is being written; PP_E_IO.
 */
PP_EXPORT pp_status_t pp_test_write_crash_record(const char *message);

/**
 * Sets the values that the memory stub reports on macOS, where os_proc_available_memory()
 * does not exist. Until it is called, both are 0.
 *
 * Thread rule: [any]. Memory: none.
 *
 * @param available_bytes The available memory to report.
 * @param footprint_bytes The footprint to report.
 * @return PP_OK; PP_E_UNSUPPORTED on iOS, where the host reads the system.
 */
PP_EXPORT pp_status_t pp_test_set_memory_stub(uint64_t available_bytes, uint64_t footprint_bytes);

/**
 * Posts an event from the calling thread, as the host's own components do.
 *
 * Thread rule: [any]. Memory: none.
 *
 * @param kind The event's kind; any value.
 * @param code The event's code.
 * @return PP_OK; PP_E_NOT_INITIALIZED; PP_E_BUSY when the event queue is full, which the
 *         queue counts.
 */
PP_EXPORT pp_status_t pp_test_post_event(uint32_t kind, int64_t code);

/**
 * Logs a line from the calling thread through the host's log pipeline.
 *
 * Thread rule: [any]. Memory: the caller keeps ownership of text.
 *
 * @param level One of the PP_LOG_ values.
 * @param text The line, UTF-8 and NUL-terminated; a line too long for the pipeline is cut.
 * @return PP_OK; PP_E_INVALID_ARGUMENT for NULL or an unknown level; PP_E_NOT_INITIALIZED;
 *         PP_E_BUSY when the log queue is full, which the queue counts.
 */
PP_EXPORT pp_status_t pp_test_log(int32_t level, const char *text);

/**
 * Copies a text into host memory, so that a caller can test pp_free().
 *
 * Thread rule: [any]. Memory: the caller frees *copy with pp_free().
 *
 * @param text The text, NUL-terminated.
 * @param copy Receives the copy.
 * @return PP_OK; PP_E_INVALID_ARGUMENT for NULL; PP_E_OUT_OF_MEMORY.
 */
PP_EXPORT pp_status_t pp_test_duplicate_text(const char *text, char **copy);

PP_EXTERN_C_END

#endif /* PANEPORT_TESTING_H */
