// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

namespace Paneport.Interop;

/// <summary>The layout of one member of a struct of the host interface.</summary>
/// <param name="Name">The member's name as the C header spells it, such as <c>struct_size</c>.</param>
/// <param name="Offset">The member's offset from the start of the struct, in bytes.</param>
/// <param name="Size">The member's size in bytes.</param>
public sealed record MemberLayoutRecord(string Name, int Offset, int Size);
