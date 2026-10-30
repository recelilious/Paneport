// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using CommunityToolkit.Mvvm.ComponentModel;

namespace Paneport.Core.ViewModels;

/// <summary>
/// An entry of the shell's navigation bar: a section, and whether the bar marks it as the one
/// shown.
/// </summary>
public sealed partial class NavigationEntry : ObservableObject
{
    /// <summary>Creates the entry of a section.</summary>
    /// <param name="section">The section that the entry opens.</param>
    public NavigationEntry(ShellSection section)
    {
        Section = section;
    }

    /// <summary>Gets the section that the entry opens.</summary>
    public ShellSection Section { get; }

    /// <summary>Gets the entry's name, the section's name as <see cref="ShellSection"/> writes it.</summary>
    public string Name => Section.ToString();

    /// <summary>Gets a value indicating whether the navigation bar marks this entry.</summary>
    [ObservableProperty]
    public partial bool IsSelected { get; internal set; }
}
