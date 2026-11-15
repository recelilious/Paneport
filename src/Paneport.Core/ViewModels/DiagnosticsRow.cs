// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

namespace Paneport.Core.ViewModels;

/// <summary>A label and its value, as a section of the Diagnostics page lists them.</summary>
/// <param name="Label">What the value is.</param>
/// <param name="Value">The value, as text.</param>
public sealed record DiagnosticsRow(string Label, string Value);
