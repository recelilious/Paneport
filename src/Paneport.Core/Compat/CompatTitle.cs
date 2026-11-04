// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

namespace Paneport.Core.Compat;

/// <summary>
/// What the compatibility database knows about a title: each layer's file and the settings they
/// give together.
/// </summary>
/// <param name="Bundled">What reading the bundled entry gave.</param>
/// <param name="Device">What reading the device entry gave.</param>
/// <param name="Effective">
/// The settings the title runs with, from the layers that were loaded. A rejected file adds none of
/// its values, and the caller reports its problems.
/// </param>
public sealed record CompatTitle(TitleEntryResult Bundled, TitleEntryResult Device, EffectiveTitleSettings Effective);
