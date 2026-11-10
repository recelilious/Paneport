// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

// Prepares the crash record folder for the writer of crash_record_writer.c. pp_init calls it
// on the main thread; nothing here runs in a signal handler.

#include <stdio.h>
#include <string.h>
#include <sys/stat.h>
#include <unistd.h>

#include "crash_record.h"
#include "error.h"

pp_status_t pp_crash_record_prepare(const char *folder) {
    // The writer opens the paths at fault time, when the working folder can be anything.
    if (folder[0] != '/') {
        return pp_error_set(PP_E_INVALID_ARGUMENT,
                            "The crash record folder must be an absolute path.");
    }

    size_t length = strlen(folder);
    while (length > 1U && folder[length - 1U] == '/') {
        length--;
    }
    if (length + 1U + sizeof(PP_CRASH_RECORD_TEMPORARY_NAME) > PP_CRASH_RECORD_PATH_SIZE) {
        return pp_error_set(
            PP_E_INVALID_ARGUMENT, "The crash record folder's path is longer than %zu bytes.",
            (size_t)PP_CRASH_RECORD_PATH_SIZE - 1U - sizeof(PP_CRASH_RECORD_TEMPORARY_NAME));
    }

    char path[PP_CRASH_RECORD_PATH_SIZE];
    memcpy(path, folder, length);
    path[length] = '\0';

    struct stat information;
    if (stat(path, &information) != 0 || !S_ISDIR(information.st_mode)) {
        return pp_error_set(PP_E_INVALID_ARGUMENT,
                            "The crash record folder does not exist or is not a folder.");
    }
    if (access(path, W_OK) != 0) {
        return pp_error_set(PP_E_IO, "The crash record folder cannot be written.");
    }

    char record_path[PP_CRASH_RECORD_PATH_SIZE];
    char temporary_path[PP_CRASH_RECORD_PATH_SIZE];
    (void)snprintf(record_path, sizeof record_path, "%s/%s", path, PP_CRASH_RECORD_FILE_NAME);
    (void)snprintf(temporary_path, sizeof temporary_path, "%s/%s", path,
                   PP_CRASH_RECORD_TEMPORARY_NAME);
    pp_crash_record_set_paths(record_path, temporary_path);
    return PP_OK;
}
