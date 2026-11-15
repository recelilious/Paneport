// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

// pp_device_get_model, which version 1.1 of the interface added for the Diagnostics page.

#include <stddef.h>
#include <stdint.h>

#include "error.h"
#include "paneport/host.h"
#include "platform.h"

PP_EXPORT pp_status_t pp_device_get_model(char *buffer, uint32_t buffer_size) {
    pp_error_clear();
    if (buffer == NULL) {
        return pp_error_set(PP_E_INVALID_ARGUMENT, "pp_device_get_model needs a buffer.");
    }

    size_t needed = 0U;
    if (!pp_platform_device_model(buffer, buffer_size, &needed)) {
        return pp_error_set(PP_E_SYSTEM,
                            "The system did not report the device's model identifier.");
    }
    if (needed > buffer_size) {
        return pp_error_set(PP_E_INVALID_ARGUMENT,
                            "pp_device_get_model needs a buffer of %zu bytes, not %u.", needed,
                            (unsigned)buffer_size);
    }
    return PP_OK;
}
