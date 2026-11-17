// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

// The Mach thread calls of thread_control.h, for arm64 Apple platforms.

#include "thread_control.h"

#include <mach/mach.h>
#include <pthread.h>
#include <stdbool.h>
#include <stddef.h>
#include <stdint.h>

#include "error.h"

#if !defined(__APPLE__) || !defined(__aarch64__)
#error "The thread control helpers use the Mach calls of arm64 Apple platforms."
#endif

// A thread that has ended leaves a dead name or a terminated thread behind, which these
// results report.
static bool thread_ended(kern_return_t result) {
    switch (result) {
    case KERN_TERMINATED:
    case MACH_SEND_INVALID_DEST:
    case MACH_SEND_INVALID_RIGHT:
    case KERN_INVALID_ARGUMENT:
    case KERN_INVALID_NAME:
    case KERN_INVALID_RIGHT:
        return true;
    default:
        return false;
    }
}

static pp_status_t failed(kern_return_t result, const char *call) {
    if (thread_ended(result)) {
        return pp_error_set(PP_E_INVALID_ARGUMENT, "%s found no running thread (%d).", call,
                            (int)result);
    }
    return pp_error_set(PP_E_SYSTEM, "%s failed with %d.", call, (int)result);
}

static bool is_calling_thread(uint32_t port) {
    return pthread_mach_thread_np(pthread_self()) == (mach_port_t)port;
}

pp_status_t pp_thread_control_open(pthread_t thread, uint32_t *port) {
    if (port == NULL) {
        return pp_error_set(PP_E_INVALID_ARGUMENT, "pp_thread_control_open needs an output.");
    }
    const mach_port_t name = pthread_mach_thread_np(thread);
    if (name == MACH_PORT_NULL) {
        return pp_error_set(PP_E_INVALID_ARGUMENT, "The thread has no Mach port.");
    }
    const kern_return_t result =
        mach_port_mod_refs(mach_task_self(), name, MACH_PORT_RIGHT_SEND, 1);
    if (result != KERN_SUCCESS) {
        return failed(result, "mach_port_mod_refs");
    }
    *port = (uint32_t)name;
    return PP_OK;
}

pp_status_t pp_thread_control_close(uint32_t port) {
    const kern_return_t result = mach_port_deallocate(mach_task_self(), (mach_port_t)port);
    return result == KERN_SUCCESS ? PP_OK : failed(result, "mach_port_deallocate");
}

pp_status_t pp_thread_control_suspend(uint32_t port) {
    if (is_calling_thread(port)) {
        return pp_error_set(PP_E_INVALID_ARGUMENT, "A thread cannot suspend itself.");
    }
    const kern_return_t result = thread_suspend((thread_act_t)port);
    return result == KERN_SUCCESS ? PP_OK : failed(result, "thread_suspend");
}

pp_status_t pp_thread_control_resume(uint32_t port) {
    if (is_calling_thread(port)) {
        return pp_error_set(PP_E_INVALID_ARGUMENT, "A thread cannot resume itself.");
    }
    const kern_return_t result = thread_resume((thread_act_t)port);
    return result == KERN_SUCCESS ? PP_OK : failed(result, "thread_resume");
}

pp_status_t pp_thread_control_get_state(uint32_t port, pp_thread_state_t *state) {
    if (state == NULL) {
        return pp_error_set(PP_E_INVALID_ARGUMENT, "pp_thread_control_get_state needs a state.");
    }
    arm_thread_state64_t native;
    mach_msg_type_number_t count = ARM_THREAD_STATE64_COUNT;
    const kern_return_t result =
        thread_get_state((thread_act_t)port, ARM_THREAD_STATE64, (thread_state_t)&native, &count);
    if (result != KERN_SUCCESS) {
        return failed(result, "thread_get_state");
    }
    state->struct_size = (uint32_t)sizeof(pp_thread_state_t);
    state->cpsr = native.__cpsr;
    for (size_t index = 0U; index < 29U; index++) {
        state->x[index] = native.__x[index];
    }
    state->fp = native.__fp;
    state->lr = native.__lr;
    state->sp = native.__sp;
    state->pc = native.__pc;
    return PP_OK;
}

pp_status_t pp_thread_control_set_state(uint32_t port, const pp_thread_state_t *state) {
    if (state == NULL || state->struct_size < sizeof(pp_thread_state_t)) {
        return pp_error_set(PP_E_INVALID_ARGUMENT,
                            "pp_thread_control_set_state needs a whole state.");
    }
    // The state is read first, so that the members that pp_thread_state_t leaves out keep
    // their values.
    arm_thread_state64_t native;
    mach_msg_type_number_t count = ARM_THREAD_STATE64_COUNT;
    kern_return_t result =
        thread_get_state((thread_act_t)port, ARM_THREAD_STATE64, (thread_state_t)&native, &count);
    if (result != KERN_SUCCESS) {
        return failed(result, "thread_get_state");
    }
    native.__cpsr = state->cpsr;
    for (size_t index = 0U; index < 29U; index++) {
        native.__x[index] = state->x[index];
    }
    native.__fp = state->fp;
    native.__lr = state->lr;
    native.__sp = state->sp;
    native.__pc = state->pc;
    result = thread_set_state((thread_act_t)port, ARM_THREAD_STATE64, (thread_state_t)&native,
                              ARM_THREAD_STATE64_COUNT);
    return result == KERN_SUCCESS ? PP_OK : failed(result, "thread_set_state");
}
