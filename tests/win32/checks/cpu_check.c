// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

#include "cpu_check.h"

#include <stdint.h>
#include <stdio.h>
#include <string.h>

#define WIN32_LEAN_AND_MEAN
#include <windows.h>

// The registers that CPUID returns, in the order EAX, EBX, ECX, EDX.
struct cpuid_result {
    uint32_t eax;
    uint32_t ebx;
    uint32_t ecx;
    uint32_t edx;
};

enum cpuid_register {
    REGISTER_EBX,
    REGISTER_ECX,
    REGISTER_EDX,
};

// A feature bit, with the leaf and register that hold it, as Intel's and AMD's manuals give
// them.
struct cpu_feature {
    const char *name;
    uint32_t leaf;
    enum cpuid_register reg;
    unsigned bit;
};

// The order of the list that the report prints. It starts with sse and sse2, which the
// expected outputs of tests/runtime rely on. avx and avx2 are reported only when the operating
// system also saves the AVX state (checked with XGETBV), as Intel's manual asks.
static const struct cpu_feature k_features[] = {
    {"sse", 1U, REGISTER_EDX, 25U},
    {"sse2", 1U, REGISTER_EDX, 26U},
    {"sse3", 1U, REGISTER_ECX, 0U},
    {"pclmulqdq", 1U, REGISTER_ECX, 1U},
    {"ssse3", 1U, REGISTER_ECX, 9U},
    {"fma", 1U, REGISTER_ECX, 12U},
    {"cx16", 1U, REGISTER_ECX, 13U},
    {"sse4.1", 1U, REGISTER_ECX, 19U},
    {"sse4.2", 1U, REGISTER_ECX, 20U},
    {"movbe", 1U, REGISTER_ECX, 22U},
    {"popcnt", 1U, REGISTER_ECX, 23U},
    {"aes", 1U, REGISTER_ECX, 25U},
    {"xsave", 1U, REGISTER_ECX, 26U},
    {"avx", 1U, REGISTER_ECX, 28U},
    {"f16c", 1U, REGISTER_ECX, 29U},
    {"rdrand", 1U, REGISTER_ECX, 30U},
    {"bmi1", 7U, REGISTER_EBX, 3U},
    {"avx2", 7U, REGISTER_EBX, 5U},
    {"bmi2", 7U, REGISTER_EBX, 8U},
    {"erms", 7U, REGISTER_EBX, 9U},
    {"adx", 7U, REGISTER_EBX, 19U},
    {"sha", 7U, REGISTER_EBX, 29U},
    {"lahf", 0x80000001U, REGISTER_ECX, 0U},
    {"lzcnt", 0x80000001U, REGISTER_ECX, 5U},
    {"rdtscp", 0x80000001U, REGISTER_EDX, 27U},
};

static struct cpuid_result run_cpuid(uint32_t leaf, uint32_t subleaf) {
    struct cpuid_result result;
    __asm__ volatile("cpuid"
                     : "=a"(result.eax), "=b"(result.ebx), "=c"(result.ecx), "=d"(result.edx)
                     : "a"(leaf), "c"(subleaf));
    return result;
}

// XCR0, which says which register states the operating system saves. XGETBV is valid only
// when CPUID reports OSXSAVE.
static uint64_t read_xcr0(void) {
    uint32_t low = 0;
    uint32_t high = 0;
    __asm__ volatile("xgetbv" : "=a"(low), "=d"(high) : "c"(0U));
    return ((uint64_t)high << 32U) | low;
}

// The features that CPUID reports, read once.
struct cpu_view {
    char vendor[13];
    uint32_t max_leaf;
    uint32_t max_extended_leaf;
    struct cpuid_result leaf1;
    struct cpuid_result leaf7;
    struct cpuid_result extended1;
    bool avx_state_saved;
};

static struct cpu_view read_cpu(void) {
    struct cpu_view view;
    memset(&view, 0, sizeof view);
    struct cpuid_result leaf0 = run_cpuid(0U, 0U);
    view.max_leaf = leaf0.eax;
    memcpy(view.vendor, &leaf0.ebx, 4U);
    memcpy(view.vendor + 4, &leaf0.edx, 4U);
    memcpy(view.vendor + 8, &leaf0.ecx, 4U);
    view.vendor[12] = '\0';
    if (view.max_leaf >= 1U) {
        view.leaf1 = run_cpuid(1U, 0U);
    }
    if (view.max_leaf >= 7U) {
        view.leaf7 = run_cpuid(7U, 0U);
    }
    view.max_extended_leaf = run_cpuid(0x80000000U, 0U).eax;
    if (view.max_extended_leaf >= 0x80000001U) {
        view.extended1 = run_cpuid(0x80000001U, 0U);
    }
    // OSXSAVE is bit 27 of ECX in leaf 1; bits 1 and 2 of XCR0 are the SSE and AVX states.
    if ((view.leaf1.ecx & (1U << 27U)) != 0U) {
        view.avx_state_saved = (read_xcr0() & 6U) == 6U;
    }
    return view;
}

static bool has_feature(const struct cpu_view *view, const struct cpu_feature *feature) {
    const struct cpuid_result *leaf = NULL;
    if (feature->leaf == 1U) {
        leaf = &view->leaf1;
    } else if (feature->leaf == 7U) {
        leaf = &view->leaf7;
    } else {
        leaf = &view->extended1;
    }
    uint32_t value = 0;
    switch (feature->reg) {
    case REGISTER_EBX:
        value = leaf->ebx;
        break;
    case REGISTER_ECX:
        value = leaf->ecx;
        break;
    case REGISTER_EDX:
        value = leaf->edx;
        break;
    }
    bool present = (value & (1U << feature->bit)) != 0U;
    if (strcmp(feature->name, "avx") == 0 || strcmp(feature->name, "avx2") == 0) {
        present = present && view->avx_state_saved;
    }
    return present;
}

static bool has_named_feature(const struct cpu_view *view, const char *name) {
    for (size_t index = 0; index < sizeof k_features / sizeof k_features[0]; index++) {
        if (strcmp(k_features[index].name, name) == 0) {
            return has_feature(view, &k_features[index]);
        }
    }
    return false;
}

static bool vendor_is_printable(const char *vendor) {
    for (size_t index = 0; index < 12U; index++) {
        if (vendor[index] < '!' || vendor[index] > '~') {
            return false;
        }
    }
    return true;
}

bool pp_cpu_report(struct pp_test_run *run, const char *area) {
    struct cpu_view view = read_cpu();
    char list[PP_TEST_LINE_MAX];
    size_t used = 0;
    list[0] = '\0';
    for (size_t index = 0; index < sizeof k_features / sizeof k_features[0]; index++) {
        if (!has_feature(&view, &k_features[index])) {
            continue;
        }
        int added = snprintf(list + used, sizeof list - used, " %s", k_features[index].name);
        if (added < 0 || (size_t)added >= sizeof list - used) {
            break;
        }
        used += (size_t)added;
    }
    bool printable = vendor_is_printable(view.vendor);
    pp_test_line(run, "%s: vendor %s, features%s", area, printable ? view.vendor : "unreadable",
                 list);
    bool passed = pp_test_check(run, printable, "CPUID gives a vendor of 12 printable characters");
    passed = pp_test_check(run, has_named_feature(&view, "sse") && has_named_feature(&view, "sse2"),
                           "CPUID reports SSE and SSE2") &&
             passed;
    return passed;
}

bool pp_cpu_compare_with_windows(struct pp_test_run *run, const char *area) {
    struct cpu_view view = read_cpu();
    bool sse = IsProcessorFeaturePresent(PF_XMMI_INSTRUCTIONS_AVAILABLE) != FALSE;
    bool sse2 = IsProcessorFeaturePresent(PF_XMMI64_INSTRUCTIONS_AVAILABLE) != FALSE;
    bool sse3 = IsProcessorFeaturePresent(PF_SSE3_INSTRUCTIONS_AVAILABLE) != FALSE;
    bool passed = pp_test_check(run, sse == has_named_feature(&view, "sse"),
                                "IsProcessorFeaturePresent agrees with CPUID about SSE");
    passed = pp_test_check(run, sse2 == has_named_feature(&view, "sse2"),
                           "IsProcessorFeaturePresent agrees with CPUID about SSE2") &&
             passed;
    passed = pp_test_check(run, sse3 == has_named_feature(&view, "sse3"),
                           "IsProcessorFeaturePresent agrees with CPUID about SSE3") &&
             passed;
    if (passed) {
        pp_test_line(run, "%s: IsProcessorFeaturePresent agrees on sse, sse2 and sse3", area);
    }
    return passed;
}
