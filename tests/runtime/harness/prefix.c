// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

#include "prefix.h"

#include <errno.h>
#include <fcntl.h>
#include <fts.h>
#include <limits.h>
#include <stdio.h>
#include <string.h>
#include <sys/stat.h>
#include <sys/types.h>
#include <unistd.h>

#ifdef __APPLE__
#include <sys/clonefile.h>
#endif

#define PP_PREFIX_COPY_SIZE 65536U

// Writes "cannot <what> <path>: <reason>" into error, for the error number reason.
static void describe(char *error, size_t error_size, const char *what, const char *path,
                     int reason) {
    char text[128] = "unknown error";
    (void)strerror_r(reason, text, sizeof text);
    (void)snprintf(error, error_size, "cannot %s %s: %s", what, path, text);
}

// Walks a folder's tree in the physical order of fts: a folder before its entries and again after
// them, and a symbolic link as itself.
static FTS *open_tree(const char *root) {
    char *const roots[] = {(char *)root, NULL};
    return fts_open(roots, FTS_PHYSICAL | FTS_NOCHDIR, NULL);
}

static bool remove_entry(const FTSENT *entry, char *error, size_t error_size) {
    int status = 0;
    switch (entry->fts_info) {
    case FTS_D:
        // A folder goes when fts returns to it after its entries; until then its owner must be
        // able to remove them.
        if ((entry->fts_statp->st_mode & S_IRWXU) != S_IRWXU) {
            (void)chmod(entry->fts_accpath, (mode_t)(entry->fts_statp->st_mode | S_IRWXU));
        }
        return true;
    case FTS_DP:
        status = rmdir(entry->fts_accpath);
        break;
    case FTS_DNR:
    case FTS_ERR:
    case FTS_NS:
        describe(error, error_size, "read", entry->fts_path, entry->fts_errno);
        return false;
    default:
        status = unlink(entry->fts_accpath);
        break;
    }
    if (status != 0) {
        describe(error, error_size, "remove", entry->fts_path, errno);
        return false;
    }
    return true;
}

bool pp_prefix_remove(const char *folder, char *error, size_t error_size) {
    struct stat status;
    if (lstat(folder, &status) != 0) {
        if (errno == ENOENT) {
            return true;
        }
        describe(error, error_size, "read", folder, errno);
        return false;
    }
    FTS *tree = open_tree(folder);
    if (tree == NULL) {
        describe(error, error_size, "open", folder, errno);
        return false;
    }
    bool removed = true;
    errno = 0;
    for (FTSENT *entry = fts_read(tree); removed && entry != NULL; entry = fts_read(tree)) {
        removed = remove_entry(entry, error, error_size);
    }
    if (removed && errno != 0) {
        describe(error, error_size, "read", folder, errno);
        removed = false;
    }
    (void)fts_close(tree);
    return removed;
}

static bool write_all(int descriptor, const char *data, size_t length) {
    while (length > 0U) {
        const ssize_t written = write(descriptor, data, length);
        if (written < 0 && errno == EINTR) {
            continue;
        }
        if (written <= 0) {
            return false;
        }
        data += written;
        length -= (size_t)written;
    }
    return true;
}

// Copies a file's data into a new file with the same permissions.
static bool copy_data(const char *source, const char *target, mode_t mode, char *error,
                      size_t error_size) {
    const int input = open(source, O_RDONLY | O_CLOEXEC | O_NOFOLLOW);
    if (input < 0) {
        describe(error, error_size, "open", source, errno);
        return false;
    }
    const int output = open(target, O_WRONLY | O_CREAT | O_EXCL | O_CLOEXEC | O_NOFOLLOW, mode);
    bool copied = output >= 0;
    int reason = output >= 0 ? 0 : errno;
    char buffer[PP_PREFIX_COPY_SIZE];
    while (copied) {
        const ssize_t count = read(input, buffer, sizeof buffer);
        if (count == 0) {
            break;
        }
        if (count < 0 && errno == EINTR) {
            continue;
        }
        if (count < 0 || !write_all(output, buffer, (size_t)count)) {
            reason = errno;
            copied = false;
        }
    }
    // The permissions that open gave the file went through the umask.
    if (copied && fchmod(output, mode) != 0) {
        reason = errno;
        copied = false;
    }
    (void)close(input);
    if (output >= 0 && close(output) != 0 && copied) {
        reason = errno;
        copied = false;
    }
    if (!copied) {
        describe(error, error_size, "copy", source, reason);
    }
    return copied;
}

static bool copy_file(const char *source, const char *target, mode_t mode, char *error,
                      size_t error_size) {
#ifdef __APPLE__
    // APFS clones a file without copying its data; another file system takes the copy below.
    if (clonefile(source, target, CLONE_NOFOLLOW) == 0) {
        return true;
    }
#endif
    return copy_data(source, target, mode, error, error_size);
}

static bool copy_link(const char *source, const char *target, char *error, size_t error_size) {
    char link[PATH_MAX];
    const ssize_t length = readlink(source, link, sizeof link - 1U);
    if (length < 0 || (size_t)length >= sizeof link - 1U) {
        describe(error, error_size, "read the link", source, length < 0 ? errno : ENAMETOOLONG);
        return false;
    }
    link[length] = '\0';
    if (symlink(link, target) != 0) {
        describe(error, error_size, "make the link", target, errno);
        return false;
    }
    return true;
}

static bool copy_entry(const FTSENT *entry, const char *target, char *error, size_t error_size) {
    const mode_t mode = (mode_t)(entry->fts_statp->st_mode & 07777U);
    switch (entry->fts_info) {
    case FTS_D:
        // Writable by its owner until its entries are in, then with its own permissions.
        if (mkdir(target, (mode_t)(mode | S_IRWXU)) != 0) {
            describe(error, error_size, "make", target, errno);
            return false;
        }
        return true;
    case FTS_DP:
        if (chmod(target, mode) != 0) {
            describe(error, error_size, "set the permissions of", target, errno);
            return false;
        }
        return true;
    case FTS_F:
        return copy_file(entry->fts_accpath, target, mode, error, error_size);
    case FTS_SL:
    case FTS_SLNONE:
        return copy_link(entry->fts_accpath, target, error, error_size);
    case FTS_DNR:
    case FTS_ERR:
    case FTS_NS:
        describe(error, error_size, "read", entry->fts_path, entry->fts_errno);
        return false;
    default:
        (void)snprintf(error, error_size,
                       "cannot copy %s, which is not a folder, a file or a symbolic link",
                       entry->fts_path);
        return false;
    }
}

bool pp_prefix_copy(const char *source, const char *target, char *error, size_t error_size) {
    // fts names every entry by the root as given, so the root loses any slash at its end.
    char root[PATH_MAX];
    size_t root_length = strlen(source);
    while (root_length > 1U && source[root_length - 1U] == '/') {
        root_length--;
    }
    if (root_length >= sizeof root) {
        (void)snprintf(error, error_size, "the path of the template prefix is too long");
        return false;
    }
    memcpy(root, source, root_length);
    root[root_length] = '\0';
    // The walk copies a symbolic link as a link and follows none, its root included, since the
    // removal shares it and must not empty what a link names. The copy of a link to the template
    // would therefore be a link to it, through which every run wrote into the template itself.
    struct stat status;
    if (lstat(root, &status) != 0) {
        describe(error, error_size, "read", root, errno);
        return false;
    }
    if (!S_ISDIR(status.st_mode)) {
        (void)snprintf(error, error_size, "cannot copy %s, which is not a folder", root);
        return false;
    }
    FTS *tree = open_tree(root);
    if (tree == NULL) {
        describe(error, error_size, "open", root, errno);
        return false;
    }
    bool copied = true;
    errno = 0;
    for (FTSENT *entry = fts_read(tree); copied && entry != NULL; entry = fts_read(tree)) {
        char destination[PATH_MAX];
        const int length = snprintf(destination, sizeof destination, "%s%s", target,
                                    entry->fts_path + root_length);
        if (length <= 0 || (size_t)length >= sizeof destination) {
            (void)snprintf(error, error_size, "a path in the copy of %s is too long", root);
            copied = false;
        } else {
            copied = copy_entry(entry, destination, error, error_size);
        }
    }
    if (copied && errno != 0) {
        describe(error, error_size, "read", root, errno);
        copied = false;
    }
    (void)fts_close(tree);
    return copied;
}
