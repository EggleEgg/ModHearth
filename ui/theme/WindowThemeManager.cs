using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;

namespace ModHearth.UI;

/// <summary>
/// Applies default style colours for any window
/// </summary>
public static class WindowThemeManager
{
    public static readonly AttachedProperty<bool> IsThemedProperty =
        AvaloniaProperty.RegisterAttached<Button, Button, bool>("IsThemed", false);

    public static bool GetIsThemed(Button element) => element.GetValue(IsThemedProperty);
    public static void SetIsThemed(Button element, bool value) => element.SetValue(IsThemedProperty, value);

    private static readonly List<WeakReference<Window>> registered = new();

    [ThreadStatic]
    private static bool isApplying;

    // Cache brushes to reduce memory usage
    private readonly record struct StyleBrushes(
        IBrush Form,
        IBrush Text,
        IBrush Panel,
        IBrush PanelClear,
        IBrush StrongPanel,
        IBrush Button,
        IBrush ButtonText,
        IBrush ButtonOutline,
        IBrush BorderPanel,
        IBrush DataGrid,
        IBrush ModRefHighlight,
        IBrush ModRefHighlightDark,
        IBrush InputText
    );

    public static void Register(Window window)
    {
        if (window == null)
            return;

        Cleanup();
        registered.Add(new WeakReference<Window>(window));

        window.Opened += OnWindowOpened;
        window.Closed += OnWindowClosed;

        if (Style.instance != null)
        {
            ApplyToWindow(window, Style.instance);
        }
    }

    public static void ApplyToOpenWindows()
    {
        ApplyToOpenWindows(Style.instance);
    }

    public static void ApplyToOpenWindows(Style? style)
    {
        if (style == null)
            return;

        Cleanup();
        for (int i = 0; i < registered.Count; i++)
        {
            if (registered[i].TryGetTarget(out Window? window))
                ApplyToWindow(window, style);
        }
    }

    private static void OnWindowOpened(object? sender, EventArgs e)
    {
        if (sender is not Window window)
            return;

        Style? style = Style.instance;
        if (style == null)
            return;

        // Ensure the visual tree is fully loaded and built before styling
        Avalonia.Threading.Dispatcher.UIThread.Post(() => ApplyToWindow(window, style), Avalonia.Threading.DispatcherPriority.Loaded);
    }

    private static void OnWindowClosed(object? sender, EventArgs e)
    {
        Cleanup();
    }

    private static void Cleanup()
    {
        registered.RemoveAll(weak => !weak.TryGetTarget(out _));
    }

    public static void ApplyToWindow(Window window, Style style)
    {
        if (window == null || style == null || isApplying)
            return;

        Style.instance = style;
        isApplying = true;
        try
        {
            ApplyToVisual(window, style);
        }
        finally
        {
            isApplying = false;
        }
    }

    public static void ApplyToVisual(Visual visual, Style style)
    {
        if (visual == null || style == null)
            return;

        Style.instance = style;

        bool isDark = IsDark(style.formColor);
        StyleBrushes brushes = new(
            Form: BrushCache.GetBrush(style.formColor.ToAvaloniaColor()),
            Text: BrushCache.GetBrush(style.textColor.ToAvaloniaColor()),
            Panel: BrushCache.GetBrush(style.modRefPanelColor.ToAvaloniaColor()),
            PanelClear: BrushCache.GetBrush(style.modRefPanelColorClear.ToAvaloniaColor()),
            StrongPanel: BrushCache.GetBrush(style.strongPanelColor.ToAvaloniaColor()),
            Button: BrushCache.GetBrush(style.buttonColor.ToAvaloniaColor()),
            ButtonText: BrushCache.GetBrush(style.buttonTextColor.ToAvaloniaColor()),
            ButtonOutline: BrushCache.GetBrush(style.buttonOutlineColor.ToAvaloniaColor()),
            BorderPanel: BrushCache.GetBrush(style.backgroundColor.ToAvaloniaColor()),
            DataGrid: BrushCache.GetBrush(style.backgroundColor.ToAvaloniaColor()),
            ModRefHighlight: BrushCache.GetBrush(style.modRefHighlightColor.ToAvaloniaColor()),
            ModRefHighlightDark: BrushCache.GetBrush(style.modRefHighlightDarkColor.ToAvaloniaColor()),
            InputText: isDark ? Brushes.White : Brushes.Black
        );

        if (visual is Window window)
        {
            window.Background = brushes.Form;
            ThemeVariant? ownerVariant = window.Owner?.RequestedThemeVariant;
            window.RequestedThemeVariant = ownerVariant ?? (isDark ? ThemeVariant.Dark : ThemeVariant.Light);
        }

        // Set global DynamicResource values once per apply pass
        Application? app = Application.Current;
        if (app != null)
        {
            app.Resources["BorderPanelBrush"] = brushes.BorderPanel;
            app.Resources["FormBackgroundBrush"] = brushes.Form;
            app.Resources["MainTextBrush"] = brushes.Text;
            app.Resources["PanelBackgroundBrush"] = brushes.Panel;
            app.Resources["StrongPanelBackgroundBrush"] = brushes.StrongPanel;
            app.Resources["ButtonBackgroundBrush"] = brushes.Button;
            app.Resources["ButtonForegroundBrush"] = brushes.ButtonText;
            app.Resources["ButtonBorderBrush"] = brushes.ButtonOutline;
            app.Resources["ModRefHighlightBrush"] = brushes.ModRefHighlight;
            app.Resources["ModRefHighlightDarkBrush"] = brushes.ModRefHighlightDark;
            app.Resources["ModRefPanelClearBrush"] = brushes.PanelClear;
            app.Resources["ButtonSelectionBrush"] = BrushCache.GetBrush(style.buttonSelectionColor.ToAvaloniaColor());
            app.Resources["WorkshopDockPreviewBackgroundBrush"] = BrushCache.GetBrush(Color.FromArgb(120, (byte)style.buttonSelectionColor.R, (byte)style.buttonSelectionColor.G, (byte)style.buttonSelectionColor.B));
        }

        // Single top-down $O(N)$ pass through the visual tree
        ApplyToVisualRecursive(visual, style, brushes);

        if (visual is IStyleAwareWindow styleAware)
            styleAware.ApplyCustomStyle(style);
    }

    private static void ApplyToVisualRecursive(
        Visual visual,
        Style style,
        in StyleBrushes brushes,
        bool inSearchBar = false,
        bool inDockHeader = false,
        bool inButtonOrCombo = false,
        bool inDockPanel = false,
        bool inDataGrid = false)
    {
        if (visual == null)
            return;

        if (visual is Control control && control.Tag is string ignoreTag && string.Equals(ignoreTag, "IgnoreTheme", StringComparison.OrdinalIgnoreCase))
            return;

        // 1. ModSearchBar styling context
        if (visual is ModSearchBar searchBar)
        {
            searchBar.ApplyStyle(style);
            inSearchBar = true;
        }
        else if (inSearchBar)
        {
            // ModSearchBar (and nested ModColorPicker) manage their own internal styling
            return;
        }

        // 2. Notification container custom styling
        if (visual is StackPanel spContainer && spContainer.Name == "notificationContainer")
        {
            StyleNotificationContainer(spContainer, brushes);
            return; // Skip default child processing
        }

        // 3. Dock chrome / host context calculation
        string typeName = visual.GetType().Name;
        if (visual is Control ctrl && (ctrl.Name == "PART_ContentPresenter" || typeName == "DeferredContentPresenter"))
        {
            inDockHeader = false; // Reset when entering dock body content host
        }
        else if (typeName is "ToolChromeControl" or "ToolDockControl" or "DockControl" || (visual is Control grip && grip.Name == "PART_Grip"))
        {
            inDockHeader = true; // Set when inside dock title/header
        }

        // 4. Element-specific styling
        if (visual is TextBlock textBlock)
        {
            if (!inButtonOrCombo && !inDockHeader)
            {
                if (inDataGrid)
                {
                    ModUpdateLogItemViewModel? vm = textBlock.DataContext as ModUpdateLogItemViewModel;
                    Visual? current = textBlock;
                    while (current != null && vm == null)
                    {
                        vm = current.DataContext as ModUpdateLogItemViewModel;
                        current = current.GetVisualParent();
                    }

                    bool hasSpecialColor = vm != null && (
                        vm.Entry.ChangeType == ModUpdateChangeType.Deleted ||
                        vm.Entry.ChangeType == ModUpdateChangeType.Updated ||
                        vm.Entry.ChangeType == ModUpdateChangeType.Added ||
                        vm.IsActive
                    );

                    if (!hasSpecialColor)
                    {
                        textBlock.Foreground = brushes.Text;
                    }
                }
                else
                {
                    textBlock.Foreground = brushes.Text;
                }
            }
        }
        else if (visual is TextBox textBox)
        {
            textBox.Background = brushes.Panel;
            textBox.Foreground = brushes.InputText;
        }
        else if (visual is ComboBox comboBox)
        {
            comboBox.Background = brushes.Panel;
            inButtonOrCombo = true;
        }
        else if (visual is ListBox listBox)
        {
            listBox.Background = brushes.Panel;
        }
        else if (visual is Button button)
        {
            // Used for important buttons
            if (button.GetValue(IsThemedProperty) || (button.Tag is string tag && string.Equals(tag, "Themed", StringComparison.OrdinalIgnoreCase)))
            {
                button.Background = brushes.Button;
                button.Foreground = brushes.ButtonText;
                button.BorderBrush = brushes.ButtonOutline;
                button.BorderThickness = new Thickness(1);
            }
            inButtonOrCombo = true;
        }
        else if (visual is DataGrid dataGrid)
        {
            dataGrid.Background = brushes.DataGrid;
            inDataGrid = true;
        }
        else if (typeName is "DataGridCell" || typeName.Contains("DataGrid"))
        {
            inDataGrid = true;
        }
        else if (visual is DockPanel)
        {
            inDockPanel = true;
        }

        // 5. Recurse down to children without heap allocations or ancestor searches
        foreach (Visual child in visual.GetVisualChildren())
        {
            ApplyToVisualRecursive(child, style, brushes, inSearchBar, inDockHeader, inButtonOrCombo, inDockPanel, inDataGrid);
        }
    }

    private static void StyleNotificationContainer(StackPanel container, in StyleBrushes brushes)
    {
        foreach (var child in container.Children)
        {
            if (child is Border b)
            {
                b.Background = brushes.PanelClear;
                b.BorderBrush = brushes.ButtonOutline;
                if (b.Child is StackPanel innerSp)
                {
                    foreach (var innerChild in innerSp.Children)
                    {
                        if (innerChild is TextBlock tb)
                            tb.Foreground = brushes.Text;
                    }
                }
            }
        }
    }

    private static bool IsDark(SimpleColor color)
    {
        double luminance = (0.299 * color.R) + (0.587 * color.G) + (0.114 * color.B);
        return luminance < 128;
    }
}