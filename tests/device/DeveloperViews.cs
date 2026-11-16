// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;

namespace Paneport.DeviceTest;

// The views of the developer pages, built in code and matched to their view models by type, as
// App.axaml matches the shell's pages (app-shell.md section 5); no view is looked up by name. They
// use the shell's styles: buttons of the class "title", texts of the classes "name" and
// "secondary".
internal static class DeveloperViews
{
    public static void Register(Application application)
    {
        application.DataTemplates.Add(new FuncDataTemplate<DeveloperMenuViewModel>((menu, _) => Menu(menu)));
        application.DataTemplates.Add(new FuncDataTemplate<DeveloperToolViewModel>((tool, _) => Tool(tool)));
    }

    private static ScrollViewer Menu(DeveloperMenuViewModel menu)
    {
        var panel = new StackPanel { Margin = new Thickness(16), Spacing = 12 };
        foreach (var entry in menu.Entries)
        {
            var button = new Button
            {
                Command = menu.OpenCommand,
                CommandParameter = entry,
                Content = new StackPanel
                {
                    Spacing = 4,
                    Children =
                    {
                        new TextBlock { Text = entry.Title, Classes = { "name" } },
                        new TextBlock { Text = entry.Description, TextWrapping = TextWrapping.Wrap, Classes = { "secondary" } },
                    },
                },
            };
            button.Classes.Add("title");
            panel.Children.Add(button);
        }

        return new ScrollViewer { Content = panel };
    }

    private static ScrollViewer Tool(DeveloperToolViewModel tool)
    {
        var result = new SelectableTextBlock { Text = tool.Result, TextWrapping = TextWrapping.Wrap };
        tool.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(DeveloperToolViewModel.Result))
            {
                result.Text = tool.Result;
            }
        };

        var buttons = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var action in tool.Actions)
        {
            buttons.Children.Add(new Button
            {
                Content = action.Label,
                Command = tool.RunCommand,
                CommandParameter = action,
                Margin = new Thickness(0, 0, 12, 12),
            });
        }

        return new ScrollViewer
        {
            Content = new StackPanel
            {
                Margin = new Thickness(16),
                Spacing = 16,
                Children =
                {
                    new TextBlock { Text = tool.Description, TextWrapping = TextWrapping.Wrap, Classes = { "secondary" } },
                    buttons,
                    result,
                },
            },
        };
    }
}
