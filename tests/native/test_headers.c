// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

// The header test of C-0005 section 5. CMake generates one C17 and one C++20 source per
// public header, each including that header alone, and links them into this program; the
// test passes when they all compile and the program runs. This file includes every header
// together, as a C caller does.

#include "paneport/common.h"
#include "paneport/host.h"
#include "paneport/memory.h"
#include "paneport/paneport.h"
#include "paneport/testing.h"

int main(void) { return PP_ABI_VERSION_MAJOR >= 1 && PP_OK == 0 ? 0 : 1; }
