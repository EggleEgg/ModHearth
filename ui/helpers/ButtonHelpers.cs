using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;

namespace ModHearth.UI.Helpers
{
    public static class ButtonHelpers
    {
        /// <summary>
        /// Wires a secondary (right) click on <paramref name="button"/> to <paramref name="onRightClick"/>.
        /// The event is marked handled so it does not also trigger the button's normal click action.
        /// </summary>
        public static void AttachRightClickHandler(Button button, Action onRightClick)
            => button.AddHandler(
                InputElement.PointerPressedEvent,
                (_, e) =>
                {
                    if (!e.GetCurrentPoint(button).Properties.IsRightButtonPressed)
                        return;

                    e.Handled = true;
                    onRightClick();
                },
                RoutingStrategies.Tunnel,
                handledEventsToo: true);

        /// <summary>
        /// Creates the compact flyout shell used by the toolbar option menus.
        /// </summary>
        public static Flyout CreateCompactFlyout(Control content, PlacementMode placement = PlacementMode.Bottom)
            => new()
            {
                FlyoutPresenterClasses = { "compact-flyout" },
                Placement = placement,
                Content = content
            };

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
                _ = ShellLauncher.TryOpenAsync(null, url, "Open documentation");
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
