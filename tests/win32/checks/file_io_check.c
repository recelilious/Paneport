// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

#include "file_io_check.h"

#include <stdint.h>
#include <string.h>
#include <wchar.h>

#define WIN32_LEAN_AND_MEAN
#include <windows.h>

#define PP_FILE_IO_SIZE (1024U * 1024U)
#define PP_FILE_IO_CHUNK (64U * 1024U)
#define PP_FILE_IO_TRUNCATED 1000U
// A view of a file mapping starts at a multiple of the allocation granularity, 64 KiB.
#define PP_FILE_IO_VIEW_OFFSET (64U * 1024U)
#define PP_FILE_IO_VIEW_SIZE (64U * 1024U)
#define PP_FILE_IO_PATH_MAX 512U

struct file_paths {
    wchar_t folder[PP_FILE_IO_PATH_MAX];
    wchar_t data[PP_FILE_IO_PATH_MAX];
    wchar_t copy[PP_FILE_IO_PATH_MAX];
    wchar_t renamed[PP_FILE_IO_PATH_MAX];
    wchar_t pattern[PP_FILE_IO_PATH_MAX];
};

static unsigned char pattern_byte(size_t offset) {
    return (unsigned char)((offset * 7U + (offset >> 9U)) & 0xFFU);
}

static bool append(wchar_t *path, const wchar_t *text) {
    size_t used = wcslen(path);
    size_t added = wcslen(text);
    if (used + added + 1U > PP_FILE_IO_PATH_MAX) {
        return false;
    }
    memcpy(path + used, text, (added + 1U) * sizeof(wchar_t));
    return true;
}

static void decimal(wchar_t *digits, DWORD value) {
    wchar_t reversed[11];
    size_t count = 0;
    do {
        reversed[count] = (wchar_t)(L'0' + (wchar_t)(value % 10U));
        count++;
        value /= 10U;
    } while (value != 0U && count < 10U);
    for (size_t index = 0; index < count; index++) {
        digits[index] = reversed[count - 1U - index];
    }
    digits[count] = L'\0';
}

static bool make_path(wchar_t *path, const wchar_t *folder, const wchar_t *name) {
    path[0] = L'\0';
    return append(path, folder) && append(path, L"\\") && append(path, name);
}

// The folder is named after the process, so that a run never meets the leftovers of another.
static bool build_paths(struct file_paths *paths, const wchar_t *temporary) {
    wchar_t process[11];
    decimal(process, GetCurrentProcessId());
    paths->folder[0] = L'\0';
    return append(paths->folder, temporary) && append(paths->folder, L"paneport-file-io-") &&
           append(paths->folder, process) && make_path(paths->data, paths->folder, L"data.bin") &&
           make_path(paths->copy, paths->folder, L"copy.bin") &&
           make_path(paths->renamed, paths->folder, L"renamed.bin") &&
           make_path(paths->pattern, paths->folder, L"*");
}

static bool write_pattern(HANDLE file) {
    unsigned char chunk[PP_FILE_IO_CHUNK];
    for (size_t start = 0; start < PP_FILE_IO_SIZE; start += PP_FILE_IO_CHUNK) {
        for (size_t index = 0; index < PP_FILE_IO_CHUNK; index++) {
            chunk[index] = pattern_byte(start + index);
        }
        DWORD written = 0;
        if (WriteFile(file, chunk, PP_FILE_IO_CHUNK, &written, NULL) == FALSE ||
            written != PP_FILE_IO_CHUNK) {
            return false;
        }
    }
    return FlushFileBuffers(file) != FALSE;
}

static bool read_back(HANDLE file) {
    LARGE_INTEGER size;
    LARGE_INTEGER start = {.QuadPart = 0};
    if (GetFileSizeEx(file, &size) == FALSE || size.QuadPart != PP_FILE_IO_SIZE ||
        SetFilePointerEx(file, start, NULL, FILE_BEGIN) == FALSE) {
        return false;
    }
    unsigned char chunk[PP_FILE_IO_CHUNK];
    for (size_t offset = 0; offset < PP_FILE_IO_SIZE; offset += PP_FILE_IO_CHUNK) {
        DWORD read = 0;
        if (ReadFile(file, chunk, PP_FILE_IO_CHUNK, &read, NULL) == FALSE ||
            read != PP_FILE_IO_CHUNK) {
            return false;
        }
        for (size_t index = 0; index < PP_FILE_IO_CHUNK; index++) {
            if (chunk[index] != pattern_byte(offset + index)) {
                return false;
            }
        }
    }
    return true;
}

static bool mapped_view_matches(HANDLE file) {
    HANDLE mapping = CreateFileMappingW(file, NULL, PAGE_READONLY, 0, 0, NULL);
    if (mapping == NULL) {
        return false;
    }
    const unsigned char *view =
        MapViewOfFile(mapping, FILE_MAP_READ, 0, PP_FILE_IO_VIEW_OFFSET, PP_FILE_IO_VIEW_SIZE);
    bool matches = view != NULL;
    for (size_t index = 0; matches && index < PP_FILE_IO_VIEW_SIZE; index++) {
        matches = view[index] == pattern_byte(PP_FILE_IO_VIEW_OFFSET + index);
    }
    if (view != NULL) {
        UnmapViewOfFile(view);
    }
    CloseHandle(mapping);
    return matches;
}

static bool truncate_file(HANDLE file) {
    LARGE_INTEGER position = {.QuadPart = PP_FILE_IO_TRUNCATED};
    LARGE_INTEGER size;
    return SetFilePointerEx(file, position, NULL, FILE_BEGIN) != FALSE &&
           SetEndOfFile(file) != FALSE && GetFileSizeEx(file, &size) != FALSE &&
           size.QuadPart == PP_FILE_IO_TRUNCATED;
}

static bool is_missing(const wchar_t *path) {
    return GetFileAttributesW(path) == INVALID_FILE_ATTRIBUTES &&
           (GetLastError() == ERROR_FILE_NOT_FOUND || GetLastError() == ERROR_PATH_NOT_FOUND);
}

static bool has_size(const wchar_t *path, DWORD expected) {
    WIN32_FILE_ATTRIBUTE_DATA data;
    return GetFileAttributesExW(path, GetFileExInfoStandard, &data) != FALSE &&
           data.nFileSizeHigh == 0U && data.nFileSizeLow == expected;
}

// The folder must hold exactly data.bin and renamed.bin, in any order.
static bool lists_two_files(const wchar_t *pattern) {
    WIN32_FIND_DATAW found;
    HANDLE search = FindFirstFileW(pattern, &found);
    if (search == INVALID_HANDLE_VALUE) {
        return false;
    }
    unsigned data = 0;
    unsigned renamed = 0;
    unsigned other = 0;
    do {
        if (wcscmp(found.cFileName, L".") == 0 || wcscmp(found.cFileName, L"..") == 0) {
            continue;
        }
        if (wcscmp(found.cFileName, L"data.bin") == 0) {
            data++;
        } else if (wcscmp(found.cFileName, L"renamed.bin") == 0) {
            renamed++;
        } else {
            other++;
        }
    } while (FindNextFileW(search, &found) != FALSE);
    bool ended = GetLastError() == ERROR_NO_MORE_FILES;
    FindClose(search);
    return ended && data == 1U && renamed == 1U && other == 0U;
}

static bool check_drive_and_folder(struct pp_test_run *run, const char *area, wchar_t *temporary) {
    bool drive = (GetLogicalDrives() & (1U << 2U)) != 0U;
    pp_test_line(run, "%s: drive C: %s", area, drive ? "present" : "missing");
    pp_test_check(run, drive, "GetLogicalDrives reports drive C:");

    DWORD length = GetTempPathW(PP_FILE_IO_PATH_MAX, temporary);
    bool on_c = length > 2U && length < PP_FILE_IO_PATH_MAX &&
                (temporary[0] == L'C' || temporary[0] == L'c') && temporary[1] == L':';
    pp_test_line(run, "%s: temporary folder %s drive C:", area, on_c ? "on" : "not on");
    pp_test_check(run, on_c, "GetTempPath gives a folder on drive C:");
    return drive && on_c;
}

static bool check_contents(struct pp_test_run *run, const char *area,
                           const struct file_paths *paths) {
    HANDLE file = CreateFileW(paths->data, GENERIC_READ | GENERIC_WRITE, 0, NULL, CREATE_NEW,
                              FILE_ATTRIBUTE_NORMAL, NULL);
    if (!pp_test_check(run, file != INVALID_HANDLE_VALUE, "CreateFile makes a new file")) {
        return false;
    }
    bool written = pp_test_check(run, write_pattern(file) && read_back(file),
                                 "the file reads back what was written");
    pp_test_line(run, "%s: create, write and read back %u bytes: %s", area, PP_FILE_IO_SIZE,
                 written ? "pass" : "fail");
    bool mapped = pp_test_check(run, written && mapped_view_matches(file),
                                "a mapped view of the file matches its contents");
    pp_test_line(run, "%s: a mapped view matches the file: %s", area, mapped ? "pass" : "fail");
    bool truncated = pp_test_check(run, truncate_file(file), "SetEndOfFile truncates the file");
    CloseHandle(file);
    return written && mapped && truncated;
}

static bool check_names(struct pp_test_run *run, const char *area, const struct file_paths *paths) {
    bool copied = pp_test_check(run,
                                CopyFileW(paths->data, paths->copy, TRUE) != FALSE &&
                                    has_size(paths->copy, PP_FILE_IO_TRUNCATED),
                                "CopyFile copies the truncated file");
    bool renamed =
        pp_test_check(run,
                      MoveFileExW(paths->copy, paths->renamed, 0) != FALSE &&
                          is_missing(paths->copy) && has_size(paths->renamed, PP_FILE_IO_TRUNCATED),
                      "MoveFileEx renames the copy");
    bool listed = pp_test_check(run, lists_two_files(paths->pattern),
                                "FindFirstFile and FindNextFile list exactly the two files");
    bool passed = copied && renamed && listed;
    pp_test_line(run, "%s: truncate, copy, rename and list: %s", area, passed ? "pass" : "fail");
    return passed;
}

static bool remove_all(struct pp_test_run *run, const char *area, const struct file_paths *paths) {
    bool passed = DeleteFileW(paths->data) != FALSE;
    passed = DeleteFileW(paths->renamed) != FALSE && passed;
    passed = RemoveDirectoryW(paths->folder) != FALSE && passed;
    passed = pp_test_check(run, passed && is_missing(paths->folder),
                           "DeleteFile and RemoveDirectory remove everything");
    pp_test_line(run, "%s: delete the files and the folder: %s", area, passed ? "pass" : "fail");
    return passed;
}

bool pp_file_io_report(struct pp_test_run *run, const char *area) {
    wchar_t temporary[PP_FILE_IO_PATH_MAX];
    if (!check_drive_and_folder(run, area, temporary)) {
        return false;
    }
    struct file_paths paths;
    if (!pp_test_check(run, build_paths(&paths, temporary), "the test paths fit")) {
        return false;
    }
    if (!pp_test_check(run, CreateDirectoryW(paths.folder, NULL) != FALSE,
                       "CreateDirectory makes the test folder")) {
        return false;
    }
    bool passed = check_contents(run, area, &paths);
    if (passed) {
        passed = check_names(run, area, &paths);
    }
    // Whatever failed, the folder and what is left in it go.
    DeleteFileW(paths.copy);
    passed = remove_all(run, area, &paths) && passed;
    return passed;
}
