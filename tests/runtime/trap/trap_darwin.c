// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

// The interposing half of the fork and exec trap (trap.h) on macOS. Each call that the trap
// intercepts has a replacement here, and the tuples of the __DATA,__interpose section, one for
// each call, tell dyld to bind the other images of the process to the replacement instead of the
// original. dyld applies the tuples of an image that it loads at launch to every image that it
// binds, those that dlopen loads later among them, except the image that holds the tuples: this
// library's own references reach the original functions (docs/design/runtime-macos-harness.md
// section 5.2). A replacement takes its caller from its own return address, which lies in the
// code that made the call.

#include <errno.h>
#include <mach/mach.h>
#include <pthread.h>
#include <signal.h>
#include <spawn.h>
#include <stdarg.h>
#include <stdbool.h>
#include <stddef.h>
#include <stdio.h>
#include <stdlib.h>
#include <sys/types.h>
#include <unistd.h>

#include "trap_core.h"

// libsystem_kernel's wrapper of the __pthread_kill system call, which sends a signal to a thread
// named by its Mach port, as the wineserver of multi mode does (Wine's server/mach.c). No header
// declares it, and its C name is reserved for the implementation, so it is declared here under a
// name of our own, with its symbol as the assembler label.
extern int pp_trap_kernel_pthread_kill(mach_port_t thread,
                                       int signal_number) __asm__("___pthread_kill");

// The C library's vfork, declared the same way: clang-tidy's bugprone-unsafe-functions reports
// every reference to vfork as a call, and the trap only names it in its tuple, never calls it.
extern pid_t pp_trap_libc_vfork(void) __asm__("_vfork");

// The original functions, as dyld binds them for this image, to which a replacement hands each
// call that the trap lets through.
static const struct {
    pid_t (*fork)(void);
    int (*execv)(const char *, char *const[]);
    int (*execve)(const char *, char *const[], char *const[]);
    int (*execvp)(const char *, char *const[]);
    int (*execvP)(const char *, const char *, char *const[]);
    int (*posix_spawn)(pid_t *, const char *, const posix_spawn_file_actions_t *,
                       const posix_spawnattr_t *, char *const[], char *const[]);
    int (*posix_spawnp)(pid_t *, const char *, const posix_spawn_file_actions_t *,
                        const posix_spawnattr_t *, char *const[], char *const[]);
    int (*system)(const char *);
    FILE *(*popen)(const char *, const char *);
    int (*kill)(pid_t, int);
    int (*killpg)(pid_t, int);
    int (*pthread_kill)(pthread_t, int);
    int (*raise)(int);
    int (*thread_signal)(mach_port_t, int);
} k_original = {
    .fork = fork,
    .execv = execv,
    .execve = execve,
    .execvp = execvp,
    .execvP = execvP,
    .posix_spawn = posix_spawn,
    .posix_spawnp = posix_spawnp,
    .system = system,
    .popen = popen,
    .kill = kill,
    .killpg = killpg,
    .pthread_kill = pthread_kill,
    .raise = raise,
    .thread_signal = pp_trap_kernel_pthread_kill,
};

// The most arguments, with the null pointer that ends them, that a disarmed trap hands on from
// execl, execle and execlp, which take them one by one; more fail with E2BIG.
#define PP_TRAP_LIST_ARGUMENTS 1024U

// Records a refused call and sets errno for it.
static void refuse(const char *call, int signal_number, const void *caller) {
    pp_trap_record(call, signal_number, caller);
    errno = EPERM;
}

static pid_t trap_fork(void) {
    if (pp_trap_refuses()) {
        refuse("fork", 0, __builtin_return_address(0));
        return -1;
    }
    return k_original.fork();
}

// vfork's child borrows its parent's stack until it execs or exits, so a vfork that returned
// through this function would leave the parent a frame that the child may have overwritten. A
// disarmed trap therefore runs fork, which POSIX allows vfork to be.
static pid_t trap_vfork(void) {
    if (pp_trap_refuses()) {
        refuse("vfork", 0, __builtin_return_address(0));
        return -1;
    }
    return k_original.fork();
}

static int trap_execve(const char *path, char *const arguments[], char *const environment[]) {
    if (pp_trap_refuses()) {
        refuse("execve", 0, __builtin_return_address(0));
        return -1;
    }
    return k_original.execve(path, arguments, environment);
}

static int trap_execv(const char *path, char *const arguments[]) {
    if (pp_trap_refuses()) {
        refuse("execv", 0, __builtin_return_address(0));
        return -1;
    }
    return k_original.execv(path, arguments);
}

static int trap_execvp(const char *file, char *const arguments[]) {
    if (pp_trap_refuses()) {
        refuse("execvp", 0, __builtin_return_address(0));
        return -1;
    }
    return k_original.execvp(file, arguments);
}

static int trap_execvP(const char *file, const char *search_path, char *const arguments[]) {
    if (pp_trap_refuses()) {
        refuse("execvP", 0, __builtin_return_address(0));
        return -1;
    }
    return k_original.execvP(file, search_path, arguments);
}

// Collects the arguments of execl, execle or execlp, from first up to the null pointer that ends
// them, into a null-terminated array of PP_TRAP_LIST_ARGUMENTS entries. Returns false, with errno
// set to E2BIG, when they do not fit. list is left after the null pointer.
static bool collect_arguments(char *first, va_list *list, char **arguments) {
    size_t count = 0U;
    for (char *next = first; next != NULL; next = va_arg(*list, char *)) {
        if (count + 1U == PP_TRAP_LIST_ARGUMENTS) {
            errno = E2BIG;
            return false;
        }
        arguments[count] = next;
        count++;
    }
    arguments[count] = NULL;
    return true;
}

// The forms of exec that take their arguments one by one.
typedef enum list_form_t { LIST_EXECL, LIST_EXECLE, LIST_EXECLP } list_form_t;

// Hands on a call of execl, execle or execlp that the trap lets through, to execv, to execve with
// the environment that follows the null pointer after the arguments, or to execvp. It never runs
// inline in a replacement: its array of arguments makes the frame big enough that the prologue
// calls the stack probe, and that call overwrites the link register before the replacement's
// __builtin_return_address reads its caller there, so the report would name the trap itself.
__attribute__((noinline)) static int hand_on_list(list_form_t form, const char *path, char *first,
                                                  va_list *list) {
    char *arguments[PP_TRAP_LIST_ARGUMENTS];
    if (!collect_arguments(first, list, arguments)) {
        return -1;
    }
    switch (form) {
    case LIST_EXECLE:
        return k_original.execve(path, arguments, va_arg(*list, char **));
    case LIST_EXECLP:
        return k_original.execvp(path, arguments);
    case LIST_EXECL:
    default:
        return k_original.execv(path, arguments);
    }
}

static int trap_execl(const char *path, char *first, ...) {
    if (pp_trap_refuses()) {
        refuse("execl", 0, __builtin_return_address(0));
        return -1;
    }
    va_list list;
    va_start(list, first);
    const int result = hand_on_list(LIST_EXECL, path, first, &list);
    va_end(list);
    return result;
}

static int trap_execle(const char *path, char *first, ...) {
    if (pp_trap_refuses()) {
        refuse("execle", 0, __builtin_return_address(0));
        return -1;
    }
    va_list list;
    va_start(list, first);
    const int result = hand_on_list(LIST_EXECLE, path, first, &list);
    va_end(list);
    return result;
}

static int trap_execlp(const char *file, char *first, ...) {
    if (pp_trap_refuses()) {
        refuse("execlp", 0, __builtin_return_address(0));
        return -1;
    }
    va_list list;
    va_start(list, first);
    const int result = hand_on_list(LIST_EXECLP, file, first, &list);
    va_end(list);
    return result;
}

// posix_spawn and posix_spawnp return an error number rather than setting errno.
static int trap_posix_spawn(pid_t *process, const char *path,
                            const posix_spawn_file_actions_t *actions,
                            const posix_spawnattr_t *attributes, char *const arguments[],
                            char *const environment[]) {
    if (pp_trap_refuses()) {
        refuse("posix_spawn", 0, __builtin_return_address(0));
        return EPERM;
    }
    return k_original.posix_spawn(process, path, actions, attributes, arguments, environment);
}

static int trap_posix_spawnp(pid_t *process, const char *file,
                             const posix_spawn_file_actions_t *actions,
                             const posix_spawnattr_t *attributes, char *const arguments[],
                             char *const environment[]) {
    if (pp_trap_refuses()) {
        refuse("posix_spawnp", 0, __builtin_return_address(0));
        return EPERM;
    }
    return k_original.posix_spawnp(process, file, actions, attributes, arguments, environment);
}

// system and popen start a shell, so both are refused even for a command that would not run.
static int trap_system(const char *command) {
    if (pp_trap_refuses()) {
        refuse("system", 0, __builtin_return_address(0));
        return -1;
    }
    return k_original.system(command);
}

static FILE *trap_popen(const char *command, const char *mode) {
    if (pp_trap_refuses()) {
        refuse("popen", 0, __builtin_return_address(0));
        return NULL;
    }
    return k_original.popen(command, mode);
}

// The armed trap refuses to send SIGUSR1 and SIGQUIT only; every other signal goes through, so
// that the host's own raise of a fault signal, when it ends the app, still works.
static int trap_kill(pid_t process, int signal_number) {
    if (pp_trap_refuses_signal(signal_number)) {
        refuse("kill", signal_number, __builtin_return_address(0));
        return -1;
    }
    return k_original.kill(process, signal_number);
}

static int trap_killpg(pid_t group, int signal_number) {
    if (pp_trap_refuses_signal(signal_number)) {
        refuse("killpg", signal_number, __builtin_return_address(0));
        return -1;
    }
    return k_original.killpg(group, signal_number);
}

// pthread_kill returns an error number rather than setting errno.
static int trap_pthread_kill(pthread_t thread, int signal_number) {
    if (pp_trap_refuses_signal(signal_number)) {
        refuse("pthread_kill", signal_number, __builtin_return_address(0));
        return EPERM;
    }
    return k_original.pthread_kill(thread, signal_number);
}

static int trap_raise(int signal_number) {
    if (pp_trap_refuses_signal(signal_number)) {
        refuse("raise", signal_number, __builtin_return_address(0));
        return -1;
    }
    return k_original.raise(signal_number);
}

static int trap_thread_signal(mach_port_t thread, int signal_number) {
    if (pp_trap_refuses_signal(signal_number)) {
        refuse("__pthread_kill", signal_number, __builtin_return_address(0));
        return -1;
    }
    return k_original.thread_signal(thread, signal_number);
}

// The type of a tuple's two entries. dyld reads them as addresses; C converts a function pointer
// to another function pointer type, and clang's -Wcast-function-type lets any function convert to
// this one.
typedef void (*pp_trap_function_t)(void);

// One tuple of dyld's interposing: other images' calls of replacee go to replacement.
typedef struct pp_trap_tuple_t {
    pp_trap_function_t replacement;
    pp_trap_function_t replacee;
} pp_trap_tuple_t;

// The tuples. dyld finds them by their section's name; nothing else refers to them, so used
// keeps the compiler and the linker from dropping them.
__attribute__((used, section("__DATA,__interpose"))) static const pp_trap_tuple_t k_tuples[] = {
    {(pp_trap_function_t)trap_fork, (pp_trap_function_t)fork},
    {(pp_trap_function_t)trap_vfork, (pp_trap_function_t)pp_trap_libc_vfork},
    {(pp_trap_function_t)trap_execl, (pp_trap_function_t)execl},
    {(pp_trap_function_t)trap_execle, (pp_trap_function_t)execle},
    {(pp_trap_function_t)trap_execlp, (pp_trap_function_t)execlp},
    {(pp_trap_function_t)trap_execv, (pp_trap_function_t)execv},
    {(pp_trap_function_t)trap_execve, (pp_trap_function_t)execve},
    {(pp_trap_function_t)trap_execvp, (pp_trap_function_t)execvp},
    {(pp_trap_function_t)trap_execvP, (pp_trap_function_t)execvP},
    {(pp_trap_function_t)trap_posix_spawn, (pp_trap_function_t)posix_spawn},
    {(pp_trap_function_t)trap_posix_spawnp, (pp_trap_function_t)posix_spawnp},
    {(pp_trap_function_t)trap_system, (pp_trap_function_t)system},
    {(pp_trap_function_t)trap_popen, (pp_trap_function_t)popen},
    {(pp_trap_function_t)trap_kill, (pp_trap_function_t)kill},
    {(pp_trap_function_t)trap_killpg, (pp_trap_function_t)killpg},
    {(pp_trap_function_t)trap_pthread_kill, (pp_trap_function_t)pthread_kill},
    {(pp_trap_function_t)trap_raise, (pp_trap_function_t)raise},
    {(pp_trap_function_t)trap_thread_signal, (pp_trap_function_t)pp_trap_kernel_pthread_kill},
};
