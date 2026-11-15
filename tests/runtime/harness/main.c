// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

// The runtime harness (docs/design/runtime-macos-harness.md): runs one case of tests/runtime
// and compares what the program printed with its expected output. eng/mac/run-harness.sh runs
// each case in a fresh harness process.
//
//   paneport_harness --case <file> --programs <folder> --expected <folder> --prefix <folder>
//                    --loader <file> --logs <folder> [--mode multi]
//
// In multi mode, the only one so far, the harness copies the program into the prefix's drive
// C:, at C:\paneport-tests, and starts Wine's loader as a child process on it, with WINEPREFIX
// naming the prefix. It prints one line, "PASS <case> (<seconds> s)" or "FAIL <case>: <why>",
// writes the program's output, Wine's error output and the result into <logs>/<case>/, and
// exits with 0 when the case passed, 1 when it failed and 2 when it could not run.

#include <errno.h>
#include <fcntl.h>
#include <limits.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <sys/stat.h>
#include <unistd.h>

#include "case_file.h"
#include "child.h"
#include "expected.h"

#define PP_HARNESS_OUTPUT_LIMIT (4U * 1024U * 1024U)
#define PP_HARNESS_ERROR_SIZE 512U
#define PP_HARNESS_REASONS_SIZE 4096U
#define PP_HARNESS_MAX_ENVIRONMENT 4096U

extern char **environ;

typedef struct options_t {
    const char *case_path;
    const char *programs;
    const char *expected;
    const char *prefix;
    const char *loader;
    const char *logs;
    const char *mode;
} options_t;

static int usage(void) {
    (void)fprintf(stderr, "usage: paneport_harness --case <file> --programs <folder> --expected "
                          "<folder> --prefix <folder> --loader <file> --logs <folder> "
                          "[--mode multi]\n");
    return 2;
}

static bool read_options(int argc, char **argv, options_t *options) {
    memset(options, 0, sizeof *options);
    options->mode = "multi";
    for (int index = 1; index + 1 < argc; index += 2) {
        const char *name = argv[index];
        const char *value = argv[index + 1];
        const char **slot = NULL;
        if (strcmp(name, "--case") == 0) {
            slot = &options->case_path;
        } else if (strcmp(name, "--programs") == 0) {
            slot = &options->programs;
        } else if (strcmp(name, "--expected") == 0) {
            slot = &options->expected;
        } else if (strcmp(name, "--prefix") == 0) {
            slot = &options->prefix;
        } else if (strcmp(name, "--loader") == 0) {
            slot = &options->loader;
        } else if (strcmp(name, "--logs") == 0) {
            slot = &options->logs;
        } else if (strcmp(name, "--mode") == 0) {
            slot = &options->mode;
        } else {
            return false;
        }
        *slot = value;
    }
    return argc % 2 == 1 && options->case_path != NULL && options->programs != NULL &&
           options->expected != NULL && options->prefix != NULL && options->loader != NULL &&
           options->logs != NULL;
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

static bool join(char *path, size_t size, const char *folder, const char *name) {
    int length = snprintf(path, size, "%s/%s", folder, name);
    return length > 0 && (size_t)length < size;
}

static bool make_folder(const char *path) { return mkdir(path, 0755) == 0 || errno == EEXIST; }

static bool write_file(const char *path, const char *data, size_t length) {
    FILE *file = fopen(path, "wb");
    if (file == NULL) {
        return false;
    }
    bool written = length == 0U || fwrite(data, 1U, length, file) == length;
    return fclose(file) == 0 && written;
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
        copied = count > 0 && write(output, buffer, (size_t)count) == count;
    }
    (void)close(input);
    if (output >= 0 && close(output) != 0) {
        copied = false;
    }
    return copied;
}

// The harness's own environment, with WINEPREFIX naming the case's prefix. The strings stay
// owned by the environment, except the new WINEPREFIX, which the caller frees.
static char **case_environment(const char *prefix, char **owned) {
    size_t count = 0;
    while (environ[count] != NULL && count < PP_HARNESS_MAX_ENVIRONMENT) {
        count++;
    }
    char **result = calloc(count + 2U, sizeof *result);
    size_t size = strlen("WINEPREFIX=") + strlen(prefix) + 1U;
    *owned = malloc(size);
    if (result == NULL || *owned == NULL) {
        free(result);
        free(*owned);
        *owned = NULL;
        return NULL;
    }
    (void)snprintf(*owned, size, "WINEPREFIX=%s", prefix);
    size_t used = 0;
    for (size_t index = 0; index < count; index++) {
        if (strncmp(environ[index], "WINEPREFIX=", strlen("WINEPREFIX=")) != 0) {
            result[used] = environ[index];
            used++;
        }
    }
    result[used] = *owned;
    return result;
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

static void judge(const pp_case_t *test, const pp_expected_t *expected,
                  const pp_child_result_t *run, char *reasons, pp_comparison_t *comparison) {
    pp_expected_compare(expected, run->output == NULL ? "" : run->output, run->output_length,
                        comparison);
    if (!comparison->matches) {
        add_reason(reasons, comparison->message);
    }
    if (run->timed_out || !run->exited) {
        add_reason(reasons, run->message);
    } else if (run->exit_code != test->exit_code) {
        char reason[96];
        (void)snprintf(reason, sizeof reason, "exited with %d, not %d", run->exit_code,
                       test->exit_code);
        add_reason(reasons, reason);
    }
    char line[512];
    if (pp_wine_reported_unhandled(run->errors == NULL ? "" : run->errors, run->errors_length, line,
                                   sizeof line)) {
        char reason[600];
        (void)snprintf(reason, sizeof reason, "Wine reported: %s", line);
        add_reason(reasons, reason);
    }
    if (run->truncated) {
        add_reason(reasons, "the output went past 4 MiB and was cut");
    }
}

static bool write_logs(const options_t *options, const char *name, const pp_child_result_t *run,
                       const char *summary) {
    char folder[PATH_MAX];
    char path[PATH_MAX];
    if (!join(folder, sizeof folder, options->logs, name) || !make_folder(options->logs) ||
        !make_folder(folder)) {
        return false;
    }
    bool written = join(path, sizeof path, folder, "stdout.txt") &&
                   write_file(path, run->output, run->output_length);
    written = join(path, sizeof path, folder, "stderr.txt") &&
              write_file(path, run->errors, run->errors_length) && written;
    written = join(path, sizeof path, folder, "result.txt") &&
              write_file(path, summary, strlen(summary)) && written;
    return written;
}

static int run_case(const options_t *options, const char *name) {
    char error[PP_HARNESS_ERROR_SIZE];
    pp_case_t test;
    if (!pp_case_read(options->case_path, &test, error, sizeof error)) {
        (void)printf("ERROR %s: %s: %s\n", name, options->case_path, error);
        return 2;
    }
    char expected_path[PATH_MAX];
    pp_expected_t expected;
    if (!join(expected_path, sizeof expected_path, options->expected, test.expected) ||
        !pp_expected_read(expected_path, &expected, error, sizeof error)) {
        (void)printf("ERROR %s: %s: %s\n", name, test.expected, error);
        pp_case_free(&test);
        return 2;
    }
    char folder[PATH_MAX];
    char windows_path[PATH_MAX];
    if (!stage_program(options, &test, folder, sizeof folder, error) ||
        snprintf(windows_path, sizeof windows_path, "C:\\paneport-tests\\%s", test.program) <= 0) {
        (void)printf("ERROR %s: %s\n", name, error);
        pp_expected_free(&expected);
        pp_case_free(&test);
        return 2;
    }

    char *arguments[PP_CASE_MAX_ARGUMENTS + 3U];
    arguments[0] = (char *)options->loader;
    arguments[1] = windows_path;
    for (size_t index = 0; index < test.argument_count; index++) {
        arguments[index + 2U] = test.arguments[index];
    }
    arguments[test.argument_count + 2U] = NULL;
    char *prefix_variable = NULL;
    char **environment = case_environment(options->prefix, &prefix_variable);
    pp_child_result_t run;
    memset(&run, 0, sizeof run);
    if (environment == NULL) {
        (void)snprintf(run.message, sizeof run.message, "out of memory");
    }
    bool started =
        environment != NULL && pp_child_run(arguments, environment, folder, test.timeout_seconds,
                                            PP_HARNESS_OUTPUT_LIMIT, &run);
    free(environment);
    free(prefix_variable);
    if (!started) {
        (void)printf("ERROR %s: %s\n", name, run.message);
        pp_expected_free(&expected);
        pp_case_free(&test);
        return 2;
    }

    char reasons[PP_HARNESS_REASONS_SIZE] = "";
    pp_comparison_t comparison;
    judge(&test, &expected, &run, reasons, &comparison);
    char summary[PP_HARNESS_REASONS_SIZE + 128U];
    bool passed = reasons[0] == '\0';
    if (passed) {
        (void)snprintf(summary, sizeof summary, "PASS %s (%.1f s)\n", name, run.seconds);
    } else {
        (void)snprintf(summary, sizeof summary, "FAIL %s: %s\n", name, reasons);
    }
    (void)fputs(summary, stdout);
    if (!write_logs(options, name, &run, summary)) {
        (void)printf("ERROR %s: cannot write the logs into %s\n", name, options->logs);
    }
    pp_child_result_free(&run);
    pp_expected_free(&expected);
    pp_case_free(&test);
    return passed ? 0 : 1;
}

int main(int argc, char **argv) {
    options_t options;
    if (!read_options(argc, argv, &options)) {
        return usage();
    }
    char name[256];
    case_name(options.case_path, name, sizeof name);
    if (strcmp(options.mode, "multi") != 0) {
        (void)printf("ERROR %s: mode %s comes with a later stage; only multi runs now\n", name,
                     options.mode);
        return 2;
    }
    int status = run_case(&options, name);
    (void)fflush(stdout);
    return status;
}
