using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ModHearth.Utilities;
using System.Collections.ObjectModel;
using System.Diagnostics;

namespace ModHearth.UI;

public partial class SortRulesWindow : Window, IModRefContextMenuProvider
{
    private readonly ObservableCollection<ModRefViewModel> visibleMods = new();
    private readonly List<ModRefViewModel> allMods = new();
    private readonly Dictionary<string, ModRefViewModel> modIdMap = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ModRelationshipRule> rules = new(StringComparer.OrdinalIgnoreCase);
    private readonly Stack<Dictionary<string, ModRelationshipRule>> undoStack = new();
    private readonly string rulesFilePath;
    private readonly Action<Dictionary<string, ModRelationshipRule>>? onRulesChanged;
    private ModRefViewModel? selectedMod;
    private bool applyingUndo;
    private readonly ListSelectionController<ModRefViewModel> selectionController = new();
    private readonly ModSearchController searchController;

    public SortRulesWindow()
        : this(new Dictionary<string, ModRelationshipRule>(), Array.Empty<ModReference>(), string.Empty, null)
    {
    }

    public SortRulesWindow(
        IReadOnlyDictionary<string, ModRelationshipRule> existingRules,
        IEnumerable<ModReference> modRefs,
        string? rulesFilePath,
        Action<Dictionary<string, ModRelationshipRule>>? onRulesChanged = null)
    {
        InitializeComponent();
        WindowThemeManager.Register(this);
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

        KeyDown += SortRulesWindowKeyDown;

        ApplyModFilter();
        selectedMod = visibleMods.FirstOrDefault();
        if (selectedMod != null)
            modTreeList.SelectedItem = selectedMod;
        selectionController.UpdateSelectionState(modTreeList);
        RefreshEditor();

        clearAllRelationshipsButton.Click += async (_, _) => await ClearAllRelationshipsAsync();

        double sortRatio = ConfigManager.GetSortRulesWindowGridSplitterRatio();
        if (sortRatio > 0 && sortRatio < 1 && MainGrid != null && MainGrid.ColumnDefinitions.Count >= 3)
        {
            MainGrid.ColumnDefinitions[0].Width = new GridLength(sortRatio, GridUnitType.Star);
            MainGrid.ColumnDefinitions[2].Width = new GridLength(1.0 - sortRatio, GridUnitType.Star);
        }

        Closed += (_, _) =>
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
        };
    }

    private void BuildViewModels(IEnumerable<ModReference> modRefs)
    {
        string modsFolderPath = ConfigManager.GetModsPath();
        string vanillaFolderPath = ConfigManager.GetVanillaModsPath();

        foreach (ModReference modref in modRefs
            .Where(m => !string.IsNullOrWhiteSpace(m.ID))
            .GroupBy(m => m.ID.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(m => DisplayNameFor(m), StringComparer.OrdinalIgnoreCase))
        {
            ModRefViewModel vm = new ModRefViewModel(modref);
            (bool isVanilla, bool isLocal, bool isSteam) = ModSourceClassifier.Classify(modref, modsFolderPath, vanillaFolderPath);
            vm.IsVanillaModSource = isVanilla;
            vm.IsLocalModSource = isLocal;
            vm.IsSteamModSource = isSteam;
            vm.RefreshStyle();
            allMods.Add(vm);
            modIdMap[modref.ID.Trim()] = vm;
        }
    }

    private void ApplyModFilter()
    {
        searchController.ApplyFilterImmediately();
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

    public ModHearthManager? GetManager() => null; // We don't use the manager here for context actions

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

    private void RefreshEditor()
    {
        sectionsPanel.Children.Clear();
        clearAllRelationshipsButton.IsVisible = selectedMod?.HasRelationships == true;

        if (selectedMod == null)
        {
            selectedTitleText.Text = "Select a mod";
            selectedSubtitleText.Text = "Choose a mod to edit its relationships.";
            summaryText.Text = "Before: 0   After: 0   Required: 0   Incompatible: 0";
            validationText.Text = string.Empty;
            return;
        }

        ModRelationshipRule rule = GetRule(selectedMod.ModReference.ID);
        selectedTitleText.Text = selectedMod.ModReference.name ?? selectedMod.ModReference.ID;
        selectedSubtitleText.Text = $"{selectedMod.ModReference.author}   {selectedMod.ModReference.ID}";
        summaryText.Text =
            $"Before: {rule.BeforeIds.Count}   After: {rule.AfterIds.Count}   Required: {rule.RequiredIds.Count}   Incompatible: {rule.IncompatibleIds.Count}";

        ValidationResult validation = ValidateRules();
        validationText.Text = validation.Message;

        if (validation.HasError)
            validationText.Foreground = Brushes.IndianRed;
        else if (validation.HasWarning)
            validationText.Foreground = Brushes.Goldenrod;
        else
            validationText.Foreground = Brushes.SeaGreen;

        sectionsPanel.Children.Add(CreateSection(ModRelationshipKind.Before, "Before", "This mod must load before these mods.", rule.BeforeIds));
        sectionsPanel.Children.Add(CreateSection(ModRelationshipKind.After, "After", "This mod must load after these mods.", rule.AfterIds));
        sectionsPanel.Children.Add(CreateSection(ModRelationshipKind.Required, "Required", "These mods are required when this mod is enabled.", rule.RequiredIds));
        sectionsPanel.Children.Add(CreateSection(ModRelationshipKind.Incompatible, "Incompatible", "These mods should not be enabled together.", rule.IncompatibleIds));
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
            border.Background = BrushCache.GetBrush(Style.instance.backgroundColor.ToAvaloniaColor());

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

        if (ids.Count == 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = $"No {title.ToLowerInvariant()} mods.",
                FontStyle = FontStyle.Italic,
                Opacity = 0.65,
                Margin = new Thickness(0, 5, 0, 2)
            });
        }
        else
        {
            foreach (string id in SortIdsForDisplay(ids))
                panel.Children.Add(CreateRelationshipRow(kind, id));
        }

        addButton.Click += async (_, _) => await AddRelationshipAsync(kind);
        clearButton.Click += async (_, _) => await ClearRelationshipAsync(kind);
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

    private static Control CreateKnownModRow(ModRefViewModel vm)
    {
        Grid grid = new() { RowDefinitions = new RowDefinitions("Auto,Auto") };
        ModRefControl modControl = new()
        {
            DataContext = vm,
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
        RelationshipPickerWindow picker = new(ownerId, kind, allMods, alreadyAdded, rules)
        {
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        string? selectedId = await picker.ShowDialog<string?>(this);
        if (string.IsNullOrWhiteSpace(selectedId))
            return;

        PushUndo();
        GetList(GetOrCreateRule(ownerId), kind).Add(selectedId.Trim());
        CommitRulesChanged();
    }

    private async Task ClearRelationshipAsync(ModRelationshipKind kind)
    {
        if (selectedMod == null)
            return;

        string title = LabelFor(kind);
        bool confirm = await DialogService.ShowConfirmAsync(this, $"Clear all {title.ToLowerInvariant()} relationships for this mod?", $"Clear {title}");
        if (!confirm)
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
        list.RemoveAll(existing => string.Equals(existing, id, StringComparison.OrdinalIgnoreCase));
        CommitRulesChanged();
    }

    private void CommitRulesChanged()
    {
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

    private void Undo()
    {
        if (undoStack.Count == 0)
            return;

        applyingUndo = true;
        try
        {
            rules.Clear();
            foreach (KeyValuePair<string, ModRelationshipRule> kvp in undoStack.Pop())
                rules[kvp.Key] = kvp.Value.Clone();
            onRulesChanged?.Invoke(CloneRules(rules));
            RefreshEditor();
        }
        finally
        {
            applyingUndo = false;
        }
    }

    private void SortRulesWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Z && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            Undo();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
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
                rules.Remove(key);
        }
    }

    private static List<string> NormalizeList(IEnumerable<string> ids)
    {
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        List<string> normalized = new();
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

    private ValidationResult ValidateRules()
    {
        List<string> warnings = new();
        List<string> errors = new();
        Dictionary<string, HashSet<string>> graph = new(StringComparer.OrdinalIgnoreCase);

        foreach (KeyValuePair<string, ModRelationshipRule> kvp in rules)
        {
            string ownerId = kvp.Key;
            AddEdges(ownerId, kvp.Value.BeforeIds, ownerId, target => target, "before");
            AddEdges(ownerId, kvp.Value.AfterIds, ownerId, target => ownerId, "after", targetIsSource: true);
            AddEdges(ownerId, kvp.Value.RequiredIds, ownerId, target => ownerId, "required", targetIsSource: true);

            foreach (string id in kvp.Value.IncompatibleIds)
            {
                string target = id.Trim();
                if (string.Equals(ownerId, target, StringComparison.OrdinalIgnoreCase))
                    errors.Add($"{DisplayLabel(ownerId)} cannot be incompatible with itself.");
                else if (!modIdMap.ContainsKey(target))
                    warnings.Add($"{DisplayLabel(ownerId)} references missing mod {target}.");
            }
        }

        foreach (KeyValuePair<string, HashSet<string>> kvp in graph)
        {
            foreach (string target in kvp.Value)
            {
                if (WouldCreateCycle(graph, kvp.Key, target))
                {
                    errors.Add($"Circular dependency detected: {DisplayLabel(kvp.Key)} conflicts with {DisplayLabel(target)}.");
                    return BuildValidationResult(errors, warnings);
                }
            }
        }

        return BuildValidationResult(errors, warnings);

        void AddEdges(
            string ownerId,
            IEnumerable<string> ids,
            string source,
            Func<string, string> destinationFactory,
            string category,
            bool targetIsSource = false)
        {
            foreach (string rawId in ids)
            {
                string target = rawId.Trim();
                if (string.IsNullOrWhiteSpace(target))
                    continue;

                if (string.Equals(ownerId, target, StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add(category == "required"
                        ? $"{DisplayLabel(ownerId)} cannot require itself."
                        : $"{DisplayLabel(ownerId)} cannot reference itself.");
                    continue;
                }

                if (!modIdMap.ContainsKey(target))
                    warnings.Add($"{DisplayLabel(ownerId)} references missing mod {target}.");

                string from = targetIsSource ? target : source;
                string to = destinationFactory(target);
                if (!graph.TryGetValue(from, out HashSet<string>? destinations))
                {
                    destinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    graph[from] = destinations;
                }
                destinations.Add(to);
            }
        }
    }

    private static ValidationResult BuildValidationResult(List<string> errors, List<string> warnings)
    {
        if (errors.Count > 0)
            return new ValidationResult(true, warnings.Count > 0, string.Join(Environment.NewLine, errors.Concat(warnings).Distinct()));
        if (warnings.Count > 0)
            return new ValidationResult(false, true, string.Join(Environment.NewLine, warnings.Distinct()));
        return new ValidationResult(false, false, "No conflicts detected.");
    }

    private static bool WouldCreateCycle(Dictionary<string, HashSet<string>> graph, string fromId, string toId)
    {
        if (string.Equals(fromId, toId, StringComparison.OrdinalIgnoreCase))
            return true;

        HashSet<string> visited = new(StringComparer.OrdinalIgnoreCase);
        Stack<string> stack = new();
        stack.Push(toId);
        while (stack.Count > 0)
        {
            string current = stack.Pop();
            if (!visited.Add(current))
                continue;
            if (string.Equals(current, fromId, StringComparison.OrdinalIgnoreCase))
                return true;
            if (!graph.TryGetValue(current, out HashSet<string>? destinations))
                continue;
            foreach (string destination in destinations)
                stack.Push(destination);
        }
        return false;
    }

    private string DisplayLabel(string id)
    {
        return modIdMap.TryGetValue(id, out ModRefViewModel? vm)
            ? vm.DisplayName
            : id;
    }

    private static Dictionary<string, ModRelationshipRule> CloneRules(IDictionary<string, ModRelationshipRule> source)
    {
        Dictionary<string, ModRelationshipRule> clone = new(StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<string, ModRelationshipRule> kvp in source)
            clone[kvp.Key] = kvp.Value.Clone();
        return clone;
    }

    private static IBrush AccentBrush(ModRelationshipKind kind)
    {
        return kind switch
        {
            ModRelationshipKind.Before => BrushCache.GetBrush(Color.Parse("#3B82F6")),
            ModRelationshipKind.After => BrushCache.GetBrush(Color.Parse("#22C55E")),
            ModRelationshipKind.Required => BrushCache.GetBrush(Color.Parse("#EAB308")),
            ModRelationshipKind.Incompatible => BrushCache.GetBrush(Color.Parse("#EF4444")),
            _ => Brushes.Gray
        };
    }

    private static Image? IconFor(ModRelationshipKind kind)
    {
        return kind switch
        {
            ModRelationshipKind.Before => ImageSourceLoader.CreateAvaloniaImage("arrowUpIcon.svg"),
            ModRelationshipKind.After => ImageSourceLoader.CreateAvaloniaImage("arrowDownIcon.svg"),
            ModRelationshipKind.Required => ImageSourceLoader.CreateAvaloniaImage("linkIcon.svg"),
            ModRelationshipKind.Incompatible => ImageSourceLoader.CreateAvaloniaImage("cancelCircledIcon.svg"),
            _ => null
        };
    }

    private static string LabelFor(ModRelationshipKind kind)
    {
        return kind switch
        {
            ModRelationshipKind.Before => "Before",
            ModRelationshipKind.After => "After",
            ModRelationshipKind.Required => "Required",
            ModRelationshipKind.Incompatible => "Incompatible",
            _ => "Relationship"
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

    private static string DisplayNameFor(ModReference modref)
    {
        return string.IsNullOrWhiteSpace(modref.name) ? modref.ID : modref.name;
    }

    private void OpenRulesFile()
    {
        if (string.IsNullOrWhiteSpace(rulesFilePath) || !File.Exists(rulesFilePath))
            return;

        try
        {
            using Process? process = Process.Start(new ProcessStartInfo
            {
                FileName = rulesFilePath,
                UseShellExecute = true
            });
        }
        catch
        {
            // Ignore shell open failures.
        }
    }

    private readonly record struct ValidationResult(bool HasError, bool HasWarning, string Message);

    private async Task ClearAllRelationshipsAsync()
    {
        if (selectedMod == null)
        {
            return;
        }

        bool confirm = await DialogService.ShowConfirmAsync(this,
            "Clear all relationships (Before, After, Required, Incompatible) for this mod?",
            "Clear All Relationships");

        if (!confirm)
        {
            return;
        }

        PushUndo();

        foreach (ModRelationshipKind kind in Enum.GetValues<ModRelationshipKind>())
        {
            GetList(GetOrCreateRule(selectedMod.ModReference.ID), kind).Clear();
        }
        CommitRulesChanged();
    }
}
