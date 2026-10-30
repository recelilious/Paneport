// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using System.Diagnostics;

namespace Paneport.Core.Security;

/// <summary>
/// A secret value, such as a Steam refresh token, whose text form is redacted, so that the
/// secret cannot reach a log line by accident (C-0010 section 2).
/// </summary>
/// <remarks>
/// <see cref="ToString"/>, string interpolation and composite formatting all give
/// <see cref="Redacted"/>, and the debugger shows the same. Only <see cref="Reveal"/> returns the
/// value, so every use of a secret is easy to find.
/// </remarks>
[DebuggerDisplay(Redacted)]
public sealed class Secret
{
    /// <summary>The text that stands for every secret.</summary>
    public const string Redacted = "[redacted]";

    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    private readonly string _value;

    /// <summary>Wraps a secret value.</summary>
    /// <param name="value">The secret, which is never empty.</param>
    /// <exception cref="ArgumentException"><paramref name="value"/> is empty.</exception>
    public Secret(string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(value);
        _value = value;
    }

    /// <summary>Returns the secret itself. Never log, display or store the result.</summary>
    /// <returns>The secret value.</returns>
    public string Reveal() => _value;

    /// <summary>Returns <see cref="Redacted"/>, never the secret.</summary>
    /// <returns><see cref="Redacted"/>.</returns>
    public override string ToString() => Redacted;
}
