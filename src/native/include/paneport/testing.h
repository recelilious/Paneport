// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

/**
 * @file
 * The exports of the host's test builds: the presets ios-device-test, macos-test,
 * asan-ubsan and tsan. The framework that the main app ships (ios-device) exports none of
 * them. They serve the interface conformance test of C-0005 section 5, which runs on macOS
 * in CI and on the device, and the binding tests of Paneport.Interop.
 */

#ifndef PP_TESTING_H
#define PP_TESTING_H

#include <stdint.h>

#include "paneport/common.h"

PP_EXTERN_C_BEGIN

/**
 * One member of a struct in the layout table. Like every struct of the interface, it starts
 * with struct_size (C-0005 section 1); a reader steps from one member to the next by it.
 */
typedef struct pp_test_layout_member_t {
    /** sizeof(pp_test_layout_member_t) in the host that built the table. */
    uint32_t struct_size;
    /** The member's name as the header spells it, UTF-8 and NUL-terminated. */
    const char *name;
    /** The member's offset from the start of the struct, in bytes. */
    uint32_t offset;
    /** The member's size, in bytes. */
    uint32_t size;
} pp_test_layout_member_t;

/**
 * One struct of the interface in the layout table. It starts with struct_size, and a reader
 * steps from one entry of the table to the next by it.
 */
typedef struct pp_test_layout_struct_t {
    /** sizeof(pp_test_layout_struct_t) in the host that built the table. */
    uint32_t struct_size;
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

/**
 * Checks that each of SIGSEGV, SIGBUS, SIGILL, SIGFPE, SIGTRAP and SIGABRT still has the signal
 * dispatcher's handler (C-0005 section 4), as test builds do at the end of pp_init().
 *
 * Thread rule: [any]. Memory: none.
 *
 * @param replaced_signal Receives 0, or the number of the first signal whose handler was
 *                        replaced; may be NULL.
 * @return PP_OK; PP_E_NOT_INITIALIZED before pp_init(); PP_E_SYSTEM when a handler was
 *         replaced, with a message that names the signal.
 */
PP_EXPORT pp_status_t pp_test_signals_check_installed(int32_t *replaced_signal);

/**
 * How the stress harness of the signal gate runs (docs/design/signals.md section 9).
 */
typedef struct pp_test_signal_harness_config_t {
    /** sizeof(pp_test_signal_harness_config_t) in the caller. */
    uint32_t struct_size;
    /** Threads marked as guest threads that fault on a guard page and a read-only page; 0 to 16. */
    uint32_t marked_threads;
    /** Unmarked threads that fault inside a registered guest reservation; 0 to 16. */
    uint32_t unmarked_threads;
    /** The pause of each thread between two faults, in microseconds; at most 1,000,000. */
    uint32_t pause_microseconds;
} pp_test_signal_harness_config_t;

/**
 * The counters of the stress harness and of the signal dispatcher.
 */
typedef struct pp_test_signal_counters_t {
    /** sizeof(pp_test_signal_counters_t) in the host. */
    uint32_t struct_size;
    /** 1 while the harness runs, else 0. */
    uint8_t running;
    /** Faults that the marked threads planted. */
    uint64_t guest_faults_raised_by_mark;
    /** Faults that the harness's guest fault handler took by the thread's mark. */
    uint64_t guest_faults_handled_by_mark;
    /** Faults that the unmarked threads planted in the reservation. */
    uint64_t guest_faults_raised_by_address;
    /** Faults that the harness's guest fault handler took by their address. */
    uint64_t guest_faults_handled_by_address;
    /** Faults that took a route the harness did not plant them for. */
    uint64_t unexpected_faults;
    /** Faults of any thread that the dispatcher passed to the runtime's handler. */
    uint64_t runtime_faults;
} pp_test_signal_counters_t;

/**
 * Starts the stress harness: it maps its pages, registers its reservation and its guest fault
 * handler, which counts each fault and skips the faulting instruction, and starts its threads.
 * It needs pp_init(), and no other guest fault handler.
 *
 * Thread rule: [any]. Memory: none.
 *
 * @param config How it runs; a struct_size of at least 16.
 * @return PP_OK; PP_E_INVALID_ARGUMENT for NULL or a value out of range; PP_E_NOT_INITIALIZED
 *         before pp_init(); PP_E_BUSY while it runs, or while another guest fault handler is
 *         set; PP_E_OUT_OF_MEMORY; PP_E_SYSTEM.
 */
PP_EXPORT pp_status_t pp_test_signal_harness_start(const pp_test_signal_harness_config_t *config);

/**
 * Stops the stress harness: its threads end, and its handler, reservation and pages go. Its
 * counters keep their final values until the next start.
 *
 * Thread rule: [any]. Memory: none.
 *
 * @return PP_OK, also when it was not running.
 */
PP_EXPORT pp_status_t pp_test_signal_harness_stop(void);

/**
 * Reads the counters of the stress harness and of the signal dispatcher.
 *
 * Thread rule: [any]. Memory: none.
 *
 * @param counters Receives the counters.
 * @return PP_OK; PP_E_INVALID_ARGUMENT for NULL.
 */
PP_EXPORT pp_status_t pp_test_signal_harness_read(pp_test_signal_counters_t *counters);

PP_EXTERN_C_END

#endif /* PP_TESTING_H */
