using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Reactive;
using Avalonia.Layout;
using System.Diagnostics;
using ModHearth.Utilities.Logging;
using ModHearth.Models;

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
    private readonly Dictionary<SearchFilterMode, TextBlock> searchModeLabels = [];
    private readonly Dictionary<SearchFilterMode, Image> searchModeIcons = [];

    // Avalonia properties for easy binding and styling in the axamls
    public static readonly StyledProperty<string?> PlaceholderTextProperty =
        AvaloniaProperty.Register<ModSearchBar, string?>(nameof(PlaceholderText), "Search");

    public static readonly StyledProperty<bool> HideFilteredProperty =
        AvaloniaProperty.Register<ModSearchBar, bool>(nameof(HideFiltered), true);
    public static readonly StyledProperty<SearchFilterMode> SearchModeProperty =
        AvaloniaProperty.Register<ModSearchBar, SearchFilterMode>(nameof(SearchMode), SearchFilterMode.Name);

    public static readonly StyledProperty<bool> SortDescendingProperty =
        AvaloniaProperty.Register<ModSearchBar, bool>(nameof(SortDescending), false);

    public static readonly StyledProperty<bool> IsSortingEnabledProperty =
        AvaloniaProperty.Register<ModSearchBar, bool>(nameof(IsSortingEnabled), true);

    public static readonly StyledProperty<bool> IsNameSearchEnabledProperty =
        AvaloniaProperty.Register<ModSearchBar, bool>(nameof(IsNameSearchEnabled), true);

    public static readonly StyledProperty<bool> IsRegexSearchEnabledProperty =
        AvaloniaProperty.Register<ModSearchBar, bool>(nameof(IsRegexSearchEnabled), true);

    // Disables the color filter too
    public static readonly StyledProperty<bool> IsColorSearchEnabledProperty =
        AvaloniaProperty.Register<ModSearchBar, bool>(nameof(IsColorSearchEnabled), true);

    public static readonly StyledProperty<bool> IsModifiedTimeSearchEnabledProperty =
        AvaloniaProperty.Register<ModSearchBar, bool>(nameof(IsModifiedTimeSearchEnabled), true);

    public static readonly StyledProperty<bool> IsIdSearchEnabledProperty =
        AvaloniaProperty.Register<ModSearchBar, bool>(nameof(IsIdSearchEnabled), true);

    public static readonly StyledProperty<bool> IsSteamFileIdSearchEnabledProperty =
        AvaloniaProperty.Register<ModSearchBar, bool>(nameof(IsSteamFileIdSearchEnabled), true);

    public static readonly StyledProperty<string?> ModifiedTimeLabelProperty =
        AvaloniaProperty.Register<ModSearchBar, string?>("ModifiedTimeLabel", "Modified");

    public static readonly StyledProperty<SearchFilterMode> DefaultSearchModeProperty =
        AvaloniaProperty.Register<ModSearchBar, SearchFilterMode>(nameof(DefaultSearchMode), SearchFilterMode.Name);

    public event EventHandler? SearchTextChanged;
    public event EventHandler? HideFilteredToggled;
    public event EventHandler? SearchModeChanged;
    public event EventHandler? SortOrderChanged;

    private readonly Dictionary<SearchFilterMode, Button> searchModeOptionButtons = [];
    private Flyout? searchModeFlyout;
    private int clearClickCount;
    private DateTime lastClearClickTime;

    public ModSearchBar()
    {
        InitializeComponent();

        SearchBox.TextChanged += (_, _) =>
        {
            if (SearchMode != SearchFilterMode.Color)
                SearchTextChanged?.Invoke(this, EventArgs.Empty);
        };
        ColorPicker.SelectionChanged += (_, _) =>
        {
            SearchTextChanged?.Invoke(this, EventArgs.Empty);
        };
        ColorPicker.PickerClicked += (_, _) =>
        {
            ShowColorPickerFlyout();
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
            DateTime now = DateTime.UtcNow;
            if ((now - lastClearClickTime).TotalMilliseconds > 800)
            {
                clearClickCount = 0;
            }
            lastClearClickTime = now;
            clearClickCount++;
            int neededClicks = 2;

            if (clearClickCount >= neededClicks)
            {
                clearClickCount = 0;
                SearchLogging.Log($"ModSearchBar ClearButton clicked {neededClicks} consecutive times - resetting to default search filter");
                SearchMode = IsSearchModeAllowed(DefaultSearchMode) ? DefaultSearchMode : GetFirstAllowedSearchMode();
                ColorPicker.ClearSelection();
                SearchBox.Text = string.Empty;
            }
            else
            {
                switch (SearchMode)
                {
                    case SearchFilterMode.Color when IsColorSearchEnabled:
                        ColorPicker.ClearSelection();
                        break;
                    default:
                        SearchBox.Text = string.Empty;
                        break;
                }
            }
        };

        PropertyChanged += (_, args) =>
        {
            if (args.Property == PlaceholderTextProperty)
            {
                PlaceholderTextBlock.Text = PlaceholderText;
            }
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
            else if (args.Property == IsSortingEnabledProperty ||
                     args.Property == IsNameSearchEnabledProperty ||
                     args.Property == IsRegexSearchEnabledProperty ||
                     args.Property == IsColorSearchEnabledProperty ||
                     args.Property == IsModifiedTimeSearchEnabledProperty ||
                     args.Property == IsIdSearchEnabledProperty ||
                     args.Property == IsSteamFileIdSearchEnabledProperty)
            {
                if (!IsSearchModeAllowed(SearchMode))
                {
                    SearchMode = GetFirstAllowedSearchMode();
                }
                UpdateSearchModeIcon();
                UpdateSearchModeButtonTooltip();
                searchModeFlyout = null;
            }
            else if (args.Property == ModifiedTimeLabelProperty)
            {
                UpdateSearchModeOptionLabels();
                UpdateSearchModeIcon();
                UpdateSearchModeButtonTooltip();
            }
        };
        PlaceholderTextBlock.Text = PlaceholderText;
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

        SearchButtonBehavior.GetOrCreate(SearchModeButton);
        SearchButtonBehavior.GetOrCreate(ToggleButton);
        SearchButtonBehavior.GetOrCreate(ClearButton);
    }

    public void SetAvailableColors(IEnumerable<ModColor> colors)
    {
        // Snapshot the current selection
        var currentSelection = ColorPicker.SelectedColors.Select(c => c.ModColor).ToList();

        ColorPicker.AvailableColors.Clear();
        ColorPicker.SelectedColors.Clear();

        foreach (var colorEnum in colors.Where(c => c != ModColor.None).Distinct())
        {
            var info = new ModColorInfo
            {
                ModColor = colorEnum,
                Name = ModColorMap.ColorNames.TryGetValue(colorEnum, out var name) ? name : colorEnum.ToString(),
                Color = ModColorMap.GetColor(colorEnum),
                IsSelected = currentSelection.Contains(colorEnum)
            };
            ColorPicker.AvailableColors.Add(info);
            if (info.IsSelected)
                ColorPicker.SelectedColors.Add(info);
        }
    }

    public string Text
    {
        get => SearchMode == SearchFilterMode.Color && IsColorSearchEnabled
            ? string.Join(",", ColorPicker.SelectedColors.Select(c => c.ModColor.ToString()))
            : SearchBox.Text ?? string.Empty;
        set
        {
            if (SearchMode != SearchFilterMode.Color || !IsColorSearchEnabled)
                SearchBox.Text = value;
            else
            {
                ColorPicker.ClearSelection();
                var colorStrings = (value ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToList();
                foreach (var s in colorStrings)
                {
                    if (Enum.TryParse(s, out ModColor modColor) && modColor != ModColor.None)
                    {
                        var colorInfo = ColorPicker.AvailableColors.FirstOrDefault(c => c.ModColor == modColor);
                        if (colorInfo != null)
                        {
                            colorInfo.IsSelected = true;
                            ColorPicker.SelectedColors.Add(colorInfo);
                        }
                    }
                }
            }
        }
    }

    public string? PlaceholderText
    {
        get => GetValue(PlaceholderTextProperty);
        set => SetValue(PlaceholderTextProperty, value);
    }

    public void FocusSearchBox()
    {
        if (SearchMode == SearchFilterMode.Color && IsColorSearchEnabled)
            return;

        _ = SearchBox.Focus();
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

    public bool IsNameSearchEnabled
    {
        get => GetValue(IsNameSearchEnabledProperty);
        set => SetValue(IsNameSearchEnabledProperty, value);
    }

    public bool IsRegexSearchEnabled
    {
        get => GetValue(IsRegexSearchEnabledProperty);
        set => SetValue(IsRegexSearchEnabledProperty, value);
    }

    public bool IsColorSearchEnabled
    {
        get => GetValue(IsColorSearchEnabledProperty);
        set => SetValue(IsColorSearchEnabledProperty, value);
    }

    public bool IsModifiedTimeSearchEnabled
    {
        get => GetValue(IsModifiedTimeSearchEnabledProperty);
        set => SetValue(IsModifiedTimeSearchEnabledProperty, value);
    }

    public bool IsIdSearchEnabled
    {
        get => GetValue(IsIdSearchEnabledProperty);
        set => SetValue(IsIdSearchEnabledProperty, value);
    }

    public bool IsSteamFileIdSearchEnabled
    {
        get => GetValue(IsSteamFileIdSearchEnabledProperty);
        set => SetValue(IsSteamFileIdSearchEnabledProperty, value);
    }

    public string? ModifiedTimeLabel
    {
        get => GetValue(ModifiedTimeLabelProperty);
        set => SetValue(ModifiedTimeLabelProperty, value);
    }

    private SearchFilterMode? cachedDefaultSearchMode;

    public SearchFilterMode DefaultSearchMode
    {
        get
        {
            if (IsSet(DefaultSearchModeProperty))
                return GetValue(DefaultSearchModeProperty);
            cachedDefaultSearchMode ??= SearchMode;
            return cachedDefaultSearchMode.Value;
        }
        set => SetValue(DefaultSearchModeProperty, value);
    }

    private bool IsSearchModeAllowed(SearchFilterMode mode)
    {
        return mode switch
        {
            SearchFilterMode.Name => IsNameSearchEnabled,
            SearchFilterMode.Regex => IsRegexSearchEnabled,
            SearchFilterMode.Color => IsColorSearchEnabled,
            SearchFilterMode.ModifiedTime => IsSortingEnabled && IsModifiedTimeSearchEnabled,
            SearchFilterMode.Id => IsIdSearchEnabled,
            SearchFilterMode.SteamFileId => IsSteamFileIdSearchEnabled,
            _ => true
        };
    }

    private SearchFilterMode GetFirstAllowedSearchMode()
    {
        if (IsNameSearchEnabled) return SearchFilterMode.Name;
        if (IsRegexSearchEnabled) return SearchFilterMode.Regex;
        if (IsColorSearchEnabled) return SearchFilterMode.Color;
        if (IsSortingEnabled && IsModifiedTimeSearchEnabled) return SearchFilterMode.ModifiedTime;
        if (IsIdSearchEnabled) return SearchFilterMode.Id;
        if (IsSteamFileIdSearchEnabled) return SearchFilterMode.SteamFileId;
        return SearchFilterMode.Name;
    }

    public SearchFilterMode SearchMode
    {
        get => GetValue(SearchModeProperty);
        set => SetValue(SearchModeProperty, value);
    }

    public bool ClearSearchSelection()
    {
        switch (SearchMode)
        {
            case SearchFilterMode.Color when IsColorSearchEnabled:
                {
                    bool hadSelection = ColorPicker.SelectedColors.Any();
                    ColorPicker.ClearSelection();
                    return hadSelection;
                }

            default:
                {
                    bool hadSelection = !string.IsNullOrEmpty(SearchBox.SelectedText);
                    int caret = SearchBox.CaretIndex;
                    SearchBox.SelectionStart = caret;
                    SearchBox.SelectionEnd = caret;
                    return hadSelection;
                }
        }
    }

    public void ApplyStyle(Style style)
    {
        if (style == null)
            return;

        IBrush panelBrush = BrushCache.GetBrush(style.panelColor.ToAvaloniaColor());
        IBrush searchBorderBrush = BrushCache.GetBrush(style.searchBorderColor.ToAvaloniaColor());
        IBrush searchButtonBrush = BrushCache.GetBrush(style.searchButtonColor.ToAvaloniaColor());
        IBrush searchButtonHoverBrush = BrushCache.GetBrush(style.searchButtonHoverColor.ToAvaloniaColor());
        IBrush searchButtonPressedBrush = BrushCache.GetBrush(style.searchButtonPressedColor.ToAvaloniaColor());
        IBrush buttonTextBrush = BrushCache.GetBrush(style.buttonTextColor.ToAvaloniaColor());

        SearchBorder.Background = panelBrush;
        SearchBorder.BorderBrush = searchBorderBrush;
        SearchBox.Background = Brushes.Transparent;
        SearchBox.ClearValue(ForegroundProperty);

        Button[] buttons =
        [
            SearchModeButton,
            ToggleButton,
            ClearButton
        ];

        foreach (Button button in buttons)
        {
            SearchButtonBehavior.GetOrCreate(button).ApplyBrushes(searchButtonBrush, searchButtonHoverBrush, searchButtonPressedBrush);
            button.Foreground = buttonTextBrush;
            button.BorderBrush = Brushes.Transparent;
        }

        SearchModeTextBlock.Foreground = buttonTextBrush;

        ColorPicker.ApplyStyle(searchButtonBrush, searchButtonHoverBrush, searchButtonPressedBrush, buttonTextBrush);
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

        StackPanel panel = new()
        {
            Margin = new Thickness(0),
            Spacing = 4,
            MinWidth = 160
        };

        if (IsNameSearchEnabled)
            panel.Children.Add(CreateSearchModeOptionButton(SearchFilterMode.Name));
        if (IsRegexSearchEnabled)
            panel.Children.Add(CreateSearchModeOptionButton(SearchFilterMode.Regex));
        if (IsColorSearchEnabled)
            panel.Children.Add(CreateSearchModeOptionButton(SearchFilterMode.Color));

        if (IsSortingEnabled && IsModifiedTimeSearchEnabled)
            panel.Children.Add(CreateSearchModeOptionButton(SearchFilterMode.ModifiedTime));

        if (IsIdSearchEnabled)
            panel.Children.Add(CreateSearchModeOptionButton(SearchFilterMode.Id));
        if (IsSteamFileIdSearchEnabled)
            panel.Children.Add(CreateSearchModeOptionButton(SearchFilterMode.SteamFileId));
        UpdateSearchModeOptionLabels();

        searchModeFlyout = new Flyout
        {
            Placement = PlacementMode.Bottom,
            Content = panel
        };
    }

    private Image CreateSearchModeIcon(SearchFilterMode mode)
    {
        Image icon = new()
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
    private Button CreateSearchModeOptionButton(SearchFilterMode mode)
    {
        Button button = new()
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(6, 4, 6, 4),
        };

        TextBlock text = new()
        {
            VerticalAlignment = VerticalAlignment.Center
        };
        searchModeLabels[mode] = text;

        Thickness textRightSpacing = new(0, 0, 15, 0);

        switch (mode)
        {
            case SearchFilterMode.Regex:
                {
                    ToolTip.SetTip(button, "Search includes mod title and description");

                    Grid grid = new()
                    {
                        ColumnDefinitions = ColumnDefinitions.Parse("Auto, *, Auto")
                    };

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

                    ToolTip.SetTip(helpButton, "Click here to view regex documentation");

                    helpButton.Click += (_, e) =>
                    {
                        e.Handled = true;

                        using Process? process = Process.Start(new ProcessStartInfo
                        {
                            FileName = "https://regex101.com/",
                            UseShellExecute = true
                        });
                    };

                    Image optionIcon = CreateSearchModeIcon(mode);
                    StackPanel leftContent = new() { Orientation = Orientation.Horizontal, Spacing = 6 };
                    leftContent.Children.Add(optionIcon);
                    leftContent.Children.Add(text);

                    leftContent.Margin = textRightSpacing;

                    Grid.SetColumn(leftContent, 0);
                    Grid.SetColumn(helpButton, 2);

                    grid.Children.Add(leftContent);
                    grid.Children.Add(helpButton);
                    button.Content = grid;
                    break;
                }

            default:
                {
                    Image optionIcon = CreateSearchModeIcon(mode);
                    StackPanel content = new() { Orientation = Orientation.Horizontal, Spacing = 6 };
                    content.Children.Add(optionIcon);
                    content.Children.Add(text);

                    content.Margin = textRightSpacing;
                    button.Content = content;
                    break;
                }
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

    private void UpdateSearchModeIcon()
    {
        string iconName = GetSearchModeIconName(SearchMode);
        SearchModeIcon.Source = ImageSourceLoader.LoadFromAssetUri(iconName)
            ?? SearchModeIcon.Source;
        SearchModeIcon.Margin = IsSortingEnabled ? new Thickness(0, 0, 12, 0) : new Thickness(0, 0, 6, 0);

        string directionIconName = SortDescending ? "sortDownIcon.svg" : "sortUpIcon.svg";
        SortDirectionIcon.Source = ImageSourceLoader.LoadFromAssetUri(directionIconName)
            ?? SortDirectionIcon.Source;

        SearchModeTextBlock.Text = GetSearchModeLabel(SearchMode, includePrefix: false);
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

    private string GetSearchModeLabel(SearchFilterMode mode, bool includePrefix = true)
    {
        if (!includePrefix)
        {
            return mode switch
            {
                SearchFilterMode.Name => "Name",
                SearchFilterMode.Regex => "Regex",
                SearchFilterMode.Color => "Color",
                SearchFilterMode.ModifiedTime => ModifiedTimeLabel ?? "Modified",
                SearchFilterMode.Id => "Mod ID",
                SearchFilterMode.SteamFileId => "Steam ID",
                _ => "Name"
            };
        }

        return mode switch
        {
            SearchFilterMode.Name => "Search by name",
            SearchFilterMode.Regex => "Search by regex",
            SearchFilterMode.Color => "Search by color",
            SearchFilterMode.ModifiedTime => ModifiedTimeLabel == "Time" ? "Sort by time" : $"Sort by {(ModifiedTimeLabel ?? "Modified").ToLower()} time",
            SearchFilterMode.Id => "Search by mod id",
            SearchFilterMode.SteamFileId => "Search by steam file id",
            _ => "Search by name"
        };
    }


    public string GetStringState()
    {
        return $"{Text}|{HideFiltered}|{SearchMode}|{SortDescending}";
    }

    public void SetStringState(string state)
    {
        string[] parts = state.Split('|');
        if (parts.Length == 4)
        {
            HideFiltered = bool.Parse(parts[1]);
            SortDescending = bool.Parse(parts[3]);
            SearchMode = Enum.Parse<SearchFilterMode>(parts[2]);
            Text = parts[0];
        }
    }

    private void ShowColorPickerFlyout()
    {
        var availableColors = ColorPicker.AvailableColors.Select(c => c.ModColor).ToList();

        var grid = new UniformGrid
        {
            Columns = (int)Math.Sqrt(availableColors.Count + 1),
        };

        void RefreshGrid()
        {
            grid.Children.Clear();

            // Add "Clear" option
            grid.Children.Add(ColorSwatchHelper.CreateColorSwatchButton(new ModColorInfo
            {
                ModColor = ModColor.None,
                Name = "Clear all filters",
                Color = Colors.Transparent,
                IsSelected = false
            }, _ =>
            {
                Text = string.Empty;
                RefreshGrid();
            }));

            var currentSelection = Text.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .ToList();

            foreach (var color in availableColors)
            {
                var info = new ModColorInfo
                {
                    ModColor = color,
                    Name = ModColorMap.ColorNames.TryGetValue(color, out var name) ? name : color.ToString(),
                    Color = ModColorMap.GetColor(color),
                    IsSelected = currentSelection.Contains(color.ToString())
                };
                grid.Children.Add(ColorSwatchHelper.CreateColorSwatchButton(info, c =>
                {
                    ToggleColor(c);
                    RefreshGrid();
                }));
            }
        }

        void ToggleColor(ModColor color)
        {
            var text = Text;
            var selectedColors = text.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .ToList();

            var colorStr = color.ToString();
            if (selectedColors.Contains(colorStr))
                _ = selectedColors.Remove(colorStr);
            else
                selectedColors.Add(colorStr);

            Text = string.Join(",", selectedColors);
        }

        RefreshGrid();

        var flyout = new Flyout
        {
            FlyoutPresenterClasses = { "compact-flyout" },
            Content = new Border
            {
                Padding = new Thickness(0),
                Child = grid
            }
        };

        flyout.ShowAt(this);
    }
}
