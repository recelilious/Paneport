// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

// The prefixes of single mode: a copy of the template keeps its folders, files, permissions and
// symbolic links, whatever the umask, and is made from a folder only, both as a run makes it and
// without clones, as a file system that cannot clone takes it; and the removal of a run's prefix
// takes all of it, a folder that its owner cannot write and a dangling link among it, or fails
// and names what it could not read.

#include <errno.h>
#include <fcntl.h>
#include <limits.h>
#include <stdbool.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <sys/resource.h>
#include <sys/stat.h>
#include <sys/types.h>
#include <unistd.h>

#include "pp_check.h"
#include "prefix.h"

// A way to copy the template: as a run copies it, which clones each file on macOS, or without
// clones, as a file system that cannot clone copies it. The runner's template and prefixes share
// one APFS volume, where every clone works, so each copy case runs both ways.
typedef struct copy_way_t {
    const char *name;
    bool (*copy)(const char *source, const char *target, char *error, size_t error_size);
} copy_way_t;

static const copy_way_t k_ways[] = {
    {"pp_prefix_copy", pp_prefix_copy},
    {"pp_prefix_copy_without_clones", pp_prefix_copy_without_clones},
};

#define PP_WAY_COUNT (sizeof k_ways / sizeof k_ways[0])

// Names the way that a round of a copy case takes, so that a failed check of the round shows it.
static void start_round(const copy_way_t *way) { (void)fprintf(stderr, "through %s\n", way->name); }

static void make_temporary(char *path, size_t size) {
    PP_CHECK(snprintf(path, size, "/tmp/paneport-prefix-XXXXXX") > 0);
    PP_CHECK(mkdtemp(path) != NULL);
}

static void path_of(char *out, size_t size, const char *base, const char *name) {
    const int length = snprintf(out, size, "%s/%s", base, name);
    PP_CHECK(length > 0 && (size_t)length < size);
}

static void make_folder(const char *base, const char *name, mode_t mode) {
    char path[PATH_MAX];
    path_of(path, sizeof path, base, name);
    PP_CHECK_EQUAL(mkdir(path, 0700), 0);
    PP_CHECK_EQUAL(chmod(path, mode), 0);
}

static void write_text(const char *base, const char *name, const char *text, mode_t mode) {
    char path[PATH_MAX];
    path_of(path, sizeof path, base, name);
    const int file = open(path, O_WRONLY | O_CREAT | O_EXCL | O_CLOEXEC, 0600);
    PP_CHECK(file >= 0);
    const size_t length = strlen(text);
    PP_CHECK(write(file, text, length) == (ssize_t)length);
    PP_CHECK_EQUAL(fchmod(file, mode), 0);
    PP_CHECK_EQUAL(close(file), 0);
}

static void make_link(const char *base, const char *name, const char *target) {
    char path[PATH_MAX];
    path_of(path, sizeof path, base, name);
    PP_CHECK_EQUAL(symlink(target, path), 0);
}

// A template like a small prefix: folders, one with the sticky bit, a script with its execute
// bits, an empty file, a link into the tree as dosdevices/c: is, a dangling link, and a folder
// that its owner can read but not write.
static void make_template(const char *root) {
    PP_CHECK_EQUAL(mkdir(root, 0700), 0);
    PP_CHECK_EQUAL(chmod(root, 0755), 0);
    make_folder(root, "dosdevices", 0755);
    make_folder(root, "drive_c", 0755);
    make_folder(root, "drive_c/windows", 0750);
    make_folder(root, "drive_c/shared", 01755);
    write_text(root, "system.reg", "WINE REGISTRY Version 2\n", 0644);
    write_text(root, ".update-timestamp", "", 0600);
    write_text(root, "drive_c/windows/run.sh", "#!/bin/sh\nexit 0\n", 0750);
    make_link(root, "dosdevices/c:", "../drive_c");
    make_link(root, "dosdevices/y:", "/nonexistent-paneport-target");
    make_folder(root, "drive_c/sealed", 0700);
    write_text(root, "drive_c/sealed/inside.txt", "inside\n", 0400);
    char sealed[PATH_MAX];
    path_of(sealed, sizeof sealed, root, "drive_c/sealed");
    PP_CHECK_EQUAL(chmod(sealed, 0500), 0);
}

static mode_t mode_of(const char *base, const char *name) {
    char path[PATH_MAX];
    path_of(path, sizeof path, base, name);
    struct stat status;
    PP_CHECK_EQUAL(lstat(path, &status), 0);
    return (mode_t)(status.st_mode & 07777U);
}

static void check_file(const char *base, const char *name, const char *text, mode_t mode) {
    char path[PATH_MAX];
    path_of(path, sizeof path, base, name);
    struct stat status;
    PP_CHECK_EQUAL(lstat(path, &status), 0);
    PP_CHECK(S_ISREG(status.st_mode));
    PP_CHECK_EQUAL(status.st_mode & 07777U, mode);
    char content[256];
    const int file = open(path, O_RDONLY | O_CLOEXEC);
    PP_CHECK(file >= 0);
    const ssize_t length = read(file, content, sizeof content - 1U);
    PP_CHECK(length >= 0);
    PP_CHECK_EQUAL(close(file), 0);
    content[length] = '\0';
    PP_CHECK_TEXT(content, text);
}

static void check_link(const char *base, const char *name, const char *target) {
    char path[PATH_MAX];
    path_of(path, sizeof path, base, name);
    struct stat status;
    PP_CHECK_EQUAL(lstat(path, &status), 0);
    PP_CHECK(S_ISLNK(status.st_mode));
    char link[PATH_MAX];
    const ssize_t length = readlink(path, link, sizeof link - 1U);
    PP_CHECK(length > 0);
    link[length] = '\0';
    PP_CHECK_TEXT(link, target);
}

static bool exists(const char *path) {
    struct stat status;
    return lstat(path, &status) == 0;
}

// Copies the template from source into a new folder copy of root, the way given, and checks the
// copy.
static void check_copy(const char *root, const char *source, const copy_way_t *way) {
    char target[PATH_MAX];
    path_of(target, sizeof target, root, "copy");
    char error[512] = "";
    if (!way->copy(source, target, error, sizeof error)) {
        (void)fprintf(stderr, "%s\n", error);
        PP_CHECK(false);
    }
    PP_CHECK_EQUAL(mode_of(target, "."), 0755);
    PP_CHECK_EQUAL(mode_of(target, "drive_c/windows"), 0750);
    PP_CHECK_EQUAL(mode_of(target, "drive_c/shared"), 01755);
    PP_CHECK_EQUAL(mode_of(target, "drive_c/sealed"), 0500);
    check_file(target, "system.reg", "WINE REGISTRY Version 2\n", 0644);
    check_file(target, ".update-timestamp", "", 0600);
    check_file(target, "drive_c/windows/run.sh", "#!/bin/sh\nexit 0\n", 0750);
    check_file(target, "drive_c/sealed/inside.txt", "inside\n", 0400);
    check_link(target, "dosdevices/c:", "../drive_c");
    check_link(target, "dosdevices/y:", "/nonexistent-paneport-target");
    // The copy stands on its own: removing it leaves the template as it was.
    PP_CHECK(pp_prefix_remove(target, error, sizeof error));
    PP_CHECK(!exists(target));
    check_file(source, "drive_c/sealed/inside.txt", "inside\n", 0400);
}

static void copy_keeps_the_tree(void) {
    char root[PATH_MAX];
    make_temporary(root, sizeof root);
    char source[PATH_MAX];
    path_of(source, sizeof source, root, "template");
    make_template(source);
    // A template named with slashes at its end gives the same copy.
    char slashed[PATH_MAX];
    path_of(slashed, sizeof slashed, source, "/");
    for (size_t index = 0; index < PP_WAY_COUNT; index++) {
        start_round(&k_ways[index]);
        check_copy(root, source, &k_ways[index]);
        check_copy(root, slashed, &k_ways[index]);
    }
    char error[512] = "";
    PP_CHECK(pp_prefix_remove(root, error, sizeof error));
}

// The permissions of the copy are the template's whatever the umask, which open and mkdir apply
// to what they make. On macOS APFS clones each file with its permissions, so there the umask
// reaches only the folders, and only the round without clones shows that a file whose data is
// copied gets its permissions back.
static void copy_ignores_the_umask(void) {
    char root[PATH_MAX];
    make_temporary(root, sizeof root);
    char source[PATH_MAX];
    path_of(source, sizeof source, root, "template");
    make_template(source);
    const mode_t saved = umask(077);
    for (size_t index = 0; index < PP_WAY_COUNT; index++) {
        start_round(&k_ways[index]);
        check_copy(root, source, &k_ways[index]);
    }
    (void)umask(saved);
    char error[512] = "";
    PP_CHECK(pp_prefix_remove(root, error, sizeof error));
}

// The template must be a folder, and nothing is made from anything else: from a symbolic link to
// the template, whose copy would be a link through which every run wrote into the template, with
// or without a slash at its end, or from a file.
static void copy_needs_a_folder(void) {
    char root[PATH_MAX];
    make_temporary(root, sizeof root);
    char source[PATH_MAX];
    path_of(source, sizeof source, root, "template");
    make_template(source);
    make_link(root, "link", source);
    write_text(root, "file", "file\n", 0644);
    char target[PATH_MAX];
    path_of(target, sizeof target, root, "copy");
    static const char *const k_sources[] = {"link", "link/", "file"};
    char error[512] = "";
    for (size_t way = 0; way < PP_WAY_COUNT; way++) {
        start_round(&k_ways[way]);
        for (size_t index = 0; index < sizeof k_sources / sizeof k_sources[0]; index++) {
            char named[PATH_MAX];
            path_of(named, sizeof named, root, k_sources[index]);
            error[0] = '\0';
            PP_CHECK(!k_ways[way].copy(named, target, error, sizeof error));
            if (strstr(error, "which is not a folder") == NULL) {
                (void)fprintf(stderr, "for %s: \"%s\"\n", k_sources[index], error);
                PP_CHECK(false);
            }
            PP_CHECK(!exists(target));
        }
    }
    PP_CHECK(pp_prefix_remove(root, error, sizeof error));
}

// A copy goes into a new folder only, so that it never mixes with what a folder holds.
static void copy_needs_a_new_target(void) {
    char root[PATH_MAX];
    make_temporary(root, sizeof root);
    char source[PATH_MAX];
    path_of(source, sizeof source, root, "template");
    make_template(source);
    char missing[PATH_MAX];
    path_of(missing, sizeof missing, root, "missing");
    char target[PATH_MAX];
    path_of(target, sizeof target, root, "copy");
    char error[512] = "";
    for (size_t index = 0; index < PP_WAY_COUNT; index++) {
        start_round(&k_ways[index]);
        error[0] = '\0';
        PP_CHECK(!k_ways[index].copy(source, root, error, sizeof error));
        PP_CHECK(strstr(error, "cannot make") != NULL);
        error[0] = '\0';
        PP_CHECK(!k_ways[index].copy(missing, target, error, sizeof error));
        PP_CHECK(error[0] != '\0');
        PP_CHECK(!exists(target));
    }
    PP_CHECK(pp_prefix_remove(root, error, sizeof error));
}

static void remove_takes_everything(void) {
    char root[PATH_MAX];
    make_temporary(root, sizeof root);
    char source[PATH_MAX];
    path_of(source, sizeof source, root, "template");
    make_template(source);
    char error[512] = "";
    if (!pp_prefix_remove(root, error, sizeof error)) {
        (void)fprintf(stderr, "%s\n", error);
        PP_CHECK(false);
    }
    PP_CHECK(!exists(root));
}

// The first run's prefix does not exist yet, which counts as removed; a link is removed itself,
// never what it names.
static void remove_takes_links_and_missing_folders(void) {
    char error[512] = "";
    PP_CHECK(pp_prefix_remove("/nonexistent-paneport-prefix", error, sizeof error));
    char root[PATH_MAX];
    make_temporary(root, sizeof root);
    make_folder(root, "kept", 0755);
    write_text(root, "kept/file.txt", "kept\n", 0644);
    char kept[PATH_MAX];
    path_of(kept, sizeof kept, root, "kept");
    char link[PATH_MAX];
    path_of(link, sizeof link, root, "link");
    PP_CHECK_EQUAL(symlink(kept, link), 0);
    PP_CHECK(pp_prefix_remove(link, error, sizeof error));
    PP_CHECK(!exists(link));
    check_file(kept, "file.txt", "kept\n", 0644);
    PP_CHECK(pp_prefix_remove(root, error, sizeof error));
    PP_CHECK(!exists(root));
}

// A folder that the walk cannot read fails the removal, which names it, and what it holds stays:
// a removal that passed over the folder would report success with the prefix still there. The
// process has no descriptor left with which the walk could open the folder: its lowest free
// descriptor becomes the limit for the removal.
static void remove_names_what_it_cannot_read(void) {
    char root[PATH_MAX];
    make_temporary(root, sizeof root);
    make_folder(root, "unread", 0755);
    write_text(root, "unread/file.txt", "kept\n", 0644);
    char folder[PATH_MAX];
    path_of(folder, sizeof folder, root, "unread");
    const int lowest = open("/dev/null", O_RDONLY | O_CLOEXEC);
    PP_CHECK(lowest >= 0);
    PP_CHECK_EQUAL(close(lowest), 0);
    struct rlimit saved;
    PP_CHECK_EQUAL(getrlimit(RLIMIT_NOFILE, &saved), 0);
    struct rlimit limit = saved;
    limit.rlim_cur = (rlim_t)lowest;
    PP_CHECK_EQUAL(setrlimit(RLIMIT_NOFILE, &limit), 0);
    char error[512] = "";
    const bool removed = pp_prefix_remove(folder, error, sizeof error);
    PP_CHECK_EQUAL(setrlimit(RLIMIT_NOFILE, &saved), 0);
    PP_CHECK(!removed);
    if (strstr(error, folder) == NULL) {
        (void)fprintf(stderr, "\"%s\" does not name %s\n", error, folder);
        PP_CHECK(false);
    }
    check_file(folder, "file.txt", "kept\n", 0644);
    PP_CHECK(pp_prefix_remove(root, error, sizeof error));
}

int main(int argc, char **argv) {
    static const pp_test_case_t cases[] = {
        {"copy_keeps_the_tree", copy_keeps_the_tree},
        {"copy_ignores_the_umask", copy_ignores_the_umask},
        {"copy_needs_a_folder", copy_needs_a_folder},
        {"copy_needs_a_new_target", copy_needs_a_new_target},
        {"remove_takes_everything", remove_takes_everything},
        {"remove_takes_links_and_missing_folders", remove_takes_links_and_missing_folders},
        {"remove_names_what_it_cannot_read", remove_names_what_it_cannot_read},
    };
    return pp_run_case(argc, argv, cases, PP_CASE_COUNT(cases));
}
