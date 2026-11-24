// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using System.Diagnostics;
using System.Reflection;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Paneport.Core.Logging;
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
// application-level template added the way the flavor adds its views, and each has a negative
// control with the generic template, which shows that the test reaches a lookup for null. Each
// guard checks what was thrown and built before what was logged, so that its first failure names
// the cause.
public sealed partial class ViewTemplatesTests
{
    private static readonly string s_viewTemplatesSource = Path.Combine("src", "Paneport.UI", "ViewTemplates.cs");

    private static readonly Lazy<TemplateRules> s_rules = new(TemplateRules.FromAvalonia);

    // The two ways the locale box has no selection: the saved settings have not loaded yet, as on a
    // device where the settings file exists, or the saved locale is not one that the page offers.
    public enum NoSelection
    {
        LoadPending,
        LocaleNotOffered,
    }

    // The two ways the locale drop-down recycles its items with today's locales, once its items
    // are as tall as larger text or more locales would make them: its list scrolls in a landscape
    // window, or the window turns while it is open.
    public enum Recycling
    {
        ScrolledInLandscape,
        TurnedWhileOpen,
    }

    // The ways a source of the app or of the device test flavor can add a template that matches
    // null, which the source test below rejects.
    public enum TemplateRule
    {
        // Avalonia's generic FuncDataTemplate, which matches null when its type is a reference type.
        GenericFuncDataTemplate,

        // One of Avalonia's templates whose match the caller supplies, such as the non-generic
        // FuncDataTemplate or FuncTreeDataTemplate, outside ViewTemplates.cs.
        CallerMatchTemplateOutsideViewTemplates,

        // A type that implements IDataTemplate or an interface that extends it, whose match can
        // take null.
        DataTemplateImplementation,

        // A using alias of one of these interfaces or templates, which would hide its name from
        // the rules above.
        TemplateAlias,
    }

    [AvaloniaFact]
    public void ATemplateMatchesAnInstanceOfItsTypeAndNeverNull()
    {
        using var warnings = AvaloniaWarnings.Record();
        var template = ViewTemplates.For<Marker>(marker => new TextBlock { Text = marker.Name });

        Assert.False(template.Match(null));
        Assert.True(template.Match(new Marker()));
        Assert.True(template.Match(new DerivedMarker()));
        Assert.False(template.Match("marker"));
        Assert.Equal("marker", Assert.IsType<TextBlock>(template.Build(new Marker())).Text);
        Assert.Null(template.Build(null));
        AssertNoEntries(warnings.Entries);
    }

    [AvaloniaTheory]
    [InlineData(NoSelection.LoadPending)]
    [InlineData(NoSelection.LocaleNotOffered)]
    public void SettingsWithoutASelectedLocaleBuildsNoApplicationTemplateForNull(NoSelection noSelection)
    {
        using var warnings = AvaloniaWarnings.Record();
        var builds = new List<Marker?>();
        var template = ViewTemplates.For<Marker>(marker =>
        {
            builds.Add(marker);
            return new TextBlock { Text = marker.Name };
        });

        var exception = ShowSettings(template, noSelection);

        Assert.True(exception is null, exception?.ToString());
        Assert.Empty(builds);
        AssertNoEntries(warnings.Entries);
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
        using var warnings = AvaloniaWarnings.Record();
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

        Assert.True(exception is null, exception?.ToString());
        Assert.NotNull(locale);
        Assert.NotEmpty(owners);
        Assert.All(owners, owner => Assert.Same(locale, owner));
        AssertNoEntries(warnings.Entries);
    }

    // Leaving a Settings page that has loaded with a locale selected, or choosing Settings again
    // while it is shown, takes the selection from the locale box: the view that leaves loses its
    // data context while it is still in the logical tree, and a view that stays first sees the new
    // page before it has loaded. Once the drop-down has been open, leaving clears its items too.
    // Each looks up a template for null inside the shell's page binding, which catches what a view
    // built for null throws there, so this test checks what is shown and logged, not only that
    // nothing was thrown. Choosing Settings again keeps the view and today's locale list, which is
    // the same list for every page, so the drop-down's items are not cleared then; that case
    // guards them should the list ever be the page's own. The developer page stands in for the
    // flavor's developer menu, with its view registered the way the flavor registers its views.
    [AvaloniaTheory]
    [InlineData(ShellSection.Library, false)]
    [InlineData(ShellSection.Library, true)]
    [InlineData(ShellSection.Diagnostics, false)]
    [InlineData(ShellSection.Diagnostics, true)]
    [InlineData(ShellSection.Developer, false)]
    [InlineData(ShellSection.Developer, true)]
    [InlineData(ShellSection.Settings, false)]
    [InlineData(ShellSection.Settings, true)]
    public void LeavingSettingsOrChoosingItAgainShowsTheNextPage(ShellSection next, bool dropDownOpened)
    {
        var builds = new List<DeveloperPage?>();
        var template = ViewTemplates.For<DeveloperPage>(page => DeveloperView(page, builds));

        var walk = WalkFromSettings(template, next, dropDownOpened);

        Assert.True(walk.Exception is null, walk.Exception?.ToString());
        Assert.DoesNotContain(null, builds);
        AssertNoEntries(walk.Warnings);
        Assert.NotEqual(0, walk.LookupsForNull);
        if (dropDownOpened && next != ShellSection.Settings)
        {
            Assert.NotEqual(0, walk.ItemLookupsForNull);
        }

        Assert.Same(walk.CurrentPage, walk.Content);
        Assert.IsType(ViewOf(next), walk.View);
        Assert.Same(walk.CurrentPage, walk.View!.DataContext);
        if (next == ShellSection.Developer)
        {
            Assert.Same(walk.CurrentPage, Assert.Single(builds));
            Assert.Same(walk.CurrentPage, ((DeveloperPageView)walk.View).Page);
        }
        else
        {
            Assert.Empty(builds);
        }

        if (next == ShellSection.Settings)
        {
            // The view stays, bound to the new page (app-shell.md section 5).
            Assert.Same(walk.SettingsView, walk.View);
        }
        else
        {
            Assert.Null(walk.SettingsView.Parent);
        }
    }

    // The negative control of the test above: with the developer page's view made by Avalonia's
    // generic FuncDataTemplate, the same steps build it for null. The shell's page binding catches
    // the exception and logs a binding warning, no page is shown, and a Settings view that left
    // stays in the logical tree.
    [AvaloniaTheory]
    [InlineData(ShellSection.Library, false)]
    [InlineData(ShellSection.Library, true)]
    [InlineData(ShellSection.Diagnostics, false)]
    [InlineData(ShellSection.Diagnostics, true)]
    [InlineData(ShellSection.Developer, false)]
    [InlineData(ShellSection.Developer, true)]
    [InlineData(ShellSection.Settings, false)]
    [InlineData(ShellSection.Settings, true)]
    public void LeavingSettingsOrChoosingItAgainBuildsAGenericFuncDataTemplateForNull(ShellSection next, bool dropDownOpened)
    {
        var builds = new List<DeveloperPage?>();
        var template = new FuncDataTemplate<DeveloperPage>((page, _) => DeveloperView(page, builds));

        var walk = WalkFromSettings(template, next, dropDownOpened);

        Assert.True(walk.Exception is null, walk.Exception?.ToString());
        Assert.Contains(null, builds);
        if (dropDownOpened && next != ShellSection.Settings)
        {
            Assert.NotEqual(0, walk.ItemLookupsForNull);
        }

        Assert.Contains(walk.Warnings, warning => warning.Contains("[Content, CurrentPage,", StringComparison.Ordinal));
        Assert.Null(walk.Content);
        Assert.Null(walk.View);
        if (next != ShellSection.Settings)
        {
            Assert.NotNull(walk.SettingsView.Parent);
        }
    }

    // A view built in code takes its page's values when it is built, as the developer menu's
    // buttons take its commands, so a template from ViewTemplates must never hand an old view to
    // a new page: choosing Developer again builds a new view for the new page
    // (ios-app-head.md section 10).
    [AvaloniaFact]
    public void ChoosingDeveloperAgainBuildsANewViewForTheNewPage()
    {
        using var warnings = AvaloniaWarnings.Record();
        var builds = new List<DeveloperPage?>();
        var template = ViewTemplates.For<DeveloperPage>(page => DeveloperView(page, builds));
        Application.Current!.DataTemplates.Add(template);
        try
        {
            var fakes = new FakeServices();
            using var harness = ShellHarness.Show(fakes, 390, 844, _ => new DeveloperPage(fakes.Log));
            harness.OpenSection(ShellSection.Developer);
            var first = harness.Page<DeveloperPageView>();
            harness.OpenSection(ShellSection.Developer);
            var second = harness.Page<DeveloperPageView>();

            DeveloperPage?[] pagesBuilt = [first.Page, second.Page];

            Assert.NotSame(first, second);
            Assert.Same(harness.Shell.CurrentPage, second.Page);
            Assert.Equal(pagesBuilt, builds);
            AssertNoEntries(warnings.Entries);
        }
        finally
        {
            Application.Current!.DataTemplates.Remove(template);
        }
    }

    // When the locale drop-down recycles an item, it clears the item's content before its template,
    // and the item's presenter looks up a template for null inside the layout pass, where nothing
    // catches what a view built for null throws.
    [AvaloniaTheory]
    [InlineData(Recycling.ScrolledInLandscape)]
    [InlineData(Recycling.TurnedWhileOpen)]
    public void TheLocaleDropDownRecyclesItsItemsWithoutBuildingATemplateForNull(Recycling recycling)
    {
        using var warnings = AvaloniaWarnings.Record();
        var builds = new List<DeveloperPage?>();
        var template = ViewTemplates.For<DeveloperPage>(page => DeveloperView(page, builds));

        var (exception, itemLookupsForNull) = RecycleLocaleItems(template, recycling);

        Assert.True(exception is null, exception?.ToString());
        Assert.Empty(builds);
        AssertNoEntries(warnings.Entries);
        Assert.NotEqual(0, itemLookupsForNull);
    }

    // The negative control of the test above: the generic template is built for null, and the
    // exception leaves the layout pass, which on the device ends the app.
    [AvaloniaTheory]
    [InlineData(Recycling.ScrolledInLandscape)]
    [InlineData(Recycling.TurnedWhileOpen)]
    public void TheLocaleDropDownRecyclesItsItemsIntoAGenericFuncDataTemplateForNull(Recycling recycling)
    {
        var builds = new List<DeveloperPage?>();
        var template = new FuncDataTemplate<DeveloperPage>((page, _) => DeveloperView(page, builds));

        var (exception, _) = RecycleLocaleItems(template, recycling);

        Assert.IsType<NullReferenceException>(exception);
        Assert.NotEmpty(builds);
        Assert.All(builds, Assert.Null);
    }

    [AvaloniaFact]
    public void NoTemplateOfTheApplicationOrOfAPageMatchesNull()
    {
        using var warnings = AvaloniaWarnings.Record();

        Assert.DoesNotContain(Application.Current!.DataTemplates, template => template.Match(null));

        Control[] views = [new MainView(), new LibraryPage(), new TitleDetailsPage(), new SettingsPage(), new DiagnosticsPage()];
        Assert.All(views, view => Assert.DoesNotContain(view.DataTemplates, template => template.Match(null)));
        AssertNoEntries(warnings.Entries);
    }

    // The device test flavor cannot run on Windows, so this test reads its sources and those of the
    // app: views built in code get their templates from ViewTemplates, and views in XAML from a
    // data template with a data type, which Avalonia requires in a template collection.
    [Fact]
    public void TheAppAndTheDeviceTestFlavorMakeViewTemplatesOnlyThroughViewTemplates()
    {
        var root = RepositoryRoot();
        string[] folders = ["src", Path.Combine("tests", "device")];

        var sources = folders
            .SelectMany(folder => Directory.EnumerateFiles(Path.Combine(root, folder), "*.cs", SearchOption.AllDirectories))
            .Select(file => Path.GetRelativePath(root, file))
            .Where(file => !file.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(part => part is "bin" or "obj"))
            .ToList();

        var broken = sources
            .SelectMany(file => BrokenRules(file, File.ReadAllText(Path.Combine(root, file))).Select(rule => $"{file}: {rule}"))
            .ToList();

        Assert.Contains(Path.Combine("tests", "device", "DeveloperViews.cs"), sources);
        Assert.Contains(s_viewTemplatesSource, sources);
        AssertNoEntries(broken);
    }

    // Each rule of the test above, broken by a planted source, each name that the rules take from
    // Avalonia's assemblies, and each kind of type declaration and of literal that the rules read.
    // ViewTemplates.cs may make the non-generic template, and the rules read code only, not
    // comments or literals.
    [Theory]
    [InlineData("tests/device/DeveloperViews.cs", "Add(new FuncDataTemplate<DeveloperMenuViewModel>((menu, _) => Menu(menu)));", TemplateRule.GenericFuncDataTemplate)]
    [InlineData("src/Paneport.UI/ViewTemplates.cs", "return new FuncDataTemplate<T>((model, _) => build(model));", TemplateRule.GenericFuncDataTemplate)]
    [InlineData("tests/device/DeveloperViews.cs", "Add(new FuncDataTemplate(data => data is DeveloperMenuViewModel, (data, _) => Menu((DeveloperMenuViewModel)data!)));", TemplateRule.CallerMatchTemplateOutsideViewTemplates)]
    [InlineData("tests/device/DeveloperViews.cs", "Add(new FuncTreeDataTemplate(data => data is null || data is DeveloperMenuViewModel, (data, _) => Menu((DeveloperMenuViewModel)data!), _ => []));", TemplateRule.CallerMatchTemplateOutsideViewTemplates)]
    [InlineData("tests/device/DeveloperViews.cs", "internal sealed class MenuTemplate() : FuncTreeDataTemplate(_ => true, (_, _) => null, _ => []);", TemplateRule.CallerMatchTemplateOutsideViewTemplates)]
    [InlineData("src/Paneport.UI/Views/EmptyView.cs", "internal sealed class EmptyView : IDataTemplate\n{\n    public bool Match(object? data) => true;\n}", TemplateRule.DataTemplateImplementation)]
    [InlineData("tests/device/DeveloperViews.cs", "public sealed class Views(int count)\n    : List<Control>, Avalonia.Controls.Templates.IRecyclingDataTemplate\n{", TemplateRule.DataTemplateImplementation)]
    [InlineData("tests/device/DeveloperViews.cs", "internal sealed class MenuTemplate : ITypedDataTemplate { }", TemplateRule.DataTemplateImplementation)]
    [InlineData("tests/device/DeveloperViews.cs", "internal sealed class Views : ITreeDataTemplate { }", TemplateRule.DataTemplateImplementation)]
    [InlineData("tests/device/DeveloperViews.cs", "internal sealed record Views(int A) : IDataTemplate;", TemplateRule.DataTemplateImplementation)]
    [InlineData("tests/device/DeveloperViews.cs", "internal struct Views : IDataTemplate { }", TemplateRule.DataTemplateImplementation)]
    [InlineData("tests/device/DeveloperViews.cs", "public interface IViews : IDataTemplate { }", TemplateRule.DataTemplateImplementation)]
    [InlineData("tests/device/DeveloperViews.cs", "using Template = Avalonia.Controls.Templates.IDataTemplate;\ninternal sealed class MenuTemplate : Template { }", TemplateRule.TemplateAlias)]
    [InlineData("tests/device/DeveloperViews.cs", "var link = \"https://example.com/\"; Add(new FuncDataTemplate<DeveloperMenuViewModel>(Build));", TemplateRule.GenericFuncDataTemplate)]
    [InlineData("tests/device/DeveloperViews.cs", "var c = '\"'; Add(new FuncDataTemplate<DeveloperMenuViewModel>(Build)); var d = '\"';", TemplateRule.GenericFuncDataTemplate)]
    [InlineData("tests/device/DeveloperViews.cs", "var path = @\"C:\\\"; Add(new FuncDataTemplate<DeveloperMenuViewModel>(Build, \"x\"));", TemplateRule.GenericFuncDataTemplate)]
    [InlineData("src/Paneport.UI/ViewTemplates.cs", "return new FuncDataTemplate(data => data is T, (data, _) => data is T model ? build(model) : null);", null)]
    [InlineData("tests/device/DeveloperViews.cs", "/* new FuncDataTemplate(match, build) */\n// Avalonia's generic FuncDataTemplate<T> matches null.\nvar text = \"class View : IDataTemplate {\";", null)]
    public void EachRuleForViewTemplatesBreaksOnAPlantedSource(string file, string source, TemplateRule? rule)
    {
        TemplateRule[] expected = rule is { } broken ? [broken] : [];

        Assert.Equal(expected, BrokenRules(file.Replace('/', Path.DirectorySeparatorChar), source));
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

    // Opens Settings, whose settings load at once with a locale selected, opens and closes the
    // locale drop-down if asked, and then opens the next section through the navigation bar. The
    // template is added to the application's templates, and the lookups for null that reach them
    // are counted for the last step only.
    private static Walk WalkFromSettings(IDataTemplate template, ShellSection next, bool dropDownOpened)
    {
        var fakes = new FakeServices();
        var lookups = new NullLookups();
        var templates = Application.Current!.DataTemplates;
        templates.Insert(0, lookups);
        templates.Add(template);
        try
        {
            using var warnings = AvaloniaWarnings.Record();
            using var harness = ShellHarness.Show(fakes, 390, 844, _ => new DeveloperPage(fakes.Log));
            harness.OpenSection(ShellSection.Settings);
            var settings = harness.Page<SettingsPage>();
            var locale = settings.FindControl<ComboBox>("LocaleChoice")!;
            Assert.NotNull(locale.SelectedItem);
            if (dropDownOpened)
            {
                locale.IsDropDownOpen = true;
                harness.Settle();
                Assert.True(locale.IsDropDownOpen, "The locale drop-down did not open.");
                Assert.Equal(locale.ItemCount, locale.GetRealizedContainers().Count());
                locale.IsDropDownOpen = false;
                harness.Settle();
            }

            lookups.Reset();
            var exception = Record.Exception(() => harness.OpenSection(next));
            var page = harness.View.FindControl<ContentControl>("Page")!;
            return new Walk(
                exception,
                lookups.Count,
                lookups.FromItems,
                [.. warnings.Entries],
                harness.Shell.CurrentPage,
                page.Content,
                page.Presenter?.Child,
                settings);
        }
        finally
        {
            templates.Remove(template);
            templates.Remove(lookups);
        }
    }

    // Opens the locale drop-down with items 34 high, as the completeness critic's probe made them,
    // so that today's 13 locales overflow, and makes it recycle them. The template is added to the
    // application's templates, and the lookups for null that the drop-down's items make are
    // counted once it is open.
    private static (Exception? Exception, int ItemLookupsForNull) RecycleLocaleItems(IDataTemplate template, Recycling recycling)
    {
        var lookups = new NullLookups();
        var templates = Application.Current!.DataTemplates;
        templates.Insert(0, lookups);
        templates.Add(template);
        try
        {
            var (width, height) = recycling == Recycling.ScrolledInLandscape ? (812, 375) : (390, 844);
            using var harness = ShellHarness.Show(new FakeServices(), width, height);
            harness.OpenSection(ShellSection.Settings);
            var locale = harness.Page<SettingsPage>().FindControl<ComboBox>("LocaleChoice")!;
            locale.Styles.Add(new Style(selector => selector.OfType<ComboBoxItem>())
            {
                Setters = { new Setter(Layoutable.MinHeightProperty, 34.0) },
            });
            harness.Settle();
            var exception = Record.Exception(() =>
            {
                locale.IsDropDownOpen = true;
                harness.Settle();
                Assert.True(locale.IsDropDownOpen, "The locale drop-down did not open.");
                Assert.NotEmpty(locale.GetRealizedContainers());
                lookups.Reset();
                if (recycling == Recycling.ScrolledInLandscape)
                {
                    var list = locale.GetVisualDescendants().OfType<Popup>().Single().Child!.GetVisualDescendants().OfType<ScrollViewer>().First();
                    var end = list.Extent.Height - list.Viewport.Height;
                    for (var step = 0; step < 20 && list.Offset.Y < end; step++)
                    {
                        list.Offset = new Vector(0, Math.Min(list.Offset.Y + 120, end));
                        harness.Settle();
                    }
                }
                else
                {
                    harness.Window.Width = 844;
                    harness.Window.Height = 390;
                    harness.Settle();
                }
            });
            return (exception, lookups.FromItems);
        }
        finally
        {
            templates.Remove(template);
            templates.Remove(lookups);
        }
    }

    // The rules for view templates that a source breaks, read from its code without comments or
    // literals.
    private static List<TemplateRule> BrokenRules(string file, string source)
    {
        var rules = s_rules.Value;
        var code = CommentsAndLiterals().Replace(source, " ");
        var broken = new List<TemplateRule>();
        if (GenericFuncDataTemplateCode().IsMatch(code))
        {
            broken.Add(TemplateRule.GenericFuncDataTemplate);
        }

        if (file != s_viewTemplatesSource && rules.CallerMatchTemplate.IsMatch(code))
        {
            broken.Add(TemplateRule.CallerMatchTemplateOutsideViewTemplates);
        }

        if (rules.Implementation.IsMatch(code))
        {
            broken.Add(TemplateRule.DataTemplateImplementation);
        }

        if (rules.Alias.IsMatch(code))
        {
            broken.Add(TemplateRule.TemplateAlias);
        }

        return broken;
    }

    // Fails with every entry in full, where Assert.Empty would cut each one short.
    private static void AssertNoEntries(IReadOnlyCollection<string> entries) =>
        Assert.True(entries.Count == 0, string.Join(Environment.NewLine, entries));

    // The view of the page shown after a section was opened.
    private static Type ViewOf(ShellSection section) => section switch
    {
        ShellSection.Library => typeof(LibraryPage),
        ShellSection.Settings => typeof(SettingsPage),
        ShellSection.Diagnostics => typeof(DiagnosticsPage),
        _ => typeof(DeveloperPageView),
    };

    // The developer page's view, which records what it was built for. Like the flavor's views, it
    // reads its page first, so a view built for null fails at once.
    private static DeveloperPageView DeveloperView(DeveloperPage page, List<DeveloperPage?> builds)
    {
        builds.Add(page);
        return new DeveloperPageView(page) { Child = new TextBlock { Text = page.Header } };
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

    // Comments, and string and character literals, which can hold a comment's marker.
    [GeneratedRegex("""//[^\n]*|/\*.*?\*/|(?:@\$*|\$+@)"(?:""|[^"])*"|\$*"(?:\\.|[^"\\\n])*"|'(?:\\.|[^'\\\n])*'""", RegexOptions.Singleline)]
    private static partial Regex CommentsAndLiterals();

    [GeneratedRegex(@"\bFuncDataTemplate\s*<")]
    private static partial Regex GenericFuncDataTemplateCode();

    // The rules that take their names from Avalonia's assemblies, by reflection, so that a template
    // interface or a template with the caller's match that a later version adds is caught too.
    private sealed record TemplateRules(Regex CallerMatchTemplate, Regex Implementation, Regex Alias)
    {
        public static TemplateRules FromAvalonia()
        {
            var types = typeof(ViewTemplates).Assembly.GetReferencedAssemblies()
                .Where(name => name.Name is "Avalonia" || name.Name?.StartsWith("Avalonia.", StringComparison.Ordinal) == true)
                .Select(Assembly.Load)
                .SelectMany(assembly => assembly.GetExportedTypes())
                .ToList();

            // Every public interface that extends IDataTemplate, and IDataTemplate itself.
            var interfaces = Names(types.Where(type => type.IsInterface && typeof(IDataTemplate).IsAssignableFrom(type)));

            // Every public template that takes its match as a function of the data, so that the
            // caller decides whether it matches null.
            var callerMatch = Names(types.Where(type =>
                type.IsClass
                && !type.IsGenericTypeDefinition
                && typeof(IDataTemplate).IsAssignableFrom(type)
                && type.GetConstructors().Any(constructor =>
                    constructor.GetParameters().Any(parameter => parameter.ParameterType == typeof(Func<object?, bool>)))));

            return new TemplateRules(
                new Regex($@"\b(?:{callerMatch})\b(?!\s*<)"),
                new Regex($@"\b(?:class|struct|record|interface)\s+[^{{;]*?:[^{{;]*\b(?:{interfaces})\b"),
                new Regex($@"\busing\s+@?\w+\s*=[^;]*\b(?:{interfaces}|{callerMatch})\b"));
        }

        // The types' names without their generic arity, as an alternation.
        private static string Names(IEnumerable<Type> types) =>
            string.Join('|', types.Select(type => Regex.Escape(type.Name.Split('`')[0])).Distinct().Order(StringComparer.Ordinal));
    }

    // What a walk from Settings left on the screen and in the log.
    private sealed record Walk(
        Exception? Exception,
        int LookupsForNull,
        int ItemLookupsForNull,
        IReadOnlyList<string> Warnings,
        PageViewModel CurrentPage,
        object? Content,
        Control? View,
        SettingsPage SettingsView);

    // Matches nothing and counts the lookups for null that reach it, and among them those of the
    // item containers that are being cleared for reuse. First among the application's templates,
    // it shows that a step looked up a template for null there.
    private sealed class NullLookups : IDataTemplate
    {
        public int Count { get; private set; }

        public int FromItems { get; private set; }

        public void Reset()
        {
            Count = 0;
            FromItems = 0;
        }

        public bool Match(object? data)
        {
            if (data is null)
            {
                Count++;
                if (new StackTrace().GetFrames().Any(frame => frame.GetMethod()?.Name == "ClearContainerForItemOverride"))
                {
                    FromItems++;
                }
            }

            return false;
        }

        public Control? Build(object? param) => null;
    }

    // Stands in for the device test flavor's developer menu.
    private sealed class DeveloperPage(ILog log) : PageViewModel(log)
    {
        public override string Header => "Developer";
    }

    // The developer page's view: a type of its own, which Avalonia's fallback for content without a
    // template, a text block, cannot be.
    private sealed class DeveloperPageView(DeveloperPage page) : Border
    {
        public DeveloperPage Page { get; } = page;
    }

    private class Marker
    {
        public string Name { get; } = "marker";
    }

    private sealed class DerivedMarker : Marker;
}
