using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;

namespace ModHearth.UI.Helpers
{
    public static class ButtonHelpers
    {
        public static Button CreateHelpButton(string url, string tooltip = "Click here to view documentation")
        {
            Button helpButton = new()
            {
                Content = "?",
                Width = 18,
                Height = 18,
                Padding = new Thickness(0),
                HorizontalContentAlignment = HorizontalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 12,
                Cursor = new Cursor(StandardCursorType.Hand)
            };

            if (!string.IsNullOrEmpty(tooltip))
            {
                ToolTip.SetTip(helpButton, tooltip);
            }

            helpButton.Click += (_, e) =>
            {
                e.Handled = true;

                try
                {
                    using Process? process = Process.Start(new ProcessStartInfo
                    {
                        FileName = url,
                        UseShellExecute = true
                    });
                }
                catch
                {
                    // Ignore or log if needed
                }
            };

            return helpButton;
        }

        public static void AttachHelpButton(Button button, string url, string tooltip = "Click here to view documentation")
        {
            AttachHelpButton(button, button.Content, url, tooltip);
        }

        public static void AttachHelpButton(Button button, object? content, string url, string tooltip = "Click here to view documentation")
        {
            Grid grid = new()
            {
                ColumnDefinitions = ColumnDefinitions.Parse("*, Auto")
            };

            if (content is string str)
            {
                TextBlock textBlock = new()
                {
                    Text = str,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(textBlock, 0);
                grid.Children.Add(textBlock);
            }
            else if (content is Control control)
            {
                control.HorizontalAlignment = HorizontalAlignment.Left;
                Grid.SetColumn(control, 0);
                grid.Children.Add(control);
            }

            Button helpButton = CreateHelpButton(url, tooltip);
            Grid.SetColumn(helpButton, 1);
            grid.Children.Add(helpButton);

            button.Content = grid;
        }

        public static Button CreateMenuButton(string iconName, string text, Func<Task> onClick)
        {
            StackPanel content = new() { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(0) };
            content.Children.Add(Rendering.ImageSourceLoader.CreateAvaloniaImage(iconName));
            content.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });

            Button button = new()
            {
                Content = content,
                Tag = "Themed",
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Padding = new Thickness(8, 2)
            };

            button.Click += async (_, _) => await onClick();
            return button;
        }

        public static Button CreateMenuButtonWithHelp(string iconName, string text, string helpUrl, string helpTooltip, Func<Task> onClick)
        {
            StackPanel content = new() { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(0) };
            content.Children.Add(Rendering.ImageSourceLoader.CreateAvaloniaImage(iconName));
            content.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });

            Button button = new()
            {
                Tag = "Themed",
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Padding = new Thickness(8, 2)
            };

            button.Click += async (_, _) => await onClick();

            AttachHelpButton(button, content, helpUrl, helpTooltip);
            return button;
        }
    }
}
