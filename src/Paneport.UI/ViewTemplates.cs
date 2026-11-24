// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Avalonia.Controls;
using Avalonia.Controls.Templates;

namespace Paneport.UI;

/// <summary>
/// Makes the data templates that match views built in code to their view models by type
/// (app-shell.md section 5).
/// </summary>
/// <remarks>
/// A view template must never match null. Avalonia's <see cref="FuncDataTemplate{T}"/> does, when
/// its type is a reference type. A content presenter that has no content but a template of its
/// own that does not match null looks further, through the logical tree and then the application's
/// templates, and builds the first template that matches: the locale box of the Settings page does
/// so while no locale is selected. A view built in code for null fails on its first member during
/// layout, where nothing handles the exception, and the app ends. The templates made here match an
/// instance of their type only.
/// </remarks>
public static class ViewTemplates
{
    /// <summary>
    /// Makes a data template that matches an instance of <typeparamref name="T"/>, never null,
    /// and builds its view with <paramref name="build"/>.
    /// </summary>
    /// <typeparam name="T">The view model type that the template matches.</typeparam>
    /// <param name="build">Builds the view of a view model.</param>
    /// <returns>The data template.</returns>
    public static IDataTemplate For<T>(Func<T, Control> build)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(build);
        return new FuncDataTemplate(data => data is T, (data, _) => data is T model ? build(model) : null);
    }
}
