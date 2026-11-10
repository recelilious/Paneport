// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

// The header test of C-0005 section 5. CMake generates one C17 and one C++20 source per
// public header, each including that header alone, and links them into this program with
// test_headers_linkage.cpp, which calls the host from C++; the test passes when they all
// compile, the program links, and the calls succeed. This file includes every header
// together, as a C caller does.

#include "paneport/common.h"
#include "paneport/host.h"
#include "paneport/memory.h"
#include "paneport/paneport.h"
#include "paneport/testing.h"

// From test_headers_linkage.cpp: 0 when the calls from C++ worked.
int paneport_header_linkage(void);

int main(void) {
    if (PP_ABI_VERSION_MAJOR < 1 || PP_OK != 0) {
        return 1;
    }
    return paneport_header_linkage();
}
