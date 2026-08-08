using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using System.Collections.ObjectModel;

namespace ModHearth.UI;

public partial class SortRulesControl : UserControl, IModRefContextMenuProvider, IStyleAwareWindow, IDisposable
{
    private readonly ObservableCollection<ModRefViewModel> visibleMods = [];
    private readonly List<ModRefViewModel> allMods = [];
    private readonly Dictionary<string, ModRefViewModel> modIdMap = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ModRelationshipRule> rules = new(StringComparer.OrdinalIgnoreCase);
    private readonly Stack<Dictionary<string, ModRelationshipRule>> undoStack = new();
    //TODO Finish up implementing github community sort rules fetching
    private readonly string rulesFilePath;
    private readonly Action<Dictionary<string, ModRelationshipRule>>? onRulesChanged;
    private ModRefViewModel? selectedMod;
    private bool applyingUndo;
    private readonly ListSelectionController<ModRefViewModel> selectionController = new();
    private readonly ModSearchController searchController;
    private List<ModRefViewModel>? pickerModsCache;
    private int pickerModsCacheVersion = -1;
    private int rulesVersion;

    private List<ModRefViewModel> GetOrBuildPickerMods()
    {
        if (pickerModsCache != null && pickerModsCacheVersion == rulesVersion)
            return pickerModsCache;

        List<ModRefViewModel> copies = allMods.Select(vm =>
        {
            ModRefViewModel copy = new(vm.ModReference);
            MainWindowModListBuilder.CopyClassification(copy, vm);
            return copy;
        }).ToList();
        ModListIndicatorUpdater.UpdateRelationshipBadges(copies, rules);

        pickerModsCache = copies;
        pickerModsCacheVersion = rulesVersion;
        return pickerModsCache;
    }

    public event EventHandler? CloseRequested;

    public SortRulesControl()
        : this(new Dictionary<string, ModRelationshipRule>(), Array.Empty<ModReference>(), string.Empty, null)
    {
    }

    public SortRulesControl(
        IReadOnlyDictionary<string, ModRelationshipRule> existingRules,
        IEnumerable<ModReference> modRefs,
        string? rulesFilePath,
        Action<Dictionary<string, ModRelationshipRule>>? onRulesChanged = null)
    {
        InitializeComponent();
        this.rulesFilePath = rulesFilePath ?? string.Empty;
        this.onRulesChanged = onRulesChanged;

        foreach (KeyValuePair<string, ModRelationshipRule> kvp in existingRules ?? new Dictionary<string, ModRelationshipRule>())
            rules[kvp.Key] = kvp.Value.Clone();

        BuildViewModels(modRefs ?? Array.Empty<ModReference>());
        ModListIndicatorUpdater.UpdateRelationshipBadges(allMods, rules);
        modTreeList.ItemsSource = visibleMods;
        modTreeList.SelectionChanged += ModTreeSelectionChanged;
        selectionController.RegisterList(modTreeList);

        // Orchestrate search and filtering
        searchController = new ModSearchController(modSearchBar, visibleMods, allMods, modTreeList, selectionController);

        KeyDown += SortRulesControlKeyDown;

        RefreshEditor();

        fixConflictsButton.Click += async (_, _) => await FixConflictsAsync();
        clearAllRelationshipsButton.Click += async (_, _) => await ClearAllRelationshipsAsync();
        HorizontalScrollHelper.EnableSidewaysScrolling(titleScrollViewer);
        HorizontalScrollHelper.EnableSidewaysScrolling(validationScrollViewer);

        double sortRatio = ConfigManager.GetSortRulesWindowGridSplitterRatio();
        if (sortRatio > 0 && sortRatio < 1 && MainGrid != null && MainGrid.ColumnDefinitions.Count >= 3)
        {
            MainGrid.ColumnDefinitions[0].Width = new GridLength(sortRatio, GridUnitType.Star);
            MainGrid.ColumnDefinitions[2].Width = new GridLength(1.0 - sortRatio, GridUnitType.Star);
        }
    }

    public void SaveSplitterRatio()
    {
        if (MainGrid != null && MainGrid.ColumnDefinitions.Count >= 3)
        {
            double w0 = MainGrid.ColumnDefinitions[0].ActualWidth;
            double w2 = MainGrid.ColumnDefinitions[2].ActualWidth;
            if (w0 + w2 > 0)
            {
                double ratio = w0 / (w0 + w2);
                ratio = Math.Clamp(ratio, 0.05, 0.95);
                ConfigManager.SetSortRulesWindowGridSplitterRatio(ratio);
            }
        }
    }

    private bool _disposed;

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
            return;

        if (disposing)
        {
            SaveSplitterRatio();
            searchController?.Dispose();
        }

        _disposed = true;
    }

    private void SortRulesControlKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Z when e.KeyModifiers.HasFlag(KeyModifiers.Control):
                Undo();
                e.Handled = true;
                break;
            case Key.Escape:
                if (selectedMod != null)
                    modTreeList.SelectedItem = null;
                else
                {
                    CloseRequested?.Invoke(this, EventArgs.Empty);
                }
                e.Handled = true;
                break;

        }
    }

    private void BuildViewModels(IEnumerable<ModReference> modRefs)
    {
        string modsFolderPath = ConfigManager.GetModsPath();
        string vanillaFolderPath = ConfigManager.GetVanillaModsPath();

        List<ModRefViewModel> classified = modRefs
            .Where(m => !string.IsNullOrWhiteSpace(m.ID))
            .Select(m => MainWindowModListBuilder.CreateViewModel(m, modsFolderPath, vanillaFolderPath))
            .ToList();

        foreach (ModRefViewModel vm in MainWindowModListBuilder.CollapseByModId(classified)
                     .OrderBy(vm => vm.DisplayName, StringComparer.OrdinalIgnoreCase))
        {
            allMods.Add(vm);
            modIdMap[vm.ModReference.ID.Trim()] = vm;
        }
    }
    private void ModTreeSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (selectionController.HandleSelectionChanged(modTreeList))
            return;

        selectionController.UpdateSelectionState(modTreeList);

        selectedMod = modTreeList.SelectedItem as ModRefViewModel;
        RefreshEditor();
    }

    private void OnMainGridSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (MainGrid != null && MainGrid.ColumnDefinitions.Count > 0)
        {
            double dynamicMax = e.NewSize.Width - 408;
            MainGrid.ColumnDefinitions[0].MaxWidth = Math.Max(200, dynamicMax);
        }
    }

    // Handled by ModRefControl
    public void OnModRefContextMenuOpened(ContextMenu menu, ModRefViewModel vm) { }

    public ModHearthManager? GetManager() => null;

    public IEnumerable<ModReference> GetSelectedModReferences(ModRefViewModel contextVm)
    {
        return modTreeList.SelectedItems?.Cast<ModRefViewModel>().Select(vm => vm.ModReference)
            ?? Enumerable.Empty<ModReference>();
    }

    public async void OnModRefContextMenuItemClicked(MenuItem item, ModRefViewModel vm)
    {
        string? tag = item.Tag?.ToString();
        if (tag == "relation-clear-all")
        {
            selectedMod = vm;
            modTreeList.SelectedItem = vm;
            await ClearAllRelationshipsAsync();
            return;
        }

        ModRelationshipKind? kind = RelationshipKindFromTag(tag);
        if (kind == null)
            return;

        selectedMod = vm;
        modTreeList.SelectedItem = vm;
        await AddRelationshipAsync(kind.Value);
    }

    private bool isApplyingStyleInternal;
    private bool isRefreshing;
    public void ApplyCustomStyle(Style style)
    {
        if (isRefreshing) return;
        isRefreshing = true;
        try
        {
            RefreshEditor();
        }
        finally { isRefreshing = false; }
    }

    private void RefreshEditor()
    {
        sectionsPanel.Children.Clear();

        if (selectedMod == null)
        {
            int globalRelationshipCount = CountAllRelationships();
            clearAllRelationshipsButton.IsEnabled = globalRelationshipCount > 0;
            clearAllRelationshipsButton.Opacity = clearAllRelationshipsButton.IsEnabled ? 1.0 : 0.4;
            clearAllRelationshipsButton.Content = BuildCountedButtonLabel("Clear", "relationship", globalRelationshipCount, "Clear all relationships");

            selectedTitleText.Text = "Select a mod";
            selectedSubtitleText.Text = "Choose a mod to edit its relationships.";
            int totalBefore = rules.Values.Sum(r => r.BeforeIds.Count);
            int totalAfter = rules.Values.Sum(r => r.AfterIds.Count);
            int totalRequired = rules.Values.Sum(r => r.RequiredIds.Count);
            int totalIncompatible = rules.Values.Sum(r => r.IncompatibleIds.Count);
            SetSummary(totalBefore, totalAfter, totalRequired, totalIncompatible);

            ValidationResult globalValidation = ValidateRules();
            int globalIssueCount = globalValidation.Issues?.Count ?? 0;
            fixConflictsButton.IsEnabled = globalIssueCount > 0;
            fixConflictsButton.Opacity = fixConflictsButton.IsEnabled ? 1.0 : 0.4;
            fixConflictsButton.Content = BuildCountedButtonLabel("Fix", "conflict", globalIssueCount, "Fix conflicts");

            if (globalValidation.Issues != null && globalValidation.Issues.Count > 0)
            {
                validationText.IsVisible = false;
                validationScrollViewer.IsVisible = true;
                validationIssuesPanel.IsVisible = true;
                validationIssuesPanel.Children.Clear();
                foreach (Control issueCtrl in globalValidation.Issues)
                    validationIssuesPanel.Children.Add(issueCtrl);
            }
            else
            {
                validationText.IsVisible = true;
                validationScrollViewer.IsVisible = false;
                validationIssuesPanel.IsVisible = false;
                validationText.Text = globalValidation.Message;
            }

            validateTextColor(validationText, validationIssuesPanel, globalValidation);

            if (Style.instance != null && !isApplyingStyleInternal)
            {
                isApplyingStyleInternal = true;
                try
                {
                    var topLevel = TopLevel.GetTopLevel(this);
                    if (topLevel is Window w)
                        WindowThemeManager.ApplyToWindow(w, Style.instance);
                }
                finally { isApplyingStyleInternal = false; }
            }
            return;
        }

        int modRelationshipCount = selectedMod.RelationshipCount;
        clearAllRelationshipsButton.IsEnabled = modRelationshipCount > 0;
        clearAllRelationshipsButton.Opacity = clearAllRelationshipsButton.IsEnabled ? 1.0 : 0.4;
        clearAllRelationshipsButton.Content = BuildCountedButtonLabel("Clear", "relationship", modRelationshipCount, "Clear all relationships");

        ValidationResult modValidation = ValidateRules(selectedMod.ModReference.ID);
        int modIssueCount = modValidation.Issues?.Count ?? 0;
        fixConflictsButton.IsEnabled = modIssueCount > 0;
        fixConflictsButton.Opacity = fixConflictsButton.IsEnabled ? 1.0 : 0.4;
        fixConflictsButton.Content = BuildCountedButtonLabel("Fix", "conflict", modIssueCount, "Fix conflicts");

        ModRelationshipRule rule = GetRule(selectedMod.ModReference.ID);
        selectedTitleText.Text = selectedMod.ModReference.name ?? selectedMod.ModReference.ID;
        selectedSubtitleText.Text = $"{selectedMod.ModReference.author}   {selectedMod.ModReference.ID}";
        SetSummary(rule.BeforeIds.Count, rule.AfterIds.Count, rule.RequiredIds.Count, rule.IncompatibleIds.Count);

        if (modValidation.Issues != null && modValidation.Issues.Count > 0)
        {
            validationText.IsVisible = false;
            validationScrollViewer.IsVisible = true;
            validationIssuesPanel.IsVisible = true;
            validationIssuesPanel.Children.Clear();
            foreach (Control issueCtrl in modValidation.Issues)
                validationIssuesPanel.Children.Add(issueCtrl);
        }
        else
        {
            validationText.IsVisible = true;
            validationScrollViewer.IsVisible = false;
            validationIssuesPanel.IsVisible = false;
            validationText.Text = modValidation.Message;
        }

        validateTextColor(validationText, validationIssuesPanel, modValidation);

        sectionsPanel.Children.Add(CreateSection(ModRelationshipKind.Before, "Before", "This mod must load before these mods.", rule.BeforeIds));
        sectionsPanel.Children.Add(CreateSection(ModRelationshipKind.After, "After", "This mod must load after these mods.", rule.AfterIds));
        sectionsPanel.Children.Add(CreateSection(ModRelationshipKind.Required, "Required", "These mods are required when this mod is enabled.", rule.RequiredIds));
        sectionsPanel.Children.Add(CreateSection(ModRelationshipKind.Incompatible, "Incompatible", "These mods should not be enabled together.", rule.IncompatibleIds));

        if (Style.instance != null && !isApplyingStyleInternal)
        {
            isApplyingStyleInternal = true;
            try
            {
                var topLevel = TopLevel.GetTopLevel(this);
                if (topLevel is Window w)
                    WindowThemeManager.ApplyToWindow(w, Style.instance);
            }
            finally { isApplyingStyleInternal = false; }
        }
    }

    private static void validateTextColor(TextBlock text, StackPanel issuesPanel, ValidationResult result)
    {
        IBrush brush;
        if (result.HasError)
            brush = Brushes.IndianRed;
        else if (result.HasWarning)
            brush = Brushes.Goldenrod;
        else
            brush = Brushes.SeaGreen;

        text.Foreground = brush;

        foreach (Control control in issuesPanel.Children)
            ApplyBrushRecursive(control, brush);
    }

    private static void ApplyBrushRecursive(Control control, IBrush brush)
    {
        switch (control)
        {
            case TextBlock tb:
                if (tb.Tag as string != "DisplayLabel")
                    tb.Foreground = brush;
                break;
            case Panel panel:
                {
                    foreach (Control child in panel.Children)
                    {
                        ApplyBrushRecursive(child, brush);
                    }

                    break;
                }

            case Border border when border.Child is Control childControl:
                ApplyBrushRecursive(childControl, brush);
                break;

        }
    }

    private Control CreateSection(ModRelationshipKind kind, string title, string description, IReadOnlyList<string> ids)
    {
        StackPanel panel = new() { Spacing = 6 };
        Border border = new()
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10),
            Child = panel
        };
        if (Style.instance != null)
            border.Background = BrushCache.GetBrush(Style.instance.panelColor.ToAvaloniaColor());

        Grid header = new() { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"), ColumnSpacing = 8 };
        Border accent = new()
        {
            Width = 5,
            Height = 22,
            CornerRadius = new CornerRadius(2),
            Background = ModListIndicatorUpdater.RelationshipBrush(kind),
            VerticalAlignment = VerticalAlignment.Center
        };
        DockPanel titleContainer = new()
        {
            VerticalAlignment = VerticalAlignment.Center
        };

        Image? icon = IconFor(kind);
        if (icon != null)
        {
            icon.Width = 14;
            icon.Height = 14;
            icon.Margin = new Thickness(0, 0, 6, 0);
            titleContainer.Children.Add(icon);
        }

        TextBlock titleText = new()
        {
            Text = title,
            Tag = "IgnoreTheme",
            FontWeight = FontWeight.Bold,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        titleContainer.Children.Add(titleText);

        Button addButton = new()
        {
            Content = $"Add {title}",
            MinWidth = 94,
            Height = 28,
            HorizontalContentAlignment = HorizontalAlignment.Center
        };
        Button clearButton = new()
        {
            Content = $"Clear {title}",
            MinWidth = 96,
            Height = 28,
            IsEnabled = ids.Count > 0,
            Opacity = ids.Count > 0 ? 1.0 : 0.4,
            HorizontalContentAlignment = HorizontalAlignment.Center
        };

        Grid.SetColumn(titleContainer, 1);
        Grid.SetColumn(addButton, 2);
        Grid.SetColumn(clearButton, 3);
        header.Children.Add(accent);
        header.Children.Add(titleContainer);
        header.Children.Add(addButton);
        header.Children.Add(clearButton);
        panel.Children.Add(header);
        panel.Children.Add(new TextBlock { Text = description, FontSize = 12, Opacity = 0.72 });

        switch (ids.Count)
        {
            case 0:
                panel.Children.Add(new TextBlock
                {
                    Text = $"No {title.ToLowerInvariant()} mods.",
                    FontStyle = FontStyle.Italic,
                    Opacity = 0.65,
                    Margin = new Thickness(0, 5, 0, 2)
                });
                break;
            default:
                {
                    foreach (string id in SortIdsForDisplay(ids))
                        panel.Children.Add(CreateRelationshipRow(kind, id));
                    break;
                }

        }

        addButton.Click += async (_, _) => await AddRelationshipAsync(kind);
        clearButton.Click += (_, _) => ClearRelationship(kind);
        return border;
    }

    private Control CreateRelationshipRow(ModRelationshipKind kind, string id)
    {
        Grid row = new()
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            ColumnSpacing = 8,
            Margin = new Thickness(0, 2)
        };

        Control content = modIdMap.TryGetValue(id, out ModRefViewModel? vm)
            ? CreateKnownModRow(vm)
            : CreateMissingModRow(id);

        Button removeButton = new()
        {
            Content = new Image
            {
                Width = 12,
                Height = 12,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Source = ImageSourceLoader.LoadFromAssetUri("cancelIcon.svg")
            },
            Width = 24,
            Height = 24,
            Padding = new Thickness(6),
            Margin = new Thickness(2, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
        };

        ToolTip.SetTip(removeButton, "Remove relationship");
        removeButton.Click += (_, _) => RemoveRelationship(kind, id);

        Border accent = new()
        {
            Width = 3,
            Background = ModListIndicatorUpdater.RelationshipBrush(kind),
            CornerRadius = new CornerRadius(2)
        };
        Grid.SetColumn(content, 1);
        Grid.SetColumn(removeButton, 2);
        row.Children.Add(accent);
        row.Children.Add(content);
        row.Children.Add(removeButton);
        return row;
    }

    private Control CreateKnownModRow(ModRefViewModel vm)
    {
        ModRefViewModel displayVm = new(vm.ModReference)
        {
            IsVanillaModSource = vm.IsVanillaModSource,
            IsLocalModSource = vm.IsLocalModSource,
            IsSteamModSource = vm.IsSteamModSource,
            IsSteamLocalModSource = vm.IsSteamLocalModSource
        };
        displayVm.RefreshStyle();
        ModListIndicatorUpdater.UpdateRelationshipBadges([displayVm], rules);

        Grid grid = new() { RowDefinitions = new RowDefinitions("Auto,Auto") };
        ModRefControl modControl = new()
        {
            DataContext = displayVm,
            ShowDetailedRuleBadges = true,
            AllowContextActions = false,
            AllowRelationshipEditing = false,
            Padding = new Thickness(0)
        };
        TextBlock detail = new()
        {
            Text = $"{vm.ModReference.author}   {vm.ModReference.ID}",
            FontSize = 11,
            Opacity = 0.62,
            Margin = new Thickness(24, 0, 0, 0)
        };
        Grid.SetRow(detail, 1);
        grid.Children.Add(modControl);
        grid.Children.Add(detail);
        return grid;
    }

    private static Control CreateMissingModRow(string id)
    {
        StackPanel panel = new() { Spacing = 1 };
        panel.Children.Add(new TextBlock
        {
            Text = "Warning: Missing Mod",
            FontWeight = FontWeight.Bold,
            Foreground = Brushes.Goldenrod
        });
        panel.Children.Add(new TextBlock
        {
            Text = id,
            FontSize = 11,
            Foreground = Brushes.Gray
        });
        return panel;
    }

    private async Task AddRelationshipAsync(ModRelationshipKind kind)
    {
        if (selectedMod == null)
            return;

        string ownerId = selectedMod.ModReference.ID.Trim();
        ModRelationshipRule rule = GetRule(ownerId);
        HashSet<string> alreadyAdded = new(GetList(rule, kind), StringComparer.OrdinalIgnoreCase);
        RelationshipPickerWindow picker = new(ownerId, kind, GetOrBuildPickerMods(), alreadyAdded, rules)
        {
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        var ownerWindow = TopLevel.GetTopLevel(this) as Window;
        if (ownerWindow == null)
            return;
        string? selectedId = await picker.ShowDialog<string?>(ownerWindow);
        if (string.IsNullOrWhiteSpace(selectedId))
            return;

        PushUndo();
        GetList(GetOrCreateRule(ownerId), kind).Add(selectedId.Trim());
        CommitRulesChanged();
    }

    private void ClearRelationship(ModRelationshipKind kind)
    {
        if (selectedMod == null)
            return;

        PushUndo();
        GetList(GetOrCreateRule(selectedMod.ModReference.ID), kind).Clear();
        CommitRulesChanged();
    }

    private void RemoveRelationship(ModRelationshipKind kind, string id)
    {
        if (selectedMod == null)
            return;

        PushUndo();
        List<string> list = GetList(GetOrCreateRule(selectedMod.ModReference.ID), kind);
        _ = list.RemoveAll(existing => string.Equals(existing, id, StringComparison.OrdinalIgnoreCase));
        CommitRulesChanged();
    }

    private void CommitRulesChanged()
    {
        rulesVersion++;
        NormalizeRules();
        ModListIndicatorUpdater.UpdateRelationshipBadges(allMods, rules);
        onRulesChanged?.Invoke(CloneRules(rules));
        RefreshEditor();
    }

    private void PushUndo()
    {
        if (!applyingUndo)
            undoStack.Push(CloneRules(rules));
    }

    public void Undo()
    {
        if (undoStack.Count == 0)
            return;

        applyingUndo = true;
        try
        {
            rules.Clear();
            foreach (KeyValuePair<string, ModRelationshipRule> kvp in undoStack.Pop())
                rules[kvp.Key] = kvp.Value.Clone();
            rulesVersion++;
            onRulesChanged?.Invoke(CloneRules(rules));
            RefreshEditor();
        }
        finally
        {
            applyingUndo = false;
        }
    }

    private void SetSummary(int beforeCount, int afterCount, int requiredCount, int incompatibleCount)
    {
        summaryText.Text = $"Before: {beforeCount}   After: {afterCount}   Required: {requiredCount}   Incompatible: {incompatibleCount}";
    }

    private static string BuildCountedButtonLabel(string verb, string singularNoun, int count, string zeroLabel)
    {
        switch (count)
        {
            case <= 0:
                return zeroLabel;
            default:
                return $"{verb} {count} {singularNoun}{(count == 1 ? string.Empty : "s")}";
        }
    }

    private int CountAllRelationships()
    {
        int total = 0;
        foreach (ModRelationshipRule rule in rules.Values)
            total += rule.BeforeIds.Count + rule.AfterIds.Count + rule.RequiredIds.Count + rule.IncompatibleIds.Count;
        return total;
    }

    private void NormalizeRules()
    {
        foreach (string key in rules.Keys.ToList())
        {
            ModRelationshipRule rule = rules[key];
            rule.BeforeIds = NormalizeList(rule.BeforeIds);
            rule.AfterIds = NormalizeList(rule.AfterIds);
            rule.RequiredIds = NormalizeList(rule.RequiredIds);
            rule.IncompatibleIds = NormalizeList(rule.IncompatibleIds);
            if (rule.IsEmpty)
                _ = rules.Remove(key);
        }
    }

    private static List<string> NormalizeList(IEnumerable<string> ids)
    {
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        List<string> normalized = [];
        foreach (string id in ids)
        {
            string trimmed = id?.Trim() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(trimmed) && seen.Add(trimmed))
                normalized.Add(trimmed);
        }
        return normalized;
    }

    private ModRelationshipRule GetRule(string id)
    {
        return rules.TryGetValue(id.Trim(), out ModRelationshipRule? rule)
            ? rule
            : new ModRelationshipRule();
    }

    private ModRelationshipRule GetOrCreateRule(string id)
    {
        string key = id.Trim();
        if (!rules.TryGetValue(key, out ModRelationshipRule? rule))
        {
            rule = new ModRelationshipRule();
            rules[key] = rule;
        }
        return rule;
    }

    private static List<string> GetList(ModRelationshipRule rule, ModRelationshipKind kind)
    {
        return kind switch
        {
            ModRelationshipKind.Before => rule.BeforeIds,
            ModRelationshipKind.After => rule.AfterIds,
            ModRelationshipKind.Required => rule.RequiredIds,
            ModRelationshipKind.Incompatible => rule.IncompatibleIds,
            _ => rule.BeforeIds
        };
    }

    private IEnumerable<string> SortIdsForDisplay(IEnumerable<string> ids)
    {
        return ids
            .OrderBy(id => modIdMap.TryGetValue(id, out ModRefViewModel? vm) ? vm.DisplayName : id, StringComparer.OrdinalIgnoreCase)
            .ThenBy(id => id, StringComparer.OrdinalIgnoreCase);
    }

    private ValidationResult ValidateRules(string? filterModId = null)
    {
        List<Control> warnings = [];
        List<Control> errors = [];
        Dictionary<string, HashSet<string>> graph = new(StringComparer.OrdinalIgnoreCase);

        Dictionary<string, List<Control>> issuesPerMod = new(StringComparer.OrdinalIgnoreCase);
        void AddIssue(string modId, Control issueControl, bool isError)
        {
            if (isError) errors.Add(issueControl);
            else warnings.Add(issueControl);

            if (!issuesPerMod.TryGetValue(modId, out List<Control>? list))
                issuesPerMod[modId] = list = [];
            list.Add(issueControl);
        }

        foreach (KeyValuePair<string, ModRelationshipRule> kvp in rules)
        {
            string ownerId = kvp.Key;

            HashSet<string> beforeSet = new(kvp.Value.BeforeIds.Select(t => t.Trim()).Where(t => !string.IsNullOrWhiteSpace(t)), StringComparer.OrdinalIgnoreCase);
            HashSet<string> afterSet = new(kvp.Value.AfterIds.Select(t => t.Trim()).Where(t => !string.IsNullOrWhiteSpace(t)), StringComparer.OrdinalIgnoreCase);
            HashSet<string> requiredSet = new(kvp.Value.RequiredIds.Select(t => t.Trim()).Where(t => !string.IsNullOrWhiteSpace(t)), StringComparer.OrdinalIgnoreCase);
            HashSet<string> incompatibleSet = new(kvp.Value.IncompatibleIds.Select(t => t.Trim()).Where(t => !string.IsNullOrWhiteSpace(t)), StringComparer.OrdinalIgnoreCase);

            foreach (string target in incompatibleSet)
            {
                if (requiredSet.Contains(target))
                    AddIssue(ownerId, BuildIssueControl(DisplayLabel(ownerId), " cannot be both required and incompatible with ", DisplayLabel(target)), true);
                if (beforeSet.Contains(target))
                    AddIssue(ownerId, BuildIssueControl(DisplayLabel(ownerId), " cannot be both before and incompatible with ", DisplayLabel(target)), true);
                if (afterSet.Contains(target))
                    AddIssue(ownerId, BuildIssueControl(DisplayLabel(ownerId), " cannot be both after and incompatible with ", DisplayLabel(target)), true);
            }

            foreach (string target in beforeSet)
            {
                if (afterSet.Contains(target))
                    AddIssue(ownerId, BuildIssueControl(DisplayLabel(ownerId), " cannot be both before and after ", DisplayLabel(target)), true);
            }

            AddEdges(ownerId, kvp.Value.BeforeIds, ownerId, target => target, "before", targetIsSource: false, conflictSet: afterSet);
            AddEdges(ownerId, kvp.Value.AfterIds, ownerId, target => ownerId, "after", targetIsSource: true, conflictSet: beforeSet);
            AddEdges(ownerId, kvp.Value.RequiredIds, ownerId, target => ownerId, "required", targetIsSource: true, conflictSet: incompatibleSet, addGraphEdge: false);
            foreach (string id in kvp.Value.IncompatibleIds)
            {
                string target = id.Trim();
                if (string.IsNullOrWhiteSpace(target))
                    continue;

                if (string.Equals(ownerId, target, StringComparison.OrdinalIgnoreCase))
                    AddIssue(ownerId, BuildIssueControl(DisplayLabel(ownerId), " cannot be incompatible with itself."), true);
                else if (!modIdMap.ContainsKey(target))
                    AddIssue(ownerId, BuildIssueControl(DisplayLabel(ownerId), " references missing mod ", DisplayLabel(target)), false);
            }
        }

        foreach (KeyValuePair<string, HashSet<string>> kvp in graph)
        {
            foreach (var target in kvp.Value.Where(target => WouldCreateCycle(graph, kvp.Key, target)))
            {
                Control circularCtrl = BuildIssueControl("Circular dependency detected: ", DisplayLabel(kvp.Key), " and ", DisplayLabel(target));
                AddIssue(kvp.Key, circularCtrl, true);
                AddIssue(target, circularCtrl, true);
            }

        }

        if (!string.IsNullOrEmpty(filterModId))
        {
            if (issuesPerMod.TryGetValue(filterModId, out List<Control>? modIssues))
            {
                bool hasModError = modIssues.Any(errors.Contains);
                bool hasModWarning = modIssues.Any(warnings.Contains);
                return new ValidationResult(hasModError, hasModWarning, string.Empty, modIssues.Distinct().ToList());
            }
            return new ValidationResult(false, false, "No conflicts detected for this mod.");
        }

        return BuildValidationResult(errors, warnings);

        void AddEdges(
            string ownerId,
            IEnumerable<string> ids,
            string source,
            Func<string, string> destinationFactory,
            string category,
            bool targetIsSource = false,
            HashSet<string>? conflictSet = null,
            bool addGraphEdge = true)
        {
            foreach (string rawId in ids)
            {
                string target = rawId.Trim();
                if (string.IsNullOrWhiteSpace(target))
                    continue;

                if (conflictSet != null && conflictSet.Contains(target))
                    continue;

                if (string.Equals(ownerId, target, StringComparison.OrdinalIgnoreCase))
                {
                    AddIssue(ownerId, category == "required"
                        ? BuildIssueControl(DisplayLabel(ownerId), " cannot require itself.")
                        : BuildIssueControl(DisplayLabel(ownerId), " cannot reference itself."), true);
                    continue;
                }

                if (!modIdMap.ContainsKey(target))
                    AddIssue(ownerId, BuildIssueControl(DisplayLabel(ownerId), $" references missing mod {target}."), false);

                if (addGraphEdge)
                {
                    string from = targetIsSource ? target : source;
                    string to = destinationFactory(target);
                    if (!graph.TryGetValue(from, out HashSet<string>? destinations))
                    {
                        destinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        graph[from] = destinations;
                    }
                    _ = destinations.Add(to);
                }
            }
        }
    }

    private static ValidationResult BuildValidationResult(List<Control> errors, List<Control> warnings)
    {
        List<Control> allIssues = errors.Concat(warnings).Distinct().ToList();
        if (errors.Count > 0)
            return new ValidationResult(true, warnings.Count > 0, string.Empty, allIssues);
        switch (warnings.Count)
        {
            case > 0:
                return new ValidationResult(false, true, string.Empty, allIssues);
            default:
                return new ValidationResult(false, false, "No conflicts detected.");
        }
    }

    private static bool WouldCreateCycle(Dictionary<string, HashSet<string>> graph, string fromId, string toId)
    {
        return ModHearthManager.WouldCreateCycle(graph, fromId, toId);
    }

    private TextBlock DisplayLabel(string id)
    {
        string label = modIdMap.TryGetValue(id, out ModRefViewModel? vm) ? vm.DisplayName : id;
        IBrush textBrush = Style.instance != null ? BrushCache.GetBrush(Style.instance.textColor.ToAvaloniaColor()) : Brushes.Black;

        return new TextBlock
        {
            Text = label,
            FontStyle = FontStyle.Italic,
            FontSize = 11.5,
            Foreground = textBrush,
            VerticalAlignment = VerticalAlignment.Bottom,
            Padding = new Thickness(0, 0, 0, 0.5),
            Tag = "DisplayLabel"
        };
    }

    private static Control BuildIssueControl(params object[] parts)
    {
        WrapPanel panel = new() { Orientation = Orientation.Horizontal };
        foreach (object part in parts)
        {
            switch (part)
            {
                case string s:
                    panel.Children.Add(new TextBlock
                    {
                        Text = s,
                        Tag = "IgnoreTheme",
                        VerticalAlignment = VerticalAlignment.Center
                    });
                    break;
                case Control c:
                    panel.Children.Add(c);
                    break;

            }
        }
        return panel;
    }

    private static Dictionary<string, ModRelationshipRule> CloneRules(IDictionary<string, ModRelationshipRule> source)
    {
        Dictionary<string, ModRelationshipRule> clone = new(StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<string, ModRelationshipRule> kvp in source)
            clone[kvp.Key] = kvp.Value.Clone();
        return clone;
    }

    private static Image? IconFor(ModRelationshipKind kind)
    {
        return kind switch
        {
            ModRelationshipKind.Before => ImageSourceLoader.CreateAvaloniaImage("arrowUpIcon.svg"),
            ModRelationshipKind.After => ImageSourceLoader.CreateAvaloniaImage("arrowDownIcon.svg"),
            ModRelationshipKind.Required => ImageSourceLoader.CreateAvaloniaImage("linkIcon.svg"),
            ModRelationshipKind.Incompatible => ImageSourceLoader.CreateAvaloniaImage("cancelCircleIcon.svg"),
            _ => null
        };
    }

    private static ModRelationshipKind? RelationshipKindFromTag(string? tag)
    {
        return tag switch
        {
            "relation-before" => ModRelationshipKind.Before,
            "relation-after" => ModRelationshipKind.After,
            "relation-required" => ModRelationshipKind.Required,
            "relation-incompatible" => ModRelationshipKind.Incompatible,
            _ => null
        };
    }

    private readonly record struct ValidationResult(bool HasError, bool HasWarning, string Message, List<Control>? Issues = null);

    private async Task ClearAllRelationshipsAsync()
    {
        bool isGlobal = selectedMod == null;
        string? ownerId = isGlobal ? null : selectedMod!.ModReference.ID.Trim();
        int affectedCount = isGlobal ? CountAllRelationships() : selectedMod!.RelationshipCount;
        if (affectedCount == 0)
            return;

        string prompt = isGlobal
            ? "Clear every relationship (Before, After, Required, Incompatible) for every mod?"
            : "Clear all relationships (Before, After, Required, Incompatible) for this mod?";

        var ownerWindow = TopLevel.GetTopLevel(this) as Window;
        if (ownerWindow == null)
            return;
        bool confirm = await DialogService.ShowConfirmAsync(ownerWindow, prompt, "Clear All Relationships");
        if (!confirm)
            return;

        PushUndo();

        if (isGlobal)
            rules.Clear();
        else
            foreach (ModRelationshipKind kind in Enum.GetValues<ModRelationshipKind>())
                GetList(GetOrCreateRule(ownerId!), kind).Clear();

        CommitRulesChanged();
    }

    private async Task FixConflictsAsync()
    {
        if (selectedMod == null)
        {
            await FixConflictsCoreAsync(null, "Attempt to automatically fix all relationship conflicts across every mod?");
            return;
        }

        await FixConflictsCoreAsync(
            selectedMod.ModReference.ID.Trim(),
            $"Attempt to automatically fix relationship conflicts for '{selectedMod.DisplayName}'?");
    }

    private async Task FixConflictsCoreAsync(string? ownerId, string confirmPrompt)
    {
        bool isGlobal = ownerId == null;
        ValidationResult validation = ValidateRules(ownerId);
        if (!validation.HasError && !validation.HasWarning)
            return;

        var ownerWindow = TopLevel.GetTopLevel(this) as Window;
        if (ownerWindow == null)
            return;
        bool confirm = await DialogService.ShowConfirmAsync(ownerWindow, confirmPrompt, "Fix Conflicts");
        if (!confirm)
            return;

        PushUndo();

        List<string> ownerIds = isGlobal ? rules.Keys.ToList() : [ownerId!];
        Dictionary<string, ModRelationshipRule> targetRules = ownerIds.ToDictionary(
            id => id, GetOrCreateRule, StringComparer.OrdinalIgnoreCase);

        foreach ((string id, ModRelationshipRule rule) in targetRules)
        {
            _ = rule.BeforeIds.RemoveAll(target => IsSelfOrMissing(id, target));
            _ = rule.AfterIds.RemoveAll(target => IsSelfOrMissing(id, target));
            _ = rule.RequiredIds.RemoveAll(target => IsSelfOrMissing(id, target));
            _ = rule.IncompatibleIds.RemoveAll(target => IsSelfOrMissing(id, target));

            _ = rule.IncompatibleIds.RemoveAll(target => rule.RequiredIds.Any(r => string.Equals(r, target, StringComparison.OrdinalIgnoreCase)));
            _ = rule.IncompatibleIds.RemoveAll(target => rule.BeforeIds.Any(b => string.Equals(b, target, StringComparison.OrdinalIgnoreCase)));
            _ = rule.IncompatibleIds.RemoveAll(target => rule.AfterIds.Any(a => string.Equals(a, target, StringComparison.OrdinalIgnoreCase)));
            _ = rule.AfterIds.RemoveAll(target => rule.BeforeIds.Any(b => string.Equals(b, target, StringComparison.OrdinalIgnoreCase)));
        }

        Dictionary<string, HashSet<string>> graph = new(StringComparer.OrdinalIgnoreCase);
        if (!isGlobal)
        {
            foreach (KeyValuePair<string, ModRelationshipRule> kvp in rules)
            {
                if (string.Equals(kvp.Key, ownerId, StringComparison.OrdinalIgnoreCase))
                    continue;

                foreach (string target in kvp.Value.BeforeIds) AddGraphEdge(graph, kvp.Key, target);
                foreach (string target in kvp.Value.AfterIds) AddGraphEdge(graph, target, kvp.Key);
            }
        }

        foreach ((string id, ModRelationshipRule rule) in targetRules)
        {
            List<string> validBefore = [];
            foreach (var target in from string target in rule.BeforeIds
                                   where !WouldCreateCycle(graph, id, target)
                                   select target)
            {
                validBefore.Add(target);
                AddGraphEdge(graph, id, target);
            }


            rule.BeforeIds = validBefore;

            List<string> validAfter = [];
            foreach (var target in from string target in rule.AfterIds
                                   where !WouldCreateCycle(graph, target, id)
                                   select target)
            {
                validAfter.Add(target);
                AddGraphEdge(graph, target, id);
            }


            rule.AfterIds = validAfter;
        }

        CommitRulesChanged();
    }

    private static void AddGraphEdge(Dictionary<string, HashSet<string>> graph, string from, string to)
    {
        if (!graph.TryGetValue(from, out HashSet<string>? edges))
        {
            edges = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            graph[from] = edges;
        }
        _ = edges.Add(to);
    }

    private bool IsSelfOrMissing(string ownerId, string targetId)
    {
        string target = targetId.Trim();
        if (string.Equals(ownerId, target, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        return !modIdMap.ContainsKey(target);
    }
}
