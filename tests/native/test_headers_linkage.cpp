// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

// Part of the header test of C-0005 section 5: the public headers declare their functions with
// C linkage for C++ callers (C-0005 section 1). This file calls functions of the headers from
// C++, and the program links the host's C objects. When a header loses its extern "C" guards,
// the calls name C++-mangled symbols that the host does not define, and the program fails to
// link.

#include "paneport/paneport.h"
#include "paneport/testing.h"

extern "C" int paneport_header_linkage(void);

extern "C" int paneport_header_linkage(void) {
    const pp_test_layout_struct_t *table = nullptr;
    uint32_t count = 0U;
    const bool linked = pp_abi_version() != 0U &&
                        pp_memory_get_budget(nullptr) == PP_E_INVALID_ARGUMENT &&
                        pp_test_get_layout_table(&table, &count) == PP_OK && count > 0U;
    return linked ? 0 : 1;
}
