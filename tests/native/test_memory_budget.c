// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

// S1.2.5: the memory budget, with the stub that macOS uses.

#include <stddef.h>
#include <stdint.h>
#include <string.h>

#include "memory_budget.h"
#include "paneport/host.h"
#include "paneport/memory.h"
#include "paneport/testing.h"
#include "pp_check.h"

#define MIB (UINT64_C(1024) * 1024U)

static pp_memory_budget_t read_budget(uint64_t available_bytes, uint64_t footprint_bytes) {
    PP_CHECK_EQUAL(pp_test_set_memory_stub(available_bytes, footprint_bytes), PP_OK);
    pp_memory_budget_t budget = {.struct_size = (uint32_t)sizeof(pp_memory_budget_t)};
    PP_CHECK_EQUAL(pp_memory_get_budget(&budget), PP_OK);
    return budget;
}

// The limit is the available memory plus the footprint.
static void limit_is_sum(void) {
    const pp_memory_budget_t budget = read_budget(5000U * MIB, 1144U * MIB);
    PP_CHECK_EQUAL(budget.limit_bytes, 6144U * MIB);
    PP_CHECK_EQUAL(budget.available_bytes, 5000U * MIB);
    PP_CHECK_EQUAL(budget.footprint_bytes, 1144U * MIB);
    PP_CHECK_EQUAL(budget.struct_size, sizeof(pp_memory_budget_t));

    const pp_memory_budget_t empty = read_budget(0U, 0U);
    PP_CHECK_EQUAL(empty.limit_bytes, 0U);
    PP_CHECK_EQUAL(empty.game_mode_level_reached, 0U);

    const pp_memory_budget_t huge = read_budget(UINT64_MAX - 5U, 10U);
    PP_CHECK_EQUAL(huge.limit_bytes, UINT64_MAX);
}

// The flag is off at 6,144 MiB and on at 8,192 MiB, and the threshold sits between them.
static void game_mode_level(void) {
    PP_CHECK_EQUAL(read_budget(5000U * MIB, 1144U * MIB).game_mode_level_reached, 0U);
    PP_CHECK_EQUAL(read_budget(7000U * MIB, 1192U * MIB).game_mode_level_reached, 1U);
    // A published reading at Game Mode: 7,259 MiB available plus 932 MiB of footprint.
    PP_CHECK_EQUAL(read_budget(7259U * MIB, 932U * MIB).game_mode_level_reached, 1U);
    PP_CHECK_EQUAL(read_budget(PP_MEMORY_GAME_MODE_LEVEL_BYTES - 1U, 0U).game_mode_level_reached,
                   0U);
    PP_CHECK_EQUAL(read_budget(PP_MEMORY_GAME_MODE_LEVEL_BYTES, 0U).game_mode_level_reached, 1U);
    PP_CHECK(PP_MEMORY_GAME_MODE_LEVEL_BYTES > 6144U * MIB);
    PP_CHECK(PP_MEMORY_GAME_MODE_LEVEL_BYTES <= 8192U * MIB);

    pp_memory_budget_t computed;
    pp_memory_budget_compute(6144U * MIB, 0U, &computed);
    PP_CHECK_EQUAL(computed.game_mode_level_reached, 0U);
    pp_memory_budget_compute(4096U * MIB, 4096U * MIB, &computed);
    PP_CHECK_EQUAL(computed.game_mode_level_reached, 1U);
}

// A caller that knows fewer members gets only those, and the rest of its memory stays as it
// was; one that knows more keeps its extra bytes.
static void smaller_struct_size(void) {
    PP_CHECK_EQUAL(pp_test_set_memory_stub(7259U * MIB, 932U * MIB), PP_OK);
    const uint64_t untouched = UINT64_C(0xABABABABABABABAB);

    pp_memory_budget_t budget;
    memset(&budget, 0xAB, sizeof budget);
    budget.struct_size = (uint32_t)offsetof(pp_memory_budget_t, available_bytes);
    PP_CHECK_EQUAL(pp_memory_get_budget(&budget), PP_OK);
    PP_CHECK_EQUAL(budget.game_mode_level_reached, 1U);
    PP_CHECK_EQUAL(budget.limit_bytes, 8191U * MIB);
    PP_CHECK_EQUAL(budget.available_bytes, untouched);
    PP_CHECK_EQUAL(budget.footprint_bytes, untouched);

    memset(&budget, 0xAB, sizeof budget);
    budget.struct_size = 8U;
    PP_CHECK_EQUAL(pp_memory_get_budget(&budget), PP_OK);
    PP_CHECK_EQUAL(budget.game_mode_level_reached, 1U);
    PP_CHECK_EQUAL(budget.limit_bytes, untouched);

    memset(&budget, 0xAB, sizeof budget);
    budget.struct_size = 4U;
    PP_CHECK_EQUAL(pp_memory_get_budget(&budget), PP_OK);
    PP_CHECK_EQUAL(budget.game_mode_level_reached, 0xABU);

    struct {
        pp_memory_budget_t budget;
        uint64_t newer_member;
    } larger;
    memset(&larger, 0xAB, sizeof larger);
    larger.budget.struct_size = (uint32_t)sizeof larger;
    PP_CHECK_EQUAL(pp_memory_get_budget(&larger.budget), PP_OK);
    PP_CHECK_EQUAL(larger.budget.footprint_bytes, 932U * MIB);
    PP_CHECK_EQUAL(larger.budget.struct_size, sizeof larger);
    PP_CHECK_EQUAL(larger.newer_member, untouched);
}

static void invalid_arguments(void) {
    PP_CHECK_EQUAL(pp_memory_get_budget(NULL), PP_E_INVALID_ARGUMENT);
    pp_memory_budget_t budget = {.struct_size = 3U};
    PP_CHECK_EQUAL(pp_memory_get_budget(&budget), PP_E_INVALID_ARGUMENT);
    PP_CHECK(strstr(pp_last_error(), "at least 4") != NULL);
}

int main(int argc, char **argv) {
    static const pp_test_case_t cases[] = {
        {"limit_is_sum", limit_is_sum},
        {"game_mode_level", game_mode_level},
        {"smaller_struct_size", smaller_struct_size},
        {"invalid_arguments", invalid_arguments},
    };
    return pp_run_case(argc, argv, cases, PP_CASE_COUNT(cases));
}
