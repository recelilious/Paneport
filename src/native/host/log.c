// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

#include "log.h"

#include <stdio.h>
#include <string.h>

#include "platform.h"

// The marker that ends a line which did not fit.
static const char k_cut_marker[] = "...";

size_t pp_utf8_cut(const char *text, size_t length) {
    // Step back over the continuation bytes (10xxxxxx) of the last character.
    size_t start = length;
    while (start > 0U && ((unsigned char)text[start - 1U] & 0xC0U) == 0x80U) {
        start--;
    }
    if (start == 0U) {
        return 0U;
    }

    // The lead byte says how long the last character is; keep it only when it is whole.
    const unsigned char lead = (unsigned char)text[start - 1U];
    size_t needed = 1U;
    if ((lead & 0xE0U) == 0xC0U) {
        needed = 2U;
    } else if ((lead & 0xF0U) == 0xE0U) {
        needed = 3U;
    } else if ((lead & 0xF8U) == 0xF0U) {
        needed = 4U;
    }
    return length - (start - 1U) >= needed ? length : start - 1U;
}

void pp_log_format(pp_log_entry_t *entry, int32_t level, const char *format, va_list arguments) {
    entry->level = level;
    entry->thread_id = pp_platform_thread_id();

    const int written = vsnprintf(entry->text, sizeof entry->text, format, arguments);
    if (written < 0) {
        (void)snprintf(entry->text, sizeof entry->text, "%s",
                       "(The host could not format a log line.)");
        return;
    }
    if ((size_t)written < sizeof entry->text) {
        return;
    }

    // The line did not fit: keep whole characters only, and end it with the marker.
    const size_t room = sizeof entry->text - sizeof k_cut_marker;
    const size_t kept = pp_utf8_cut(entry->text, room);
    memcpy(entry->text + kept, k_cut_marker, sizeof k_cut_marker);
}

pp_status_t pp_log(int32_t level, const char *format, ...) {
    pp_log_entry_t entry;
    va_list arguments;
    va_start(arguments, format);
    pp_log_format(&entry, level, format, arguments);
    va_end(arguments);
    return pp_event_thread_post_log(&entry);
}
