// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

// S2.2.5: the installed check, and the last-resort handler that the host installs when it is
// loaded. The check passes after pp_init and fails, naming the signal, when a handler was
// replaced; the last-resort handler takes only signals that still have the default action
// (docs/design/signals.md sections 5 and 7).

#include <signal.h>
#include <stdbool.h>
#include <stdint.h>
#include <string.h>
#include <sys/wait.h>

#include "paneport/host.h"
#include "paneport/testing.h"
#include "pp_check.h"
#include "signal_test_support.h"
#include "signals/signal_state.h"
#include "test_support.h"

// The documented singleton of this program (C-0010 section 3): the path that the program
// started with, which a case needs to start a child.
static const char *s_program;

static void replacement_handler(int signal_number) { (void)signal_number; }

static void passes_after_init(void) {
    pp_start_host(NULL);
    int32_t replaced = -1;
    PP_CHECK_EQUAL(pp_test_signals_check_installed(&replaced), PP_OK);
    PP_CHECK_EQUAL(replaced, 0);
    PP_CHECK_EQUAL(pp_test_signals_check_installed(NULL), PP_OK);
}

static void fails_naming_replaced_signal(void) {
    pp_start_host(NULL);
    struct sigaction action;
    memset(&action, 0, sizeof action);
    action.sa_handler = replacement_handler;
    PP_CHECK_EQUAL(sigemptyset(&action.sa_mask), 0);
    struct sigaction previous;
    PP_CHECK_EQUAL(sigaction(SIGBUS, &action, &previous), 0);

    int32_t replaced = 0;
    PP_CHECK_EQUAL(pp_test_signals_check_installed(&replaced), PP_E_SYSTEM);
    PP_CHECK_EQUAL(replaced, SIGBUS);
    PP_CHECK(strstr(pp_last_error(), "SIGBUS") != NULL);

    PP_CHECK_EQUAL(sigaction(SIGBUS, &previous, NULL), 0);
    PP_CHECK_EQUAL(pp_test_signals_check_installed(&replaced), PP_OK);
    PP_CHECK_EQUAL(replaced, 0);
}

static void fails_before_init(void) {
    int32_t replaced = -1;
    PP_CHECK_EQUAL(pp_test_signals_check_installed(&replaced), PP_E_NOT_INITIALIZED);
    PP_CHECK_EQUAL(replaced, 0);
}

static bool has_last_resort(int signal_number) {
    struct sigaction current;
    PP_CHECK_EQUAL(sigaction(signal_number, NULL, &current), 0);
    if ((current.sa_flags & SA_SIGINFO) == 0) {
        return false;
    }
    return current.sa_sigaction == pp_signal_last_resort;
}

// Before pp_init, each of the six signals has the last-resort handler, because the presets
// leave the fault signals to the host (signals.md section 11).
static void last_resort_installed_at_load(void) {
    for (size_t index = 0U; index < PP_SIGNAL_COUNT; index++) {
        PP_CHECK(has_last_resort(pp_test_signal_at(index)));
    }
}

// Child: it started with SIGTRAP ignored, which its parent arranged; the host left SIGTRAP
// alone and took the other five.
static void child_keeps_ignored(const char *folder) {
    (void)folder;
    struct sigaction current;
    PP_CHECK_EQUAL(sigaction(SIGTRAP, NULL, &current), 0);
    PP_CHECK((current.sa_flags & SA_SIGINFO) == 0 && current.sa_handler == SIG_IGN);
    for (size_t index = 0U; index < PP_SIGNAL_COUNT; index++) {
        const int signal_number = pp_test_signal_at(index);
        PP_CHECK(signal_number == SIGTRAP || has_last_resort(signal_number));
    }
}

static void last_resort_only_where_default(void) {
    struct sigaction ignore;
    memset(&ignore, 0, sizeof ignore);
    ignore.sa_handler = SIG_IGN;
    PP_CHECK_EQUAL(sigemptyset(&ignore.sa_mask), 0);
    struct sigaction previous;
    PP_CHECK_EQUAL(sigaction(SIGTRAP, &ignore, &previous), 0);
    const int status = pp_run_child(s_program, "keeps_ignored", "-");
    PP_CHECK_EQUAL(sigaction(SIGTRAP, &previous, NULL), 0);
    PP_CHECK(WIFEXITED(status));
    PP_CHECK_EQUAL(WEXITSTATUS(status), 0);
}

int main(int argc, char **argv) {
    s_program = argv[0];
    static const pp_test_case_t cases[] = {
        {"passes_after_init", passes_after_init},
        {"fails_naming_replaced_signal", fails_naming_replaced_signal},
        {"fails_before_init", fails_before_init},
        {"last_resort_installed_at_load", last_resort_installed_at_load},
        {"last_resort_only_where_default", last_resort_only_where_default},
    };
    static const pp_child_case_t children[] = {
        {"keeps_ignored", child_keeps_ignored},
    };
    return pp_signal_test_main(argc, argv, cases, PP_CASE_COUNT(cases), children,
                               PP_CASE_COUNT(children));
}
