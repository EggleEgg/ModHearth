using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Reactive;
using Avalonia.Layout;
using System.Diagnostics;
using ModHearth.Utilities.Logging;

namespace ModHearth.UI;

public enum SearchFilterMode
{
    Name,
    Regex,
    ModifiedTime,
    Id,
    SteamFileId,
}

public partial class ModSearchBar : UserControl
{
    private readonly Dictionary<SearchFilterMode, TextBlock> searchModeLabels = new();
    private readonly Dictionary<SearchFilterMode, Image> searchModeCheckIcons = new();

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
            SearchTextChanged?.Invoke(this, EventArgs.Empty);
        };
        SearchModeButton.Click += (s, e) =>
        {
            e.Handled = true;
            EnsureSearchModeFlyout();
            searchModeFlyout?.ShowAt(SearchModeButton);
        };
        ToggleButton.Click += (s, e) =>
        {
            e.Handled = true;
            HideFiltered = !HideFiltered;
            HideFilteredToggled?.Invoke(this, EventArgs.Empty);
        };
        ClearButton.Click += (s, e) =>
        {
            e.Handled = true;
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
                SearchLogging.Log($"SearchModeChanged = {GetSearchModeLabel(SearchMode)}");
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

        button.GetObservable(InputElement.IsPointerOverProperty)
              .Subscribe(new AnonymousObserver<bool>(isOver =>
              {
                  state.IsPointerOver = isOver;
                  if (!isOver)
                      state.IsPressed = false;
                  UpdateSearchButtonBackground(button, state);
              }));

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

        button.Click += (_, _) =>
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
        panel.Children.Add(CreateSearchModeOptionButton(SearchFilterMode.Regex, "Search by regex"));
        panel.Children.Add(CreateSearchModeOptionButton(SearchFilterMode.ModifiedTime, "Sort by modified time"));
        panel.Children.Add(CreateSearchModeOptionButton(SearchFilterMode.Id, "Search by mod id"));
        panel.Children.Add(CreateSearchModeOptionButton(SearchFilterMode.SteamFileId, "Search by steam file id"));
        UpdateSearchModeOptionLabels();

        searchModeFlyout = new Flyout
        {
            Placement = PlacementMode.Bottom,
            Content = panel
        };
    }

    private Image CreateCheckIcon(SearchFilterMode mode)
    {
        Image icon = new Image
        {
            Width = 16,
            Height = 16,
            Margin = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
            Stretch = Stretch.Uniform
        };

        searchModeCheckIcons[mode] = icon;
        return icon;
    }
    private Button CreateSearchModeOptionButton(SearchFilterMode mode, string label)
    {
        Button button = new Button
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(8, 4, 6, 4),
        };

        TextBlock text = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center
        };
        searchModeLabels[mode] = text;

        if (mode == SearchFilterMode.Regex)
        {
            ToolTip.SetTip(button, "Search includes mod title and description");
            Grid grid = new Grid();

            Button helpButton = new Button
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

            ToolTip.SetTip(helpButton, "Click here to view regex documentation");

            helpButton.Click += (_, e) =>
            {
                e.Handled = true;

                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://www3.ntu.edu.sg/home/ehchua/programming/howto/Regexe.html",
                    UseShellExecute = true
                });
            };

            Image checkIcon = CreateCheckIcon(mode);
            StackPanel leftContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            leftContent.Children.Add(checkIcon);
            leftContent.Children.Add(text);
            grid.Children.Add(leftContent);
            grid.Children.Add(helpButton);
            button.Content = grid;
        }
        else
        {
            Image checkIcon = CreateCheckIcon(mode);
            StackPanel content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            content.Children.Add(checkIcon);
            content.Children.Add(text);
            button.Content = content;
        }

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
        foreach ((SearchFilterMode mode, TextBlock label) in searchModeLabels)
            label.Text = GetSearchModeLabel(mode);

        foreach ((SearchFilterMode mode, Image icon) in searchModeCheckIcons)
        {
            string iconName = SearchMode == mode ? "checkBoxIcon.svg" : "checkBoxEmptyIcon.svg";
            icon.Source = ImageSourceLoader.LoadFromAssetUri(iconName) ?? icon.Source;
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
            SearchFilterMode.Regex => "regexIcon.svg",
            SearchFilterMode.ModifiedTime => "modifiedClockIcon.svg",
            SearchFilterMode.Id => "idButtonIcon.svg",
            SearchFilterMode.SteamFileId => "steamIdIcon.svg",
            _ => "alphabetIcon.svg"
        };
    }

    private static string GetSearchModeLabel(SearchFilterMode mode)
    {
        return mode switch
        {
            SearchFilterMode.Name => "Search by name",
            SearchFilterMode.Regex => "Search by regex",
            SearchFilterMode.Id => "Search by mod id",
            SearchFilterMode.ModifiedTime => "Sort by modified time",
            SearchFilterMode.SteamFileId => "Search by steam file id",
            _ => "Search by name"
        };
    }


}
