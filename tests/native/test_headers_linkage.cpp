// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

// Part of the header test of C-0005 section 5: the public headers declare their functions with
// C linkage for C++ callers (C-0005 section 1). Each line below declares a function again with
// extern "C"; C++ refuses a second declaration with another linkage, so this file stops
// compiling when a header loses its extern "C" guards.

#include "paneport/paneport.h"
#include "paneport/testing.h"

extern "C" uint32_t pp_abi_version(void);
extern "C" pp_status_t pp_init(const pp_init_config_t *config);
extern "C" pp_status_t pp_memory_get_budget(pp_memory_budget_t *budget);
extern "C" pp_status_t pp_test_get_layout_table(const pp_test_layout_struct_t **table,
                                                uint32_t *count);
