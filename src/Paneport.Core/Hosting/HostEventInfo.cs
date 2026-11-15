// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

namespace Paneport.Core.Hosting;

/// <summary>
/// An event of the native host (<c>pp_event_t</c>, C-0005 section 2). Version 1 of the interface
/// defines no kind yet; each later function group defines its kinds and what the other values
/// hold for them.
/// </summary>
/// <param name="Kind">Which event this is.</param>
/// <param name="Code">A value whose meaning the kind defines, such as an exit code.</param>
/// <param name="Handle">The handle that the event is about, or 0.</param>
/// <param name="Address">An address whose meaning the kind defines, or 0.</param>
public readonly record struct HostEventInfo(uint Kind, long Code, ulong Handle, ulong Address);
