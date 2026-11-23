// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

// The core of the fork and exec trap (trap.h): its switch, its count and its reports. A report
// is formatted here by hand rather than with the C library's formatting, because a refused call
// may come from a signal handler, where only dladdr, which names the caller, is beyond what a
// handler may call.

#include "trap.h"

#include <dlfcn.h>
#include <signal.h>
#include <stdatomic.h>
#include <stdint.h>
#include <string.h>
#include <unistd.h>

#include "trap_core.h"

// The trap's state: a process-wide singleton (C-0010 section 3), since the calls that it
// intercepts come from any thread, Wine's among them. A report is complete before its flag is
// set, so a reader that sees the flag sees the whole report.
static struct {
    atomic_bool armed;
    atomic_uint count;
    atomic_bool kept[PP_TRAP_REPORTS];
    char reports[PP_TRAP_REPORTS][PP_TRAP_REPORT_SIZE];
} s_trap;

// A text that grows in a buffer of a fixed size, cut where it would not fit, and always
// NUL-terminated.
typedef struct text_t {
    char *data;
    size_t size;
    size_t used;
} text_t;

static void append(text_t *text, const char *part) {
    while (*part != '\0' && text->used + 1U < text->size) {
        text->data[text->used] = *part;
        text->used++;
        part++;
    }
    text->data[text->used] = '\0';
}

static void append_hex(text_t *text, uintptr_t value) {
    static const char k_digits[] = "0123456789abcdef";
    char digits[(2U * sizeof value) + 3U];
    size_t start = sizeof digits - 1U;
    digits[start] = '\0';
    do {
        start--;
        digits[start] = k_digits[value & 0xFU];
        value >>= 4U;
    } while (value != 0U);
    start--;
    digits[start] = 'x';
    start--;
    digits[start] = '0';
    append(text, digits + start);
}

static const char *signal_name(int signal_number) {
    if (signal_number == SIGUSR1) {
        return "SIGUSR1";
    }
    if (signal_number == SIGQUIT) {
        return "SIGQUIT";
    }
    return "a signal";
}

static const char *base_name(const char *path) {
    const char *slash = strrchr(path, '/');
    return slash == NULL ? path : slash + 1;
}

// Writes "<call> from <image>!<symbol>+<offset>" into buffer, or "<image>+<offset from the
// image's start>" when dladdr finds no symbol, or the bare address when it finds no image.
static void describe(char *buffer, size_t size, const char *call, int signal_number,
                     const void *caller) {
    text_t text = {buffer, size, 0U};
    buffer[0] = '\0';
    append(&text, call);
    if (signal_number != 0) {
        append(&text, "(");
        append(&text, signal_name(signal_number));
        append(&text, ")");
    }
    append(&text, " from ");
    const uintptr_t address = (uintptr_t)caller;
    Dl_info info;
    memset(&info, 0, sizeof info);
    if (caller == NULL || dladdr(caller, &info) == 0 || info.dli_fname == NULL) {
        append_hex(&text, address);
        return;
    }
    append(&text, base_name(info.dli_fname));
    if (info.dli_sname != NULL && info.dli_saddr != NULL && (uintptr_t)info.dli_saddr <= address) {
        append(&text, "!");
        append(&text, info.dli_sname);
        append(&text, "+");
        append_hex(&text, address - (uintptr_t)info.dli_saddr);
    } else {
        append(&text, "+");
        append_hex(&text, address - (uintptr_t)info.dli_fbase);
    }
}

void pp_trap_arm(void) { atomic_store_explicit(&s_trap.armed, true, memory_order_seq_cst); }

void pp_trap_disarm(void) { atomic_store_explicit(&s_trap.armed, false, memory_order_seq_cst); }

uint32_t pp_trap_count(void) {
    return (uint32_t)atomic_load_explicit(&s_trap.count, memory_order_acquire);
}

bool pp_trap_report(uint32_t index, char *buffer, size_t size) {
    if (buffer == NULL || size == 0U || index >= PP_TRAP_REPORTS ||
        !atomic_load_explicit(&s_trap.kept[index], memory_order_acquire)) {
        return false;
    }
    text_t text = {buffer, size, 0U};
    append(&text, s_trap.reports[index]);
    return true;
}

bool pp_trap_refuses(void) { return atomic_load_explicit(&s_trap.armed, memory_order_acquire); }

bool pp_trap_refuses_signal(int signal_number) {
    if (signal_number != SIGUSR1 && signal_number != SIGQUIT) {
        return false;
    }
    return pp_trap_refuses();
}

void pp_trap_record(const char *call, int signal_number, const void *caller) {
    char report[PP_TRAP_REPORT_SIZE];
    describe(report, sizeof report, call, signal_number, caller);
    const unsigned index = atomic_fetch_add_explicit(&s_trap.count, 1U, memory_order_acq_rel);
    if (index < PP_TRAP_REPORTS) {
        memcpy(s_trap.reports[index], report, sizeof report);
        atomic_store_explicit(&s_trap.kept[index], true, memory_order_release);
    }
    // At once as well, for a run that ends before anyone reads the reports. One write, so that
    // the line stays whole beside other writers of standard error; the buffer leaves room for its
    // line feed.
    char line[PP_TRAP_REPORT_SIZE + 32U];
    text_t text = {line, sizeof line - 1U, 0U};
    append(&text, "paneport trap: refused ");
    append(&text, report);
    line[text.used] = '\n';
    (void)write(STDERR_FILENO, line, text.used + 1U);
}
