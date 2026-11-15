// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

// File operations on drive C:, in a folder of their own below the temporary folder, which
// Windows and a Wine prefix both keep on C:. No line names a path, because a path holds the
// user's name.

#ifndef PP_FILE_IO_CHECK_H
#define PP_FILE_IO_CHECK_H

#include "test_output.h"

// Checks that GetLogicalDrives reports C: and that the temporary folder is on it, then creates,
// writes, reads back, maps, truncates, copies, renames, lists and deletes files in a new folder
// there, and removes the folder. Prints one line per step and returns whether every step passed.
bool pp_file_io_report(struct pp_test_run *run, const char *area);

#endif
