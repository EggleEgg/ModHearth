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
    Color,
    ModifiedTime,
    Id,
    SteamFileId,
}

public partial class ModSearchBar : UserControl
{
    private readonly Dictionary<SearchFilterMode, TextBlock> searchModeLabels = new();
    private readonly Dictionary<SearchFilterMode, Image> searchModeIcons = new();

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

    public static readonly StyledProperty<bool> SortDescendingProperty =
        AvaloniaProperty.Register<ModSearchBar, bool>(nameof(SortDescending), true);

    public static readonly StyledProperty<bool> IsSortingEnabledProperty =
        AvaloniaProperty.Register<ModSearchBar, bool>(nameof(IsSortingEnabled), true);

    public event EventHandler? SearchTextChanged;
    public event EventHandler? HideFilteredToggled;
    public event EventHandler? SearchModeChanged;
    public event EventHandler? SortOrderChanged;
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
            else if (args.Property == SortDescendingProperty)
            {
                UpdateSearchModeIcon();
                UpdateSearchModeButtonTooltip();
                SortOrderChanged?.Invoke(this, EventArgs.Empty);
            }
            else if (args.Property == IsSortingEnabledProperty)
            {
                UpdateSearchModeIcon();
                UpdateSearchModeButtonTooltip();
                searchModeFlyout = null;
            }
        };
        SearchBox.Watermark = Watermark;
        UpdateToggleIcon();
        UpdateSearchModeIcon();
        UpdateSearchModeButtonTooltip();

        SearchModeButton.PointerPressed += (s, e) =>
        {
            if (IsSortingEnabled && e.GetCurrentPoint(SearchModeButton).Properties.IsRightButtonPressed)
            {
                e.Handled = true;
                SortDescending = !SortDescending;
                UpdateSearchModeIcon();
                UpdateSearchModeButtonTooltip();
                SortOrderChanged?.Invoke(this, EventArgs.Empty);
            }
        };

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

    public bool SortDescending
    {
        get => GetValue(SortDescendingProperty);
        set => SetValue(SortDescendingProperty, value);
    }

    public bool IsSortingEnabled
    {
        get => GetValue(IsSortingEnabledProperty);
        set => SetValue(IsSortingEnabledProperty, value);
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

        IBrush panelBrush = BrushCache.GetBrush(style.modRefPanelColor.ToAvaloniaColor());
        IBrush searchBorderBrush = BrushCache.GetBrush(style.searchBorderColor.ToAvaloniaColor());
        IBrush searchButtonBrush = BrushCache.GetBrush(style.searchButtonColor.ToAvaloniaColor());
        IBrush searchButtonHoverBrush = BrushCache.GetBrush(style.searchButtonHoverColor.ToAvaloniaColor());
        IBrush searchButtonPressedBrush = BrushCache.GetBrush(style.searchButtonPressedColor.ToAvaloniaColor());
        IBrush buttonTextBrush = BrushCache.GetBrush(style.buttonTextColor.ToAvaloniaColor());

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
        IBrush targetBrush;
        if (state.IsPressed)
            targetBrush = state.PressedBrush;
        else if (state.IsPointerOver)
            targetBrush = state.HoverBrush;
        else
            targetBrush = state.NormalBrush;

        button.Background = targetBrush;

        if (button.Content is Panel contentPanel)
            foreach (Control child in contentPanel.Children)
                if (child is Border contentBorder)
                    contentBorder.Background = targetBrush;
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
            Margin = new Thickness(0),
            Spacing = 4,
            MinWidth = 160
        };

        panel.Children.Add(CreateSearchModeOptionButton(SearchFilterMode.Name, "Search by name"));
        panel.Children.Add(CreateSearchModeOptionButton(SearchFilterMode.Regex, "Search by regex"));
        panel.Children.Add(CreateSearchModeOptionButton(SearchFilterMode.Color, "Search by color"));
        if (IsSortingEnabled)
        {
            panel.Children.Add(CreateSearchModeOptionButton(SearchFilterMode.ModifiedTime, "Sort by modified time"));
        }
        panel.Children.Add(CreateSearchModeOptionButton(SearchFilterMode.Id, "Search by mod id"));
        panel.Children.Add(CreateSearchModeOptionButton(SearchFilterMode.SteamFileId, "Search by steam file id"));
        UpdateSearchModeOptionLabels();

        searchModeFlyout = new Flyout
        {
            Placement = PlacementMode.Bottom,
            Content = panel
        };
    }

    private Image CreateSearchModeIcon(SearchFilterMode mode)
    {
        Image icon = new Image
        {
            Width = 16,
            Height = 16,
            Margin = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
            Stretch = Stretch.Uniform
        };

        searchModeIcons[mode] = icon;
        return icon;
    }
    private Button CreateSearchModeOptionButton(SearchFilterMode mode, string label)
    {
        Button button = new Button
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(4, 4, 6, 4),
        };

        TextBlock text = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center
        };
        searchModeLabels[mode] = text;

        Thickness textRightSpacing = new Thickness(0, 0, 15, 0);

        if (mode == SearchFilterMode.Regex)
        {
            ToolTip.SetTip(button, "Search includes mod title and description");

            Grid grid = new Grid();
            grid.ColumnDefinitions = ColumnDefinitions.Parse("Auto, *, Auto");

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

            Image optionIcon = CreateSearchModeIcon(mode);
            StackPanel leftContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            leftContent.Children.Add(optionIcon);
            leftContent.Children.Add(text);

            leftContent.Margin = textRightSpacing;

            Grid.SetColumn(leftContent, 0);
            Grid.SetColumn(helpButton, 2);

            grid.Children.Add(leftContent);
            grid.Children.Add(helpButton);
            button.Content = grid;
        }
        else
        {
            Image optionIcon = CreateSearchModeIcon(mode);
            StackPanel content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            content.Children.Add(optionIcon);
            content.Children.Add(text);

            content.Margin = textRightSpacing;
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
        {
            label.Text = GetSearchModeLabel(mode);
            label.FontWeight = SearchMode == mode ? FontWeight.Bold : FontWeight.Normal;
        }

        foreach ((SearchFilterMode mode, Image icon) in searchModeIcons)
        {
            string iconName = GetSearchModeIconName(mode);
            icon.Source = ImageSourceLoader.LoadFromAssetUri(iconName) ?? icon.Source;
            icon.Opacity = SearchMode == mode ? 1.0 : 0.6;
        }
    }

    private void UpdateSearchModeButtonTooltip()
    {
        string sortOrder = SortDescending ? "descending" : "ascending";
        ToolTip.SetTip(SearchModeButton, IsSortingEnabled
            ? $"{GetSearchModeLabel(SearchMode)} ({sortOrder})\nRight click to sort by asc/desc order"
            : GetSearchModeLabel(SearchMode));
    }

    //make sure to change these if the asociated axaml template ever changes
    private void UpdateSearchModeIcon()
    {
        string iconName = GetSearchModeIconName(SearchMode);
        SearchModeIcon.Source = ImageSourceLoader.LoadFromAssetUri(iconName)
            ?? SearchModeIcon.Source;

        string directionIconName = SortDescending ? "sortDownIcon.svg" : "sortUpIcon.svg";
        SortDirectionIcon.Source = ImageSourceLoader.LoadFromAssetUri(directionIconName)
            ?? SortDirectionIcon.Source;
        SortDirectionIcon.IsVisible = IsSortingEnabled;
        SearchModeButton.Width = IsSortingEnabled ? 30 : 22;

        if (!IsSortingEnabled)
        {
            SearchModeIcon.Width = double.NaN;
            SearchModeIcon.Height = double.NaN;
            SearchModeIcon.HorizontalAlignment = HorizontalAlignment.Center;
        }

    }

    private static string GetSearchModeIconName(SearchFilterMode mode)
    {
        return mode switch
        {
            SearchFilterMode.Name => "alphabetIcon.svg",
            SearchFilterMode.Regex => "regexIcon.svg",
            SearchFilterMode.Color => "paintBrushIcon.svg",
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
            SearchFilterMode.Color => "Search by color",
            SearchFilterMode.ModifiedTime => "Sort by modified time",
            SearchFilterMode.Id => "Search by mod id",
            SearchFilterMode.SteamFileId => "Search by steam file id",
            _ => "Search by name"
        };
    }


}
