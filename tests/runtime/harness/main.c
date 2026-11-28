// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

// The runtime harness (docs/design/runtime-macos-harness.md section 5): runs one case of
// tests/runtime and compares what the program printed with its expected output.
// eng/mac/run-harness.sh runs each case in a fresh harness process.
//
//   paneport_harness --case <file> --programs <folder> --expected <folder> --prefix <folder>
//                    --loader <file> --logs <folder> [--mode single|multi] [--template <folder>]
//
// Both modes copy the program into the prefix's drive C:, at C:\paneport-tests, and run it as
// C:\paneport-tests\<program> with the case's arguments and variables, once for each of the
// case's runs, which stop at the first that fails.
//
// - single, the default: before each run the harness makes the prefix a fresh copy of the
//   template prefix, then starts itself in the child role of single_child.h, which runs the
//   program in its own process through the native host's session API and writes the program's
//   exit code into its result file (run_result.h). The session cases run in this mode only.
//   Every path that single mode takes is absolute.
// - multi: Wine as upstream runs it. The harness starts Wine's loader on the program as a child
//   process, with WINEPREFIX naming the prefix, and the runs share the prefix. A session case
//   is skipped.
//
// It prints one line: "PASS <case> (<seconds> s)", with ", <n> runs" after the seconds for a
// case of more than one run; "FAIL <case>: <why>", which names the failed run in such a case; or
// "SKIP <case>: <why>". It writes the last run's output, its error output and the result into
// <logs>/<case>/, and exits with 0 when the case passed, 1 when it failed, 2 when it could not
// run and 3 when it was skipped.
//
// The child role, which only the harness itself starts:
//
//   paneport_harness --run-single <case file> --prefix <folder> --runtime <folder> --logs <folder>

#include <errno.h>
#include <fcntl.h>
#include <limits.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <sys/stat.h>
#include <unistd.h>

#include "case_file.h"
#include "child.h"
#include "expected.h"
#include "prefix.h"
#include "run_result.h"
#include "single_child.h"

#define PP_HARNESS_OUTPUT_LIMIT ((size_t)4U * 1024U * 1024U)
#define PP_HARNESS_ERROR_SIZE 512U
#define PP_HARNESS_REASONS_SIZE 4096U

extern char **environ;

typedef struct options_t {
    const char *case_path;
    const char *programs;
    const char *expected;
    const char *prefix;
    const char *loader;
    const char *logs;
    const char *mode;
    const char *template_folder;
    const char *run_single;
    const char *runtime;
    // "pool" when the child gets its code memory from the JIT code pool; the parent passes it
    // to the child in pool mode.
    const char *jit;
    // The harness's own path, as it was started, which single mode starts its children with.
    const char *self;
} options_t;

static int usage(void) {
    (void)fprintf(stderr, "usage: paneport_harness --case <file> --programs <folder> --expected "
                          "<folder> --prefix <folder> --loader <file> --logs <folder> "
                          "[--mode single|pool|multi] [--template <folder>]\n");
    return 2;
}

// The member of options that an option of the command line sets, or NULL for an unknown one.
static const char **option_slot(options_t *options, const char *name) {
    if (strcmp(name, "--case") == 0) {
        return &options->case_path;
    }
    if (strcmp(name, "--programs") == 0) {
        return &options->programs;
    }
    if (strcmp(name, "--expected") == 0) {
        return &options->expected;
    }
    if (strcmp(name, "--prefix") == 0) {
        return &options->prefix;
    }
    if (strcmp(name, "--loader") == 0) {
        return &options->loader;
    }
    if (strcmp(name, "--logs") == 0) {
        return &options->logs;
    }
    if (strcmp(name, "--mode") == 0) {
        return &options->mode;
    }
    if (strcmp(name, "--template") == 0) {
        return &options->template_folder;
    }
    if (strcmp(name, "--run-single") == 0) {
        return &options->run_single;
    }
    if (strcmp(name, "--runtime") == 0) {
        return &options->runtime;
    }
    if (strcmp(name, "--jit") == 0) {
        return &options->jit;
    }
    return NULL;
}

static bool read_options(int argc, char *const *argv, options_t *options) {
    memset(options, 0, sizeof *options);
    options->mode = "single";
    options->self = argv[0];
    for (int index = 1; index + 1 < argc; index += 2) {
        const char **slot = option_slot(options, argv[index]);
        if (slot == NULL) {
            return false;
        }
        *slot = argv[index + 1];
    }
    return argc % 2 == 1;
}

// Whether the options hold what a run of a case needs; the child role needs fewer.
static bool has_case_options(const options_t *options) {
    const char *const required[] = {
        options->case_path, options->programs, options->expected,
        options->prefix,    options->loader,   options->logs,
    };
    for (size_t index = 0; index < sizeof required / sizeof required[0]; index++) {
        if (required[index] == NULL) {
            return false;
        }
    }
    return true;
}

// The case's name: its file name without the folder and without .json.
static void case_name(const char *path, char *name, size_t size) {
    const char *slash = strrchr(path, '/');
    const char *start = slash == NULL ? path : slash + 1;
    (void)snprintf(name, size, "%s", start);
    size_t length = strlen(name);
    if (length > 5U && strcmp(name + length - 5U, ".json") == 0) {
        name[length - 5U] = '\0';
    }
}

// Writes base/name into out; fails when it does not fit.
static bool join(char *out, size_t size, const char *base, const char *name) {
    int length = snprintf(out, size, "%s/%s", base, name);
    if (length <= 0) {
        return false;
    }
    return (size_t)length < size;
}

static bool make_folder(const char *path) {
    if (mkdir(path, 0755) == 0) {
        return true;
    }
    return errno == EEXIST;
}

static bool write_file(const char *path, const char *data, size_t length) {
    FILE *file = fopen(path, "wb");
    if (file == NULL) {
        return false;
    }
    bool written = true;
    if (length > 0U && fwrite(data, 1U, length, file) != length) {
        written = false;
    }
    if (fclose(file) != 0) {
        written = false;
    }
    return written;
}

static bool copy_file(const char *source, const char *target) {
    int input = open(source, O_RDONLY | O_CLOEXEC);
    if (input < 0) {
        return false;
    }
    int output = open(target, O_WRONLY | O_CREAT | O_TRUNC | O_CLOEXEC, 0755);
    bool copied = output >= 0;
    char buffer[65536];
    while (copied) {
        ssize_t count = read(input, buffer, sizeof buffer);
        if (count == 0) {
            break;
        }
        if (count < 0 || write(output, buffer, (size_t)count) != count) {
            copied = false;
        }
    }
    (void)close(input);
    if (output >= 0 && close(output) != 0) {
        copied = false;
    }
    return copied;
}

static void add_reason(char *reasons, const char *reason) {
    size_t used = strlen(reasons);
    (void)snprintf(reasons + used, PP_HARNESS_REASONS_SIZE - used, "%s%s", used == 0U ? "" : "; ",
                   reason);
}

// Puts the program into the prefix and returns its folder there, the working folder of the run.
static bool stage_program(const options_t *options, const pp_case_t *test, char *folder,
                          size_t folder_size, char *error) {
    char drive[PATH_MAX];
    char source[PATH_MAX];
    char target[PATH_MAX];
    if (!join(drive, sizeof drive, options->prefix, "drive_c") ||
        !join(folder, folder_size, drive, "paneport-tests") ||
        !join(source, sizeof source, options->programs, test->program) ||
        !join(target, sizeof target, folder, test->program)) {
        (void)snprintf(error, PP_HARNESS_ERROR_SIZE, "a path is too long");
        return false;
    }
    struct stat status;
    if (stat(drive, &status) != 0 || !S_ISDIR(status.st_mode)) {
        (void)snprintf(error, PP_HARNESS_ERROR_SIZE,
                       "the prefix has no drive_c; eng/mac/run-harness.sh prepares it");
        return false;
    }
    if (!make_folder(folder) || !copy_file(source, target)) {
        (void)snprintf(error, PP_HARNESS_ERROR_SIZE, "cannot copy %s into the prefix",
                       test->program);
        return false;
    }
    return true;
}

// --- Judging a run ---------------------------------------------------------------------------

static void compare_output(const pp_expected_t *expected, const pp_child_result_t *run,
                           char *reasons) {
    pp_comparison_t comparison;
    pp_expected_compare(expected, run->output == NULL ? "" : run->output, run->output_length,
                        &comparison);
    if (!comparison.matches) {
        add_reason(reasons, comparison.message);
    }
}

// Wine's report of an unhandled exception in the error output, and the streams that were cut.
static void report_streams(const pp_child_result_t *run, char *reasons) {
    char line[512];
    if (pp_wine_reported_unhandled(run->errors == NULL ? "" : run->errors, run->errors_length, line,
                                   sizeof line)) {
        char reason[600];
        (void)snprintf(reason, sizeof reason, "Wine reported: %s", line);
        add_reason(reasons, reason);
    }
    if (run->output_truncated) {
        add_reason(reasons, "the output went past 4 MiB and was cut");
    }
    if (run->errors_truncated) {
        add_reason(reasons, "the error stream went past 4 MiB and only its last 4 MiB were kept, "
                            "so a report of Wine's in the part that was cut would be missed");
    }
}

static void judge_multi(const pp_case_t *test, const pp_expected_t *expected,
                        const pp_child_result_t *run, char *reasons) {
    compare_output(expected, run, reasons);
    if (run->timed_out || !run->exited) {
        add_reason(reasons, run->message);
    } else if (run->exit_code != test->exit_code) {
        char reason[96];
        (void)snprintf(reason, sizeof reason, "exited with %d, not %d", run->exit_code,
                       test->exit_code);
        add_reason(reasons, reason);
    }
    report_streams(run, reasons);
}

// What one run of single mode left: how the harness child ended, with its output and error
// output, and the result that it wrote, if it wrote one.
typedef struct single_run_t {
    pp_child_result_t child;
    pp_run_result_t result;
    bool result_read;
    char result_error[PP_HARNESS_ERROR_SIZE];
} single_run_t;

// Appends text to a reason of PP_HARNESS_REASONS_SIZE bytes, cut where it would not fit.
static void append_text(char *reason, const char *separator, const char *text) {
    const size_t used = strlen(reason);
    (void)snprintf(reason + used, PP_HARNESS_REASONS_SIZE - used, "%s%s", separator, text);
}

// Adds "<head><item>; <item>...<tail>" to the reasons.
static void add_list(char *reasons, const char *head, const char (*items)[PP_RESULT_TEXT_SIZE],
                     size_t count, const char *tail) {
    char reason[PP_HARNESS_REASONS_SIZE] = "";
    append_text(reason, "", head);
    for (size_t index = 0; index < count; index++) {
        append_text(reason, index == 0U ? "" : "; ", items[index]);
    }
    append_text(reason, "", tail);
    add_reason(reasons, reason);
}

// What the harness child reported about its side of the run, or that it reported nothing.
static void report_child(const pp_case_t *test, const single_run_t *run, char *reasons) {
    const pp_child_result_t *child = &run->child;
    const pp_run_result_t *result = &run->result;
    char reason[PP_HARNESS_ERROR_SIZE + 96U];
    if (!run->result_read) {
        // A child that ran out of time was ended before it could write its result.
        if (child->exited) {
            (void)snprintf(reason, sizeof reason,
                           "the harness child exited with %d and wrote no result: %s",
                           child->exit_code, run->result_error);
            add_reason(reasons, reason);
        } else if (!child->timed_out) {
            (void)snprintf(reason, sizeof reason, "the harness child wrote no result: %s",
                           run->result_error);
            add_reason(reasons, reason);
        }
        return;
    }
    if (result->failure_count > 0U) {
        const char *tail = "";
        if (result->failures_cut) {
            tail = "; and more";
        }
        add_list(reasons, "the harness child found: ", result->failures, result->failure_count,
                 tail);
    }
    if (result->trap_count > 0U) {
        char head[64];
        (void)snprintf(head, sizeof head,
                       "the trap refused %u call%s: ", (unsigned)result->trap_count,
                       result->trap_count == 1U ? "" : "s");
        add_list(reasons, head, result->traps, result->trap_lines,
                 result->trap_count > result->trap_lines ? "; and more" : "");
    }
    const bool explained = (result->failure_count + result->trap_count) > 0U;
    if (child->exited && child->exit_code != 0 && !explained) {
        (void)snprintf(reason, sizeof reason, "the harness child exited with %d", child->exit_code);
        add_reason(reasons, reason);
    }
    if (test->kind == PP_CASE_PROGRAM && !result->has_exit_code && !explained &&
        !child->timed_out) {
        add_reason(reasons, "the harness child reported no exit code for the program");
    }
}

// Judges a run of single mode: the same comparison and reports of Wine as in multi mode, the
// program's whole exit code, then what the harness child found.
static void judge_single(const pp_case_t *test, const pp_expected_t *expected,
                         const single_run_t *run, char *reasons) {
    const pp_child_result_t *child = &run->child;
    if (test->kind == PP_CASE_PROGRAM) {
        compare_output(expected, child, reasons);
    }
    if (child->timed_out) {
        add_reason(reasons, child->message);
    } else if (!child->exited) {
        char reason[96];
        (void)snprintf(reason, sizeof reason, "the harness child ended by signal %d",
                       child->signal);
        add_reason(reasons, reason);
    } else if (test->kind == PP_CASE_PROGRAM && run->result_read && run->result.has_exit_code) {
        char reason[128];
        if (!pp_run_result_exit_code_matches(run->result.exit_code, test->exit_code, reason,
                                             sizeof reason)) {
            add_reason(reasons, reason);
        }
    }
    report_streams(child, reasons);
    report_child(test, run, reasons);
}

// --- The logs and the case's line --------------------------------------------------------------

static bool write_log(const char *folder, const char *name, const char *data, size_t length) {
    char path[PATH_MAX];
    if (!join(path, sizeof path, folder, name)) {
        return false;
    }
    return write_file(path, data, length);
}

static bool write_logs(const options_t *options, const char *name, const pp_child_result_t *run,
                       const char *summary) {
    char folder[PATH_MAX];
    if (!join(folder, sizeof folder, options->logs, name) || !make_folder(options->logs) ||
        !make_folder(folder)) {
        return false;
    }
    bool written = write_log(folder, "stdout.txt", run->output, run->output_length);
    if (!write_log(folder, "stderr.txt", run->errors, run->errors_length)) {
        written = false;
    }
    if (!write_log(folder, "result.txt", summary, strlen(summary))) {
        written = false;
    }
    return written;
}

// Prints the case's line and writes its logs, those of the last run. Returns 0 when the case
// passed and 1 when it failed.
static int report(const options_t *options, const char *name, const pp_case_t *test,
                  unsigned runs_done, double seconds, const char *reasons,
                  const pp_child_result_t *run) {
    char summary[PP_HARNESS_REASONS_SIZE + 128U];
    bool passed = reasons[0] == '\0';
    if (passed && test->runs == 1U) {
        (void)snprintf(summary, sizeof summary, "PASS %s (%.1f s)\n", name, seconds);
    } else if (passed) {
        (void)snprintf(summary, sizeof summary, "PASS %s (%.1f s, %u runs)\n", name, seconds,
                       test->runs);
    } else if (test->runs == 1U) {
        (void)snprintf(summary, sizeof summary, "FAIL %s: %s\n", name, reasons);
    } else {
        (void)snprintf(summary, sizeof summary, "FAIL %s: run %u of %u: %s\n", name, runs_done,
                       test->runs, reasons);
    }
    (void)fputs(summary, stdout);
    if (!write_logs(options, name, run, summary)) {
        (void)printf("ERROR %s: cannot write the logs into %s\n", name, options->logs);
    }
    if (passed) {
        return 0;
    }
    return 1;
}

// --- Multi mode ------------------------------------------------------------------------------

static int run_multi(const options_t *options, const char *name, const pp_case_t *test,
                     const pp_expected_t *expected) {
    char error[PP_HARNESS_ERROR_SIZE];
    char folder[PATH_MAX];
    char windows_path[PATH_MAX];
    if (!stage_program(options, test, folder, sizeof folder, error) ||
        snprintf(windows_path, sizeof windows_path, "C:\\paneport-tests\\%s", test->program) <= 0) {
        (void)printf("ERROR %s: %s\n", name, error);
        return 2;
    }

    char *arguments[PP_CASE_MAX_ARGUMENTS + 3U];
    arguments[0] = (char *)options->loader;
    arguments[1] = windows_path;
    for (size_t index = 0; index < test->argument_count; index++) {
        arguments[index + 2U] = test->arguments[index];
    }
    arguments[test->argument_count + 2U] = NULL;
    char *prefix_variable = NULL;
    char **environment = pp_case_environment(test, environ, options->prefix, &prefix_variable);
    pp_child_result_t run;
    memset(&run, 0, sizeof run);
    bool started = environment != NULL;
    if (!started) {
        (void)snprintf(run.message, sizeof run.message, "out of memory");
    }
    // The runs stop at the first that fails, and the logs hold the last run.
    char reasons[PP_HARNESS_REASONS_SIZE] = "";
    unsigned runs_done = 0;
    double seconds = 0.0;
    while (started && runs_done < test->runs && reasons[0] == '\0') {
        pp_child_result_free(&run);
        started = pp_child_run(arguments, environment, folder, test->timeout_seconds,
                               PP_HARNESS_OUTPUT_LIMIT, &run);
        if (started) {
            runs_done++;
            seconds += run.seconds;
            judge_multi(test, expected, &run, reasons);
        }
    }
    free((void *)environment);
    free(prefix_variable);
    if (!started) {
        (void)printf("ERROR %s: %s\n", name, run.message);
        pp_child_result_free(&run);
        return 2;
    }
    const int status = report(options, name, test, runs_done, seconds, reasons, &run);
    pp_child_result_free(&run);
    return status;
}

// Whether the mode is pool mode: single mode with the JIT code pool.
static bool is_pool_mode(const options_t *options) { return strcmp(options->mode, "pool") == 0; }

// A session case in multi mode, which has no session API, or a case of pool mode in another
// mode: its line says so, and so does its log.
static int skip(const options_t *options, const char *name, const pp_case_t *test) {
    char summary[256];
    if (pp_case_needs_pool(test) && !is_pool_mode(options)) {
        (void)snprintf(
            summary, sizeof summary,
            "SKIP %s: the case has pool_mib or pool_check, so it runs in pool mode only\n", name);
    } else {
        (void)snprintf(summary, sizeof summary,
                       "SKIP %s: %s is a session case, which runs in single mode only\n", name,
                       pp_case_kind_name(test->kind));
    }
    (void)fputs(summary, stdout);
    pp_child_result_t nothing;
    memset(&nothing, 0, sizeof nothing);
    if (!write_logs(options, name, &nothing, summary)) {
        (void)printf("ERROR %s: cannot write the logs into %s\n", name, options->logs);
    }
    return 3;
}

// --- Single mode -----------------------------------------------------------------------------

// What the runs of single mode share: the harness's own path and Wine's install folder for the
// child's command line, the case's log folder, and the result file in it.
typedef struct single_t {
    char self[PATH_MAX];
    char runtime[PATH_MAX];
    char logs[PATH_MAX];
    char result[PATH_MAX];
} single_t;

// Writes the folder that holds path into folder, which may be path itself.
static bool parent_folder(const char *path, char *folder, size_t size) {
    const char *slash = strrchr(path, '/');
    if (slash == NULL || slash == path) {
        return false;
    }
    const size_t length = (size_t)(slash - path);
    if (length >= size) {
        return false;
    }
    memmove(folder, path, length);
    folder[length] = '\0';
    return true;
}

// Prepares what the runs of single mode share. The child gets absolute paths only, because it
// runs in the program's folder.
static bool prepare_single(const options_t *options, const char *name, single_t *single,
                           char *error) {
    const char *const paths[] = {
        options->case_path, options->prefix, options->template_folder,
        options->loader,    options->logs,
    };
    for (size_t index = 0; index < sizeof paths / sizeof paths[0]; index++) {
        if (paths[index] == NULL || paths[index][0] != '/') {
            (void)snprintf(error, PP_HARNESS_ERROR_SIZE,
                           "single mode needs --template, and absolute paths for --case, "
                           "--prefix, --template, --loader and --logs");
            return false;
        }
    }
    if (strchr(options->self, '/') == NULL || realpath(options->self, single->self) == NULL) {
        (void)snprintf(error, PP_HARNESS_ERROR_SIZE,
                       "single mode needs the harness started by its path, which it starts its "
                       "children with");
        return false;
    }
    // Wine's install is the folder above the loader's bin/, and holds lib/wine.
    if (!parent_folder(options->loader, single->runtime, sizeof single->runtime) ||
        !parent_folder(single->runtime, single->runtime, sizeof single->runtime)) {
        (void)snprintf(error, PP_HARNESS_ERROR_SIZE,
                       "the loader is not in the bin folder of Wine's install");
        return false;
    }
    if (!join(single->logs, sizeof single->logs, options->logs, name) ||
        !join(single->result, sizeof single->result, single->logs, PP_RESULT_FILE) ||
        !make_folder(options->logs) || !make_folder(single->logs)) {
        (void)snprintf(error, PP_HARNESS_ERROR_SIZE, "cannot make the log folder of the case");
        return false;
    }
    return true;
}

// One run of single mode: a fresh copy of the template prefix with the program in it, and a
// harness child that runs it. Returns false, with a message in error, when the run could not
// start.
static bool run_single_once(const options_t *options, const single_t *single, const pp_case_t *test,
                            char *const environment[], single_run_t *run, char *error) {
    if (!pp_prefix_remove(options->prefix, error, PP_HARNESS_ERROR_SIZE) ||
        !pp_prefix_copy(options->template_folder, options->prefix, error, PP_HARNESS_ERROR_SIZE)) {
        return false;
    }
    char folder[PATH_MAX];
    const char *directory = NULL;
    if (test->kind == PP_CASE_PROGRAM) {
        if (!stage_program(options, test, folder, sizeof folder, error)) {
            return false;
        }
        directory = folder;
    }
    if (unlink(single->result) != 0 && errno != ENOENT) {
        (void)snprintf(error, PP_HARNESS_ERROR_SIZE, "cannot remove the last run's result");
        return false;
    }
    // In pool mode the child takes its code memory from the JIT code pool; "--jit pool" says so.
    char *arguments[] = {
        (char *)single->self,
        "--run-single",
        (char *)options->case_path,
        "--prefix",
        (char *)options->prefix,
        "--runtime",
        (char *)single->runtime,
        "--logs",
        (char *)single->logs,
        NULL,
        NULL,
        NULL,
    };
    if (is_pool_mode(options)) {
        arguments[9] = "--jit";
        arguments[10] = "pool";
    }
    if (!pp_child_run(arguments, environment, directory, test->timeout_seconds,
                      PP_HARNESS_OUTPUT_LIMIT, &run->child)) {
        (void)snprintf(error, PP_HARNESS_ERROR_SIZE, "%s", run->child.message);
        return false;
    }
    run->result_read = pp_run_result_read(single->result, &run->result, run->result_error,
                                          sizeof run->result_error);
    return true;
}

static int run_single(const options_t *options, const char *name, const pp_case_t *test,
                      const pp_expected_t *expected) {
    char error[PP_HARNESS_ERROR_SIZE] = "";
    single_t single;
    if (!prepare_single(options, name, &single, error)) {
        (void)printf("ERROR %s: %s\n", name, error);
        return 2;
    }
    char **environment = pp_case_inherited_environment(test, environ);
    if (environment == NULL) {
        (void)printf("ERROR %s: out of memory\n", name);
        return 2;
    }
    single_run_t run;
    memset(&run, 0, sizeof run);
    // The runs stop at the first that fails, and the logs hold the last run.
    char reasons[PP_HARNESS_REASONS_SIZE] = "";
    unsigned runs_done = 0;
    double seconds = 0.0;
    bool started = true;
    while (started && runs_done < test->runs && reasons[0] == '\0') {
        pp_child_result_free(&run.child);
        pp_run_result_init(&run.result);
        run.result_read = false;
        started = run_single_once(options, &single, test, environment, &run, error);
        if (started) {
            runs_done++;
            seconds += run.child.seconds;
            judge_single(test, expected, &run, reasons);
        }
    }
    free((void *)environment);
    if (!started) {
        (void)printf("ERROR %s: %s\n", name, error);
        pp_child_result_free(&run.child);
        return 2;
    }
    const int status = report(options, name, test, runs_done, seconds, reasons, &run.child);
    pp_child_result_free(&run.child);
    return status;
}

// --- The case --------------------------------------------------------------------------------

static int run_case(const options_t *options, const char *name, bool single) {
    char error[PP_HARNESS_ERROR_SIZE];
    pp_case_t test;
    if (!pp_case_read(options->case_path, &test, error, sizeof error)) {
        (void)printf("ERROR %s: %s: %s\n", name, options->case_path, error);
        return 2;
    }
    if ((!single && test.kind != PP_CASE_PROGRAM) ||
        (pp_case_needs_pool(&test) && !is_pool_mode(options))) {
        const int skipped = skip(options, name, &test);
        pp_case_free(&test);
        return skipped;
    }
    pp_expected_t expected;
    memset(&expected, 0, sizeof expected);
    if (test.kind == PP_CASE_PROGRAM) {
        char expected_path[PATH_MAX];
        if (!join(expected_path, sizeof expected_path, options->expected, test.expected) ||
            !pp_expected_read(expected_path, &expected, error, sizeof error)) {
            (void)printf("ERROR %s: %s: %s\n", name, test.expected, error);
            pp_case_free(&test);
            return 2;
        }
    }
    int status = 0;
    if (single) {
        status = run_single(options, name, &test, &expected);
    } else {
        status = run_multi(options, name, &test, &expected);
    }
    pp_expected_free(&expected);
    pp_case_free(&test);
    return status;
}

int main(int argc, char **argv) {
    options_t options;
    if (!read_options(argc, argv, &options)) {
        return usage();
    }
    if (options.run_single != NULL) {
        if (options.prefix == NULL || options.runtime == NULL || options.logs == NULL) {
            return usage();
        }
        if (options.jit != NULL && strcmp(options.jit, "pool") != 0) {
            return usage();
        }
        const pp_single_child_t child = {
            .case_path = options.run_single,
            .prefix = options.prefix,
            .runtime = options.runtime,
            .logs = options.logs,
            .pool = options.jit != NULL,
        };
        return pp_single_child_run(&child);
    }
    if (!has_case_options(&options)) {
        return usage();
    }
    char name[256];
    case_name(options.case_path, name, sizeof name);
    int status = 2;
    if (strcmp(options.mode, "single") == 0 || is_pool_mode(&options)) {
        status = run_case(&options, name, true);
    } else if (strcmp(options.mode, "multi") == 0) {
        status = run_case(&options, name, false);
    } else {
        (void)printf("ERROR %s: there is no mode %s; the modes are single, pool and multi\n", name,
                     options.mode);
    }
    (void)fflush(stdout);
    return status;
}
