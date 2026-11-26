// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

// The prefixes of single mode: before each run, the harness removes the run's prefix and makes it
// a fresh copy of the template prefix, so that no run sees what an earlier one left in it
// (docs/design/runtime-macos-harness.md section 5.1). Every function here runs on the harness's
// only thread.

#ifndef PP_HARNESS_PREFIX_H
#define PP_HARNESS_PREFIX_H

#include <stdbool.h>
#include <stddef.h>

// Removes folder with everything in it, never following a symbolic link; a folder that does not
// exist counts as removed. On failure it writes a message into error.
bool pp_prefix_remove(const char *folder, char *error, size_t error_size);

// Makes target, which must not exist, a copy of the folder source: folders and files with their
// permissions, and symbolic links as links, never followed. source must be the folder itself:
// anything else, a symbolic link to a folder among it, is refused before anything is made. On
// macOS each file is cloned, which APFS does without copying its data, and copied where cloning
// fails. On failure it writes a message into error, and target may hold part of the copy.
bool pp_prefix_copy(const char *source, const char *target, char *error, size_t error_size);

// Makes target a copy of source as pp_prefix_copy does, but copies the data of every file, as a
// file system that cannot clone does. Only test_prefix calls it, never a run: the runner's
// template and prefixes share one APFS volume, where every clone works, so the copy of the data
// would go untested there otherwise.
bool pp_prefix_copy_without_clones(const char *source, const char *target, char *error,
                                   size_t error_size);

#endif
