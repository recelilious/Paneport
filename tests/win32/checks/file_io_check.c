// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

#include "file_io_check.h"

#include <stdint.h>
#include <string.h>
#include <wchar.h>
#include <wctype.h>

#define WIN32_LEAN_AND_MEAN
#include <windows.h>

#define PP_FILE_IO_SIZE 1048576U // 1 MiB
#define PP_FILE_IO_CHUNK 65536U  // 64 KiB
#define PP_FILE_IO_TRUNCATED 1000U
// A view of a file mapping starts at a multiple of the allocation granularity, 64 KiB.
#define PP_FILE_IO_VIEW_OFFSET 65536U
#define PP_FILE_IO_VIEW_SIZE 65536U
#define PP_FILE_IO_PATH_MAX 512U

struct file_paths {
    wchar_t folder[PP_FILE_IO_PATH_MAX];
    wchar_t data[PP_FILE_IO_PATH_MAX];
    wchar_t copy[PP_FILE_IO_PATH_MAX];
    wchar_t renamed[PP_FILE_IO_PATH_MAX];
    wchar_t pattern[PP_FILE_IO_PATH_MAX];
};

static unsigned char pattern_byte(size_t offset) {
    return (unsigned char)(((offset * 7U) + (offset >> 9U)) & 0xFFU);
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
    if (!append(path, folder) || !append(path, L"\\")) {
        return false;
    }
    return append(path, name);
}

// The folder is named after the process, so that a run never meets the leftovers of another.
static bool build_paths(struct file_paths *paths, const wchar_t *temporary) {
    wchar_t process[11];
    decimal(process, GetCurrentProcessId());
    paths->folder[0] = L'\0';
    if (!append(paths->folder, temporary) || !append(paths->folder, L"paneport-file-io-") ||
        !append(paths->folder, process)) {
        return false;
    }
    if (!make_path(paths->data, paths->folder, L"data.bin") ||
        !make_path(paths->copy, paths->folder, L"copy.bin") ||
        !make_path(paths->renamed, paths->folder, L"renamed.bin")) {
        return false;
    }
    return make_path(paths->pattern, paths->folder, L"*");
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
    if (SetFilePointerEx(file, position, NULL, FILE_BEGIN) == FALSE ||
        SetEndOfFile(file) == FALSE || GetFileSizeEx(file, &size) == FALSE) {
        return false;
    }
    return size.QuadPart == PP_FILE_IO_TRUNCATED;
}

static bool is_missing(const wchar_t *path) {
    if (GetFileAttributesW(path) != INVALID_FILE_ATTRIBUTES) {
        return false;
    }
    DWORD error = GetLastError();
    if (error == ERROR_FILE_NOT_FOUND) {
        return true;
    }
    return error == ERROR_PATH_NOT_FOUND;
}

static bool has_size(const wchar_t *path, DWORD expected) {
    WIN32_FILE_ATTRIBUTE_DATA data;
    if (GetFileAttributesExW(path, GetFileExInfoStandard, &data) == FALSE ||
        data.nFileSizeHigh != 0U) {
        return false;
    }
    return data.nFileSizeLow == expected;
}

// What a listing of the test folder found.
struct listing {
    unsigned data;
    unsigned renamed;
    unsigned other;
    bool ended;
};

static struct listing list_folder(const wchar_t *pattern) {
    struct listing found = {0U, 0U, 0U, false};
    WIN32_FIND_DATAW entry;
    HANDLE search = FindFirstFileW(pattern, &entry);
    if (search == INVALID_HANDLE_VALUE) {
        return found;
    }
    do {
        if (wcscmp(entry.cFileName, L".") == 0 || wcscmp(entry.cFileName, L"..") == 0) {
            continue;
        }
        if (wcscmp(entry.cFileName, L"data.bin") == 0) {
            found.data++;
        } else if (wcscmp(entry.cFileName, L"renamed.bin") == 0) {
            found.renamed++;
        } else {
            found.other++;
        }
    } while (FindNextFileW(search, &entry) != FALSE);
    found.ended = GetLastError() == ERROR_NO_MORE_FILES;
    FindClose(search);
    return found;
}

static bool check_drive_and_folder(struct pp_test_run *run, const char *area, wchar_t *temporary) {
    unsigned failures = run->failures;
    bool drive = (GetLogicalDrives() & (1U << 2U)) != 0U;
    if (drive) {
        pp_test_line(run, "%s: drive C: present", area);
    } else {
        pp_test_line(run, "%s: drive C: missing", area);
    }
    pp_test_check(run, drive, "GetLogicalDrives reports drive C:");

    DWORD length = GetTempPathW(PP_FILE_IO_PATH_MAX, temporary);
    bool on_c = false;
    if (length > 2U && length < PP_FILE_IO_PATH_MAX && temporary[1] == L':') {
        on_c = towupper(temporary[0]) == L'C';
    }
    if (on_c) {
        pp_test_line(run, "%s: temporary folder on drive C:", area);
    } else {
        pp_test_line(run, "%s: temporary folder not on drive C:", area);
    }
    pp_test_check(run, on_c, "GetTempPath gives a folder on drive C:");
    return run->failures == failures;
}

static bool check_contents(struct pp_test_run *run, const char *area,
                           const struct file_paths *paths) {
    unsigned failures = run->failures;
    HANDLE file = CreateFileW(paths->data, GENERIC_READ | GENERIC_WRITE, 0, NULL, CREATE_NEW,
                              FILE_ATTRIBUTE_NORMAL, NULL);
    if (!pp_test_check(run, file != INVALID_HANDLE_VALUE, "CreateFile makes a new file")) {
        return false;
    }
    bool written = pp_test_check(run, write_pattern(file), "WriteFile writes the file");
    if (written) {
        written = pp_test_check(run, read_back(file), "the file reads back what was written");
    }
    pp_test_line(run, "%s: create, write and read back %u bytes: %s", area, PP_FILE_IO_SIZE,
                 pp_test_verdict(written));
    bool mapped = false;
    if (written) {
        mapped = pp_test_check(run, mapped_view_matches(file),
                               "a mapped view of the file matches its contents");
    }
    pp_test_line(run, "%s: a mapped view matches the file: %s", area, pp_test_verdict(mapped));
    pp_test_check(run, truncate_file(file), "SetEndOfFile truncates the file");
    CloseHandle(file);
    return run->failures == failures;
}

static bool check_names(struct pp_test_run *run, const char *area, const struct file_paths *paths) {
    unsigned failures = run->failures;
    if (pp_test_check(run, CopyFileW(paths->data, paths->copy, TRUE) != FALSE,
                      "CopyFile copies the truncated file")) {
        pp_test_check(run, has_size(paths->copy, PP_FILE_IO_TRUNCATED),
                      "the copy has the truncated size");
        if (pp_test_check(run, MoveFileExW(paths->copy, paths->renamed, 0) != FALSE,
                          "MoveFileEx renames the copy")) {
            pp_test_check(run, is_missing(paths->copy), "the old name is gone after the rename");
            pp_test_check(run, has_size(paths->renamed, PP_FILE_IO_TRUNCATED),
                          "the renamed file keeps its size");
        }
    }
    struct listing found = list_folder(paths->pattern);
    pp_test_check(run, found.ended, "FindFirstFile and FindNextFile list the whole folder");
    pp_test_check(run, found.data == 1U, "the listing holds data.bin once");
    pp_test_check(run, found.renamed == 1U, "the listing holds renamed.bin once");
    pp_test_check(run, found.other == 0U, "the listing holds no other file");
    bool passed = run->failures == failures;
    pp_test_line(run, "%s: truncate, copy, rename and list: %s", area, pp_test_verdict(passed));
    return passed;
}

static bool remove_all(struct pp_test_run *run, const char *area, const struct file_paths *paths) {
    unsigned failures = run->failures;
    pp_test_check(run, DeleteFileW(paths->data) != FALSE, "DeleteFile removes data.bin");
    pp_test_check(run, DeleteFileW(paths->renamed) != FALSE, "DeleteFile removes renamed.bin");
    pp_test_check(run, RemoveDirectoryW(paths->folder) != FALSE,
                  "RemoveDirectory removes the test folder");
    pp_test_check(run, is_missing(paths->folder), "the test folder is gone");
    bool passed = run->failures == failures;
    pp_test_line(run, "%s: delete the files and the folder: %s", area, pp_test_verdict(passed));
    return passed;
}

bool pp_file_io_report(struct pp_test_run *run, const char *area) {
    unsigned failures = run->failures;
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
    if (check_contents(run, area, &paths)) {
        check_names(run, area, &paths);
    }
    // Whatever failed, the folder and what is left in it go.
    DeleteFileW(paths.copy);
    remove_all(run, area, &paths);
    return run->failures == failures;
}
