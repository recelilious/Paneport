// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

// The parent role of single mode, main.c, end to end: the harness, built with a fake of its child
// role (fake_single_child.c) that reports the exit code that its case's argument names, judges
// the program's whole Windows exit code, as the session reports it, where multi mode has only its
// low 8 bits. No Win32 test program can exit with more than 255, so no case of tests/runtime can
// show a code whose low 8 bits are the expected ones.

#include <fcntl.h>
#include <limits.h>
#include <stdbool.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <sys/stat.h>
#include <unistd.h>

#include "child.h"
#include "pp_check.h"
#include "prefix.h"

#ifndef PP_FAKE_CHILD_HARNESS
#error "tests/runtime/CMakeLists.txt defines PP_FAKE_CHILD_HARNESS."
#endif

extern char **environ;

// The folders of the harness's runs, all in one temporary folder: the template prefix, the
// programs, the expected outputs, the cases, Wine's loader, which the parent only names, the run's
// prefix and the logs.
typedef struct folders_t {
    char root[PATH_MAX];
    char template_folder[PATH_MAX];
    char programs[PATH_MAX];
    char expected[PATH_MAX];
    char cases[PATH_MAX];
    char loader[PATH_MAX];
    char prefix[PATH_MAX];
    char logs[PATH_MAX];
} folders_t;

static void path_of(char *out, size_t size, const char *base, const char *name) {
    const int length = snprintf(out, size, "%s/%s", base, name);
    PP_CHECK(length > 0 && (size_t)length < size);
}

static void write_text(const char *path, const char *text) {
    const int file = open(path, O_WRONLY | O_CREAT | O_EXCL | O_CLOEXEC, 0644);
    PP_CHECK(file >= 0);
    const size_t length = strlen(text);
    PP_CHECK(write(file, text, length) == (ssize_t)length);
    PP_CHECK_EQUAL(close(file), 0);
}

static void make_folder(const char *path) { PP_CHECK_EQUAL(mkdir(path, 0755), 0); }

// Makes the folders, with a template that holds drive C:, a program for the parent to copy into
// each run's prefix, and the expected output, the fake child's line.
static void make_folders(folders_t *folders) {
    PP_CHECK(snprintf(folders->root, sizeof folders->root, "/tmp/paneport-single-XXXXXX") > 0);
    PP_CHECK(mkdtemp(folders->root) != NULL);
    path_of(folders->template_folder, PATH_MAX, folders->root, "template");
    path_of(folders->programs, PATH_MAX, folders->root, "programs");
    path_of(folders->expected, PATH_MAX, folders->root, "expected");
    path_of(folders->cases, PATH_MAX, folders->root, "cases");
    path_of(folders->loader, PATH_MAX, folders->root, "wine/bin/wine");
    path_of(folders->prefix, PATH_MAX, folders->root, "prefix");
    path_of(folders->logs, PATH_MAX, folders->root, "logs");
    char path[PATH_MAX];
    make_folder(folders->template_folder);
    path_of(path, sizeof path, folders->template_folder, "drive_c");
    make_folder(path);
    make_folder(folders->programs);
    path_of(path, sizeof path, folders->programs, "fake.exe");
    write_text(path, "MZ");
    make_folder(folders->expected);
    path_of(path, sizeof path, folders->expected, "fake.txt");
    write_text(path, "= fake: the program's only line\n");
    make_folder(folders->cases);
}

// Runs the harness in single mode on a case named name, whose fake program reports the exit code
// reported while the case expects expected, and checks that the harness exits with status and
// prints line, of which it compares the first compared bytes.
static void check_run(const folders_t *folders, const char *name, const char *reported,
                      int expected, int status, const char *line, size_t compared) {
    char file_name[128];
    (void)snprintf(file_name, sizeof file_name, "%s.json", name);
    char case_file[PATH_MAX];
    path_of(case_file, sizeof case_file, folders->cases, file_name);
    char text[256];
    (void)snprintf(text, sizeof text,
                   "{\"program\": \"fake.exe\", \"arguments\": [\"%s\"], \"timeout_seconds\": 60, "
                   "\"exit_code\": %d, \"expected\": \"fake.txt\"}\n",
                   reported, expected);
    write_text(case_file, text);
    char *arguments[] = {
        PP_FAKE_CHILD_HARNESS,
        "--mode",
        "single",
        "--case",
        case_file,
        "--programs",
        (char *)folders->programs,
        "--expected",
        (char *)folders->expected,
        "--template",
        (char *)folders->template_folder,
        "--prefix",
        (char *)folders->prefix,
        "--loader",
        (char *)folders->loader,
        "--logs",
        (char *)folders->logs,
        NULL,
    };
    pp_child_result_t run;
    PP_CHECK(pp_child_run(arguments, environ, NULL, 60U, 65536U, &run));
    const char *output = run.output == NULL ? "" : run.output;
    if (strncmp(output, line, compared) != 0 || !run.exited || run.exit_code != status) {
        (void)fprintf(stderr,
                      "%s: the harness printed \"%s\" and exited with %d, not \"%s\" and %d\n",
                      name, output, run.exit_code, line, status);
        if (run.errors != NULL) {
            (void)fprintf(stderr, "its error output: %s\n", run.errors);
        }
        PP_CHECK(false);
    }
    pp_child_result_free(&run);
}

// Checks that the harness passes the case: its line starts with "PASS <name> (", before its time.
static void check_pass(const folders_t *folders, const char *name, const char *reported,
                       int expected) {
    char head[160];
    (void)snprintf(head, sizeof head, "PASS %s (", name);
    check_run(folders, name, reported, expected, 0, head, strlen(head));
}

// Checks that the harness fails the case with exactly the line given.
static void check_fail(const folders_t *folders, const char *name, const char *reported,
                       int expected, const char *line) {
    check_run(folders, name, reported, expected, 1, line, strlen(line) + 1U);
}

// A code whose low 8 bits are the expected ones fails, and is named in hexadecimal too, while a
// code equal to the expected one passes, and a code of 255 or less is named in decimal alone, as
// the planted case wrong_exit_code reads in CI.
static void whole_exit_code_judged(void) {
    folders_t folders;
    make_folders(&folders);
    check_pass(&folders, "code_0_for_0", "0", 0);
    check_pass(&folders, "code_3_for_3", "3", 3);
    check_fail(&folders, "code_256_for_0", "256", 0,
               "FAIL code_256_for_0: exited with 256, not 0 (Windows exit code 0x100)\n");
    check_fail(&folders, "code_c0000100_for_0", "3221225728", 0,
               "FAIL code_c0000100_for_0: exited with 3221225728, not 0 (Windows exit code "
               "0xc0000100)\n");
    check_fail(&folders, "code_259_for_3", "259", 3,
               "FAIL code_259_for_3: exited with 259, not 3 (Windows exit code 0x103)\n");
    check_fail(&folders, "code_3_for_4", "3", 4, "FAIL code_3_for_4: exited with 3, not 4\n");
    char error[512] = "";
    PP_CHECK(pp_prefix_remove(folders.root, error, sizeof error));
}

int main(int argc, char **argv) {
    static const pp_test_case_t cases[] = {
        {"whole_exit_code_judged", whole_exit_code_judged},
    };
    return pp_run_case(argc, argv, cases, PP_CASE_COUNT(cases));
}
