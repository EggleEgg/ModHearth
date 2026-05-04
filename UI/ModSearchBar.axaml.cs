using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using System;
using System.Collections.Generic;

namespace ModHearth.UI;

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

    public event EventHandler? SearchTextChanged;
    public event EventHandler? HideFilteredToggled;
    private readonly Dictionary<Button, SearchButtonState> searchButtonStates = new();

    public ModSearchBar()
    {
        InitializeComponent();

        SearchBox.TextChanged += (_, _) =>
        {
            TempSearchLog($"TextChanged text='{SearchBox.Text ?? string.Empty}' hideFiltered={HideFiltered}");
            SearchTextChanged?.Invoke(this, EventArgs.Empty);
        };
        ToggleButton.Click += (_, _) =>
        {
            HideFiltered = !HideFiltered;
            TempSearchLog($"ToggleClicked hideFiltered={HideFiltered}");
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
        };
        SearchBox.Watermark = Watermark;
        UpdateToggleIcon();

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
