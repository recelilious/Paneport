// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

namespace Paneport.Core.Compat;

/// <summary>
/// A rule that a title file or entry breaks.
/// </summary>
/// <remarks>
/// A problem names the field and the rule, never the value: a rejected value may be a local path
/// or other personal data, and problems reach logs (C-0010 section 2). A field name that is not a
/// plain name shows as <c>&lt;name not shown&gt;</c>.
/// </remarks>
/// <param name="Rule">The kind of rule.</param>
/// <param name="Path">The field, as a JSON path such as <c>$.graphics.renderScale</c>.</param>
/// <param name="Message">What the rule asks for.</param>
public sealed record CompatProblem(CompatRule Rule, string Path, string Message)
{
    /// <summary>Gets the problem as one line.</summary>
    /// <returns>The path, the rule and the message.</returns>
    public override string ToString() => $"{Path}: {Rule}: {Message}";
}
