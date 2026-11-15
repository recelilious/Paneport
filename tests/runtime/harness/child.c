// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

#include "child.h"

#include <errno.h>
#include <fcntl.h>
#include <poll.h>
#include <signal.h>
#include <spawn.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <sys/types.h>
#include <sys/wait.h>
#include <time.h>
#include <unistd.h>

#define PP_CHILD_READ_SIZE 4096U
#define PP_CHILD_POLL_MS 100

// One captured stream: the harness's end of its pipe, while it is open, and what came so far.
typedef struct stream_t {
    int descriptor;
    char *data;
    size_t length;
    size_t capacity;
} stream_t;

static double now_seconds(void) {
    struct timespec now;
    (void)clock_gettime(CLOCK_MONOTONIC, &now);
    return (double)now.tv_sec + ((double)now.tv_nsec / 1e9);
}

static bool make_pipe(int descriptors[2]) {
    if (pipe(descriptors) != 0) {
        return false;
    }
    // Neither end stays open in the child: posix_spawn duplicates the writing end onto its
    // standard output or error, and the duplicate does not close on exec.
    (void)fcntl(descriptors[0], F_SETFD, FD_CLOEXEC);
    (void)fcntl(descriptors[1], F_SETFD, FD_CLOEXEC);
    return true;
}

static void close_stream(stream_t *stream) {
    if (stream->descriptor >= 0) {
        (void)close(stream->descriptor);
        stream->descriptor = -1;
    }
}

static void read_stream(stream_t *stream, size_t limit, bool *truncated) {
    char buffer[PP_CHILD_READ_SIZE];
    ssize_t count = read(stream->descriptor, buffer, sizeof buffer);
    if (count < 0) {
        if (errno != EINTR && errno != EAGAIN) {
            close_stream(stream);
        }
        return;
    }
    if (count == 0) {
        close_stream(stream);
        return;
    }
    size_t keep = (size_t)count;
    if (stream->length + keep > limit) {
        keep = limit - stream->length;
        *truncated = true;
    }
    if (keep == 0U) {
        return;
    }
    if (stream->data == NULL || stream->length + keep + 1U > stream->capacity) {
        size_t capacity = stream->capacity;
        if (capacity == 0U) {
            capacity = (size_t)2U * PP_CHILD_READ_SIZE;
        }
        while (capacity < stream->length + keep + 1U) {
            capacity *= 2U;
        }
        char *grown = realloc(stream->data, capacity);
        if (grown == NULL) {
            *truncated = true;
            return;
        }
        stream->data = grown;
        stream->capacity = capacity;
    }
    memcpy(stream->data + stream->length, buffer, keep);
    stream->length += keep;
    stream->data[stream->length] = '\0';
}

// The working folder of the child. macOS 26 names the action as POSIX 2024 does and deprecates
// its earlier name, which the C libraries of other systems may still have alone.
static int add_chdir(posix_spawn_file_actions_t *actions, const char *directory) {
#if defined(__APPLE__)
    return posix_spawn_file_actions_addchdir(actions, directory);
#else
    return posix_spawn_file_actions_addchdir_np(actions, directory);
#endif
}

static int start_child(pid_t *child, char *const arguments[], char *const environment[],
                       const char *directory, const int output[2], const int errors[2]) {
    posix_spawn_file_actions_t actions;
    posix_spawnattr_t attributes;
    int status = posix_spawn_file_actions_init(&actions);
    if (status != 0) {
        return status;
    }
    status = posix_spawnattr_init(&attributes);
    if (status != 0) {
        (void)posix_spawn_file_actions_destroy(&actions);
        return status;
    }
    sigset_t none;
    sigset_t all;
    (void)sigemptyset(&none);
    (void)sigfillset(&all);
    // A process group of its own, the default handling of every signal and nothing blocked, so
    // that the child starts as from a shell and a timeout can end its whole group.
    status = posix_spawnattr_setflags(&attributes, POSIX_SPAWN_SETPGROUP | POSIX_SPAWN_SETSIGDEF |
                                                       POSIX_SPAWN_SETSIGMASK);
    status = status != 0 ? status : posix_spawnattr_setpgroup(&attributes, 0);
    status = status != 0 ? status : posix_spawnattr_setsigdefault(&attributes, &all);
    status = status != 0 ? status : posix_spawnattr_setsigmask(&attributes, &none);
    status = status != 0 ? status
                         : posix_spawn_file_actions_addopen(&actions, STDIN_FILENO, "/dev/null",
                                                            O_RDONLY, 0);
    status =
        status != 0 ? status : posix_spawn_file_actions_adddup2(&actions, output[1], STDOUT_FILENO);
    status =
        status != 0 ? status : posix_spawn_file_actions_adddup2(&actions, errors[1], STDERR_FILENO);
    if (status == 0 && directory != NULL) {
        status = add_chdir(&actions, directory);
    }
    if (status == 0) {
        status = posix_spawn(child, arguments[0], &actions, &attributes, arguments, environment);
    }
    (void)posix_spawnattr_destroy(&attributes);
    (void)posix_spawn_file_actions_destroy(&actions);
    return status;
}

// Reads both streams until the child ends and its pipes close, the timeout passes, or the
// child has ended and the drain time is over. Returns the child's wait status.
static int collect(pid_t child, stream_t streams[2], unsigned timeout_seconds, size_t limit,
                   pp_child_result_t *result) {
    double deadline = now_seconds() + (double)timeout_seconds;
    double drain_end = 0.0;
    bool ended = false;
    int wait_status = 0;
    for (;;) {
        if (!ended && waitpid(child, &wait_status, WNOHANG) == child) {
            ended = true;
            drain_end = now_seconds() + ((double)PP_CHILD_DRAIN_MS / 1000.0);
        }
        double now = now_seconds();
        if ((ended && streams[0].descriptor < 0 && streams[1].descriptor < 0) ||
            (ended && now >= drain_end)) {
            break;
        }
        if (!ended && now >= deadline) {
            (void)kill(-child, SIGKILL);
            (void)waitpid(child, &wait_status, 0);
            result->timed_out = true;
            break;
        }
        struct pollfd watched[2];
        stream_t *owners[2];
        nfds_t count = 0;
        for (size_t index = 0; index < 2U; index++) {
            if (streams[index].descriptor >= 0) {
                watched[count].fd = streams[index].descriptor;
                watched[count].events = POLLIN;
                watched[count].revents = 0;
                owners[count] = &streams[index];
                count++;
            }
        }
        int ready = poll(count == 0U ? NULL : watched, count, PP_CHILD_POLL_MS);
        for (nfds_t index = 0; ready > 0 && index < count; index++) {
            unsigned events = (unsigned)watched[index].revents;
            if ((events & ((unsigned)POLLIN | (unsigned)POLLHUP | (unsigned)POLLERR)) != 0U) {
                read_stream(owners[index], limit, &result->truncated);
            }
        }
    }
    // Whatever the child left in its group, such as a helper that keeps the pipes open, ends
    // with the case.
    (void)kill(-child, SIGKILL);
    return wait_status;
}

bool pp_child_run(char *const arguments[], char *const environment[], const char *directory,
                  unsigned timeout_seconds, size_t limit, pp_child_result_t *result) {
    memset(result, 0, sizeof *result);
    int output[2];
    int errors[2];
    if (!make_pipe(output)) {
        (void)snprintf(result->message, sizeof result->message, "cannot make a pipe");
        return false;
    }
    if (!make_pipe(errors)) {
        (void)close(output[0]);
        (void)close(output[1]);
        (void)snprintf(result->message, sizeof result->message, "cannot make a pipe");
        return false;
    }
    double start = now_seconds();
    pid_t child = 0;
    int status = start_child(&child, arguments, environment, directory, output, errors);
    (void)close(output[1]);
    (void)close(errors[1]);
    stream_t streams[2] = {{output[0], NULL, 0U, 0U}, {errors[0], NULL, 0U, 0U}};
    if (status != 0) {
        char reason[128] = "unknown error";
        (void)strerror_r(status, reason, sizeof reason);
        (void)snprintf(result->message, sizeof result->message, "cannot start %s: %s", arguments[0],
                       reason);
        close_stream(&streams[0]);
        close_stream(&streams[1]);
        return false;
    }
    result->started = true;
    int wait_status = collect(child, streams, timeout_seconds, limit, result);
    close_stream(&streams[0]);
    close_stream(&streams[1]);
    result->seconds = now_seconds() - start;
    result->output = streams[0].data;
    result->output_length = streams[0].length;
    result->errors = streams[1].data;
    result->errors_length = streams[1].length;
    if (result->timed_out) {
        (void)snprintf(result->message, sizeof result->message, "timed out after %u s",
                       timeout_seconds);
    } else if (WIFEXITED(wait_status)) {
        result->exited = true;
        result->exit_code = WEXITSTATUS(wait_status);
        (void)snprintf(result->message, sizeof result->message, "exited with %d",
                       result->exit_code);
    } else if (WIFSIGNALED(wait_status)) {
        result->signal = WTERMSIG(wait_status);
        (void)snprintf(result->message, sizeof result->message, "ended by signal %d",
                       result->signal);
    }
    return true;
}

void pp_child_result_free(pp_child_result_t *result) {
    free(result->output);
    free(result->errors);
    memset(result, 0, sizeof *result);
}
