// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Paneport.Core.Settings;
using Paneport.Core.ViewModels;
using Paneport.Tests.Fakes;
using Paneport.UI.Views;
using Xunit;

namespace Paneport.UI.Tests;

// A view template must never match null (app-shell.md section 5). The device test flavor added its
// developer views to the application's templates with Avalonia's generic FuncDataTemplate, which
// matches null too, and on the device the Settings page built the developer menu's view for null
// and ended the app. The Settings tests below show the page on a phone-sized window with an
// application-level template added the way the flavor adds its views.
public sealed class ViewTemplatesTests
{
    // The two ways the locale box has no selection: the saved settings have not loaded yet, as on a
    // device where the settings file exists, or the saved locale is not one that the page offers.
    public enum NoSelection
    {
        LoadPending,
        LocaleNotOffered,
    }

    [AvaloniaFact]
    public void ATemplateMatchesAnInstanceOfItsTypeAndNeverNull()
    {
        var template = ViewTemplates.For<Marker>(marker => new TextBlock { Text = marker.Name });

        Assert.False(template.Match(null));
        Assert.True(template.Match(new Marker()));
        Assert.True(template.Match(new DerivedMarker()));
        Assert.False(template.Match("marker"));
        Assert.Equal("marker", Assert.IsType<TextBlock>(template.Build(new Marker())).Text);
        Assert.Null(template.Build(null));
    }

    [AvaloniaTheory]
    [InlineData(NoSelection.LoadPending)]
    [InlineData(NoSelection.LocaleNotOffered)]
    public void SettingsWithoutASelectedLocaleBuildsNoApplicationTemplateForNull(NoSelection noSelection)
    {
        using var warnings = AvaloniaWarnings.Record();
        var builds = new List<Marker>();
        var template = ViewTemplates.For<Marker>(marker =>
        {
            builds.Add(marker);
            return new TextBlock { Text = marker.Name };
        });

        var exception = ShowSettings(template, noSelection);

        Assert.Null(exception);
        Assert.Empty(builds);
        Assert.Empty(warnings.Entries);
    }

    // The negative control of the test above: the same page with the same template made by
    // Avalonia's generic FuncDataTemplate builds the template for null, and the view's first member
    // ends the layout with the exception that ended the app on the device. If Avalonia stops
    // matching null here, this test fails, and the test above needs another way to reach a lookup
    // for null.
    [AvaloniaTheory]
    [InlineData(NoSelection.LoadPending)]
    [InlineData(NoSelection.LocaleNotOffered)]
    public void SettingsWithoutASelectedLocaleBuildsAGenericFuncDataTemplateForNull(NoSelection noSelection)
    {
        var builds = new List<Marker?>();
        var template = new FuncDataTemplate<Marker>((marker, _) =>
        {
            builds.Add(marker);
            return new TextBlock { Text = marker.Name };
        });

        var exception = ShowSettings(template, noSelection);

        Assert.IsType<NullReferenceException>(exception);
        Assert.NotEmpty(builds);
        Assert.All(builds, Assert.Null);
    }

    // What looks up a template for null is the locale box while it has no selection: a generic
    // FuncDataTemplate that builds an empty view instead of failing ends up inside it.
    [AvaloniaFact]
    public void TheLocaleBoxLooksUpATemplateForNullWhileItHasNoSelection()
    {
        var builtForNull = new List<Control>();
        var template = new FuncDataTemplate<Marker>((marker, _) =>
        {
            var view = new Border();
            if (marker is null)
            {
                builtForNull.Add(view);
            }

            return view;
        });
        ComboBox? locale = null;
        List<ComboBox?> owners = [];

        var exception = ShowSettings(template, NoSelection.LoadPending, harness =>
        {
            locale = harness.Page<SettingsPage>().FindControl<ComboBox>("LocaleChoice");
            owners = [.. builtForNull.Select(view => view.FindAncestorOfType<ComboBox>())];
        });

        Assert.Null(exception);
        Assert.NotNull(locale);
        Assert.NotEmpty(owners);
        Assert.All(owners, owner => Assert.Same(locale, owner));
    }

    [AvaloniaFact]
    public void NoTemplateOfTheApplicationOrOfAPageMatchesNull()
    {
        Assert.DoesNotContain(Application.Current!.DataTemplates, template => template.Match(null));

        Control[] views = [new MainView(), new LibraryPage(), new TitleDetailsPage(), new SettingsPage(), new DiagnosticsPage()];
        Assert.All(views, view => Assert.DoesNotContain(view.DataTemplates, template => template.Match(null)));
    }

    // The device test flavor cannot run on Windows, so this test reads its sources: no file of the
    // app or of the flavor names the generic FuncDataTemplate, and views built in code go through
    // ViewTemplates instead.
    [Fact]
    public void NoSourceOfTheAppOrTheDeviceTestFlavorUsesTheGenericFuncDataTemplate()
    {
        var root = RepositoryRoot();
        string[] folders = ["src", Path.Combine("tests", "device")];

        var sources = folders
            .SelectMany(folder => Directory.EnumerateFiles(Path.Combine(root, folder), "*.cs", SearchOption.AllDirectories))
            .Select(file => Path.GetRelativePath(root, file))
            .Where(file => !file.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(part => part is "bin" or "obj"))
            .ToList();

        var generic = sources
            .Where(file => File.ReadAllText(Path.Combine(root, file)).Contains("FuncDataTemplate<", StringComparison.Ordinal))
            .ToList();

        Assert.Contains(Path.Combine("tests", "device", "DeveloperViews.cs"), sources);
        Assert.Empty(generic);
    }

    // Shows the Settings page with the template added to the application's templates, then lets a
    // pending load finish, and returns what was thrown, if anything. The inspection runs while the
    // locale box has no selection.
    private static Exception? ShowSettings(IDataTemplate template, NoSelection noSelection, Action<ShellHarness>? inspect = null)
    {
        var fakes = new FakeServices();
        if (noSelection == NoSelection.LoadPending)
        {
            fakes.Settings.LoadGate = new TaskCompletionSource();
        }
        else
        {
            fakes.Settings.Saved = new AppSettings { Locale = "xx-XX" };
        }

        Application.Current!.DataTemplates.Add(template);
        try
        {
            using var harness = ShellHarness.Show(fakes, 390, 844);
            return Record.Exception(() =>
            {
                harness.OpenSection(ShellSection.Settings);
                inspect?.Invoke(harness);
                fakes.Settings.LoadGate?.SetResult();
                harness.Settle();
            });
        }
        finally
        {
            Application.Current!.DataTemplates.Remove(template);
        }
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Paneport.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("No folder above the test assembly holds Paneport.slnx.");
    }

    private class Marker
    {
        public string Name { get; } = "marker";
    }

    private sealed class DerivedMarker : Marker;
}
