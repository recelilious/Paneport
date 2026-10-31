// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

namespace Paneport.Core.Compat;

/// <summary>
/// The two layers of title entries (C-0008 section 1). Both use the same fields, and each has
/// rules of its own.
/// </summary>
public enum CompatLayer
{
    /// <summary>
    /// The snapshot of <c>compat/</c> that the app bundles: reviewed entries, which follow the title
    /// schema.
    /// </summary>
    Bundled,

    /// <summary>
    /// The device's own layer: the computed classification and the user's overrides. It never holds
    /// <c>launch</c>, <c>rating</c> or <c>reports</c>, and its <c>ceg</c> is always <c>unknown</c>.
    /// </summary>
    Device,
}
