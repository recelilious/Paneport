// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Paneport.Core.Library;

namespace Paneport.Core.Compat;

/// <summary>
/// One <c>*.json</c> file of a layer's folder and what reading it gave.
/// </summary>
/// <param name="FileName">The file's name, without its folder.</param>
/// <param name="Id">
/// The identity that the name gives, or null when the name is not <c>steam-&lt;appid&gt;.json</c> or
/// <c>local-&lt;slug&gt;.json</c>; the file is then rejected without being read.
/// </param>
/// <param name="Result">The entry, or the rules the file breaks.</param>
public sealed record CompatFile(string FileName, TitleId? Id, TitleEntryResult Result);
