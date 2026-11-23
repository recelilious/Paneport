// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

// The debugger flags of C-0006 section 3 step 1, read from the system, and the clock. Both
// backends use them: iOS for the protocol, macOS for the test of the reader itself.

#include "jit/jit_platform.h"

#include <stddef.h>
#include <string.h>
#include <sys/proc.h>
#include <sys/sysctl.h>
#include <sys/types.h>
#include <time.h>
#include <unistd.h>

// csops is part of the system's C library on every Apple platform, but no public SDK header
// declares it. The prototype and the two values below are those of XNU's published sources,
// bsd/sys/codesign.h and osfmk/kern/cs_blobs.h; jit.md section 5 records the basis.
extern int csops(pid_t pid, unsigned int ops, void *useraddr, size_t usersize);

// The operation that reads the code-signing status word, and the flag in it that XNU sets
// when a debugger attaches and never clears.
#define PP_CS_OPS_STATUS 0U
#define PP_CS_DEBUGGED 0x10000000U

pp_jit_flags_t pp_jit_decode_flags(uint32_t code_signing_status, int32_t process_flags) {
    const pp_jit_flags_t flags = {
        .debugged_since_launch = (code_signing_status & PP_CS_DEBUGGED) != 0U,
        .attached_now = ((uint32_t)process_flags & (uint32_t)P_TRACED) != 0U,
    };
    return flags;
}

bool pp_jit_read_darwin_flags(pp_jit_flags_t *flags) {
    const pid_t self = getpid();
    uint32_t status = 0U;
    if (csops(self, PP_CS_OPS_STATUS, &status, sizeof status) != 0) {
        return false;
    }

    // Apple's Technical Q&A QA1361 reads P_TRACED the same way.
    int name[4] = {CTL_KERN, KERN_PROC, KERN_PROC_PID, (int)self};
    struct kinfo_proc process;
    memset(&process, 0, sizeof process);
    size_t size = sizeof process;
    if (sysctl(name, 4U, &process, &size, NULL, 0U) != 0 || size != sizeof process) {
        return false;
    }
    *flags = pp_jit_decode_flags(status, process.kp_proc.p_flag);
    return true;
}

uint64_t pp_jit_monotonic_ms(void) {
    struct timespec now;
    if (clock_gettime(CLOCK_MONOTONIC, &now) != 0) {
        return 0U;
    }
    return ((uint64_t)now.tv_sec * 1000U) + ((uint64_t)now.tv_nsec / 1000000U);
}
