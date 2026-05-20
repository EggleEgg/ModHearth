using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using System;
using System.Collections.Generic;

namespace ModHearth.UI;

public enum SearchFilterMode
{
    Name,
    Id,
    SteamFileId
}

public partial class ModSearchBar : UserControl
{
    private sealed class SearchButtonState
    {
        public IBrush NormalBrush { get; set; } = Brushes.Transparent;
        public IBrush HoverBrush { get; set; } = Brushes.Transparent;
        public IBrush PressedBrush { get; set; } = Brushes.Transparent;
        public bool IsPointerOver { get; set; }
        public bool IsPressed { get; set; }
    }

    public static readonly StyledProperty<string?> WatermarkProperty =
        AvaloniaProperty.Register<ModSearchBar, string?>(nameof(Watermark), "Search");

    public static readonly StyledProperty<bool> HideFilteredProperty =
        AvaloniaProperty.Register<ModSearchBar, bool>(nameof(HideFiltered));
    public static readonly StyledProperty<SearchFilterMode> SearchModeProperty =
        AvaloniaProperty.Register<ModSearchBar, SearchFilterMode>(nameof(SearchMode), SearchFilterMode.Name);

    public event EventHandler? SearchTextChanged;
    public event EventHandler? HideFilteredToggled;
    public event EventHandler? SearchModeChanged;
    private readonly Dictionary<Button, SearchButtonState> searchButtonStates = new();
    private readonly Dictionary<SearchFilterMode, Button> searchModeOptionButtons = new();
    private Flyout? searchModeFlyout;

    public ModSearchBar()
    {
        InitializeComponent();

        SearchBox.TextChanged += (_, _) =>
        {
            TempSearchLog($"TextChanged text='{SearchBox.Text ?? string.Empty}' hideFiltered={HideFiltered} mode={SearchModeToLogLabel(SearchMode)}");
            SearchTextChanged?.Invoke(this, EventArgs.Empty);
        };
        SearchModeButton.Click += (_, _) =>
        {
            EnsureSearchModeFlyout();
            searchModeFlyout?.ShowAt(SearchModeButton);
        };
        ToggleButton.Click += (_, _) =>
        {
            HideFiltered = !HideFiltered;
            TempSearchLog($"ToggleClicked hideFiltered={HideFiltered} mode={SearchModeToLogLabel(SearchMode)}");
            HideFilteredToggled?.Invoke(this, EventArgs.Empty);
        };
        ClearButton.Click += (_, _) =>
        {
            TempSearchLog("ClearClicked");
            SearchBox.Text = string.Empty;
        };

        PropertyChanged += (_, args) =>
        {
            if (args.Property == WatermarkProperty)
                SearchBox.Watermark = Watermark;
            else if (args.Property == HideFilteredProperty)
                UpdateToggleIcon();
            else if (args.Property == SearchModeProperty)
            {
                UpdateSearchModeOptionLabels();
                UpdateSearchModeIcon();
                UpdateSearchModeButtonTooltip();
                TempSearchLog($"SearchModeChanged mode={SearchModeToLogLabel(SearchMode)}");
                SearchModeChanged?.Invoke(this, EventArgs.Empty);
            }
        };
        SearchBox.Watermark = Watermark;
        UpdateToggleIcon();
        UpdateSearchModeIcon();
        UpdateSearchModeButtonTooltip();

        InitializeSearchButtonState(SearchModeButton);
        InitializeSearchButtonState(ToggleButton);
        InitializeSearchButtonState(ClearButton);
    }

    public string Text
    {
        get => SearchBox.Text ?? string.Empty;
        set => SearchBox.Text = value;
    }

    public string? Watermark
    {
        get => GetValue(WatermarkProperty);
        set => SetValue(WatermarkProperty, value);
    }

    public bool HideFiltered
    {
        get => GetValue(HideFilteredProperty);
        set => SetValue(HideFilteredProperty, value);
    }

    public SearchFilterMode SearchMode
    {
        get => GetValue(SearchModeProperty);
        set => SetValue(SearchModeProperty, value);
    }

    public bool ClearSearchSelection()
    {
        bool hadSelection = !string.IsNullOrEmpty(SearchBox.SelectedText);
        int caret = SearchBox.CaretIndex;
        SearchBox.SelectionStart = caret;
        SearchBox.SelectionEnd = caret;
        return hadSelection;
    }

    public void ApplyStyle(Style style)
    {
        if (style == null)
            return;

        IBrush panelBrush = new SolidColorBrush(style.modRefPanelColor.ToAvaloniaColor());
        IBrush searchBorderBrush = new SolidColorBrush(style.searchBorderColor.ToAvaloniaColor());
        IBrush searchButtonBrush = new SolidColorBrush(style.searchButtonColor.ToAvaloniaColor());
        IBrush searchButtonHoverBrush = new SolidColorBrush(style.searchButtonHoverColor.ToAvaloniaColor());
        IBrush searchButtonPressedBrush = new SolidColorBrush(style.searchButtonPressedColor.ToAvaloniaColor());
        IBrush buttonTextBrush = new SolidColorBrush(style.buttonTextColor.ToAvaloniaColor());

        SearchBorder.Background = panelBrush;
        SearchBorder.BorderBrush = searchBorderBrush;
        SearchBox.Background = Brushes.Transparent;
        SearchBox.ClearValue(TextBox.ForegroundProperty);

        Button[] buttons =
        {
            SearchModeButton,
            ToggleButton,
            ClearButton
        };

        foreach (Button button in buttons)
        {
            ApplySearchButtonBrushes(button, searchButtonBrush, searchButtonHoverBrush, searchButtonPressedBrush);
            button.Foreground = buttonTextBrush;
            button.BorderBrush = Brushes.Transparent;
            button.BorderThickness = new Thickness(0);
        }
    }

    private void InitializeSearchButtonState(Button button)
    {
        SearchButtonState state = new SearchButtonState();
        searchButtonStates[button] = state;

        button.PointerEntered += (_, _) =>
        {
            state.IsPointerOver = true;
            UpdateSearchButtonBackground(button, state);
        };

        button.PointerExited += (_, _) =>
        {
            state.IsPointerOver = false;
            state.IsPressed = false;
            UpdateSearchButtonBackground(button, state);
        };

        button.PointerPressed += (_, args) =>
        {
            if (args.GetCurrentPoint(button).Properties.IsLeftButtonPressed)
            {
                state.IsPressed = true;
                UpdateSearchButtonBackground(button, state);
            }
        };

        button.PointerReleased += (_, _) =>
        {
            state.IsPressed = false;
            state.IsPointerOver = button.IsPointerOver;
            UpdateSearchButtonBackground(button, state);
        };

        button.PointerCaptureLost += (_, _) =>
        {
            state.IsPressed = false;
            state.IsPointerOver = button.IsPointerOver;
            UpdateSearchButtonBackground(button, state);
        };
    }

    private void ApplySearchButtonBrushes(
        Button button,
        IBrush normalBrush,
        IBrush hoverBrush,
        IBrush pressedBrush)
    {
        if (!searchButtonStates.TryGetValue(button, out SearchButtonState? state))
            return;

        state.NormalBrush = normalBrush;
        state.HoverBrush = hoverBrush;
        state.PressedBrush = pressedBrush;
        UpdateSearchButtonBackground(button, state);
    }

    private static void UpdateSearchButtonBackground(Button button, SearchButtonState state)
    {
        if (state.IsPressed)
        {
            button.Background = state.PressedBrush;
            return;
        }

        button.Background = state.IsPointerOver ? state.HoverBrush : state.NormalBrush;
    }

    private void UpdateToggleIcon()
    {
        string iconName = HideFiltered ? "hideEyeIcon.svg" : "viewEyeIcon.svg";
        ToggleIcon.Source = ImageSourceLoader.LoadFromAssetUri(iconName)
            ?? ToggleIcon.Source;

        ToolTip.SetTip(ToggleButton, HideFiltered
            ? "Show mismatched mods"
            : "Hide mismatched mods");
    }

    private void EnsureSearchModeFlyout()
    {
        if (searchModeFlyout != null)
            return;

        StackPanel panel = new StackPanel
        {
            Margin = new Thickness(8),
            Spacing = 4,
            MinWidth = 160
        };

        panel.Children.Add(CreateSearchModeOptionButton(SearchFilterMode.Name, "Search by name"));
        panel.Children.Add(CreateSearchModeOptionButton(SearchFilterMode.Id, "Search by mod id"));
        panel.Children.Add(CreateSearchModeOptionButton(SearchFilterMode.SteamFileId, "Search by steam file id"));
        UpdateSearchModeOptionLabels();

        searchModeFlyout = new Flyout
        {
            Placement = PlacementMode.Bottom,
            Content = panel
        };
    }

    private Button CreateSearchModeOptionButton(SearchFilterMode mode, string label)
    {
        Button button = new Button
        {
            Content = label,
            HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Left,
            Padding = new Thickness(8, 4)
        };

        button.Click += (_, _) =>
        {
            SearchMode = mode;
            searchModeFlyout?.Hide();
        };
        searchModeOptionButtons[mode] = button;

        return button;
    }

    private void UpdateSearchModeOptionLabels()
    {
        foreach ((SearchFilterMode mode, Button button) in searchModeOptionButtons)
        {
            string marker = SearchMode == mode ? "[x]" : "[ ]";
            button.Content = $"{marker} {GetSearchModeLabel(mode)}";
        }
    }

    private void UpdateSearchModeButtonTooltip()
    {
        ToolTip.SetTip(SearchModeButton, $"{GetSearchModeLabel(SearchMode)} (click to change)");
    }

    private void UpdateSearchModeIcon()
    {
        string iconName = GetSearchModeIconName(SearchMode);
        SearchModeIcon.Source = ImageSourceLoader.LoadFromAssetUri(iconName)
            ?? SearchModeIcon.Source;
    }

    private static string GetSearchModeIconName(SearchFilterMode mode)
    {
        return mode switch
        {
            SearchFilterMode.Name => "alphabetIcon.svg",
            SearchFilterMode.Id => "idCardIcon.svg",
            SearchFilterMode.SteamFileId => "steamIdIcon.svg",
            _ => "alphabetIcon.svg"
        };
    }

    private static string GetSearchModeLabel(SearchFilterMode mode)
    {
        return mode switch
        {
            SearchFilterMode.Name => "Search by name",
            SearchFilterMode.Id => "Search by mod id",
            SearchFilterMode.SteamFileId => "Search by steam file id",
            _ => "Search by name"
        };
    }

    private static string SearchModeToLogLabel(SearchFilterMode mode)
    {
        return mode switch
        {
            SearchFilterMode.Name => "name",
            SearchFilterMode.Id => "id",
            SearchFilterMode.SteamFileId => "steam_file_id",
            _ => "name"
        };
    }

    private static void TempSearchLog(string message)
    {
        if (!IsDevModeEnabled())
            return;

        Console.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [TEMP][ModSearchBar] {message}");
    }

    private static bool IsDevModeEnabled()
    {
        string? value = Environment.GetEnvironmentVariable("MODHEARTH_DEVMODE");
        return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }
}
