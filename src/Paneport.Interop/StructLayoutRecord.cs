// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

namespace Paneport.Interop;

/// <summary>The layout of one struct of the host interface, as C or C# defines it.</summary>
/// <param name="Name">The C type name, such as <c>pp_memory_budget_t</c>.</param>
/// <param name="Size">The struct's size in bytes.</param>
/// <param name="Members">The members, in declaration order.</param>
public sealed record StructLayoutRecord(string Name, int Size, IReadOnlyList<MemberLayoutRecord> Members);
