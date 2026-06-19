using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace ModHearth.UI;

public partial class SortRulesWindow : Window
{
    private readonly ObservableCollection<ModRefViewModel> availableMods = new();
    private readonly ObservableCollection<ModRefViewModel> ruleMods = new();
    private readonly List<ModRefViewModel> masterRuleList = new();
    private readonly ObservableCollection<object> ruleJsonLines = new();

    private readonly Dictionary<string, ModRefViewModel> modKeyMap = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ModRefViewModel> modIdMap = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, RuleLineIndices> ruleLineIndices = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<RuleGap> ruleGaps = new(RuleGapComparer.Instance);
    private readonly HashSet<RuleGap> initialRuleGaps = new(RuleGapComparer.Instance);
    private readonly HashSet<RuleEdge> explicitRequiredRules = new(RuleEdgeComparer.Instance);
    private readonly HashSet<RuleEdge> initialExplicitRequiredRules = new(RuleEdgeComparer.Instance);
    private readonly HashSet<string> explicitRequiredIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> initialExplicitRequiredIds = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<RuleGap> redoRuleGaps = new(RuleGapComparer.Instance);
    private HashSet<RuleEdge> redoExplicitRequiredRules = new(RuleEdgeComparer.Instance);
    private HashSet<string> redoExplicitRequiredIds = new(StringComparer.OrdinalIgnoreCase);

    private readonly ModListDragDropController modListController;
    private readonly UndoRedoKeyHandler undoRedoHandler;
    private readonly string modsFolderPath;
    private readonly string vanillaFolderPath;
    private readonly Action<List<ModSortRule>>? onSave;
    private readonly DispatcherTimer searchDebounceTimer;

    private readonly List<string> initialRuleOrder = new();
    private bool changesMade;
    private bool redoAvailable;
    private bool isRedoing;
    private bool bypassUnsavedClosePrompt;
    private bool unsavedClosePromptInFlight;
    private List<string> redoRuleOrder = new();

    private string? lastJumpModId;
    private bool lastJumpWasAfter;

    public SortRulesWindow()
        : this(Array.Empty<ModSortRule>(), Array.Empty<ModReference>(), string.Empty, string.Empty, null)
    {
    }

    public SortRulesWindow(IEnumerable<ModSortRule> existingRules)
        : this(existingRules, Array.Empty<ModReference>(), string.Empty, string.Empty, null)
    {
    }

    public SortRulesWindow(IEnumerable<ModSortRule> existingRules, IEnumerable<ModReference> modRefs)
        : this(existingRules, modRefs, string.Empty, string.Empty, null)
    {
    }

    public SortRulesWindow(
        IEnumerable<ModSortRule> existingRules,
        IEnumerable<ModReference> modRefs,
        string? modsFolderPath,
        string? vanillaFolderPath,
        Action<List<ModSortRule>>? onSave = null)
    {
        InitializeComponent();
        WindowThemeManager.Register(this);
        this.modsFolderPath = modsFolderPath ?? string.Empty;
        this.vanillaFolderPath = vanillaFolderPath ?? string.Empty;
        this.onSave = onSave;

        BuildViewModels(existingRules ?? Array.Empty<ModSortRule>(), modRefs ?? Array.Empty<ModReference>());

        modTreeList.ItemsSource = availableMods;
        rulesList.ItemsSource = ruleMods;
        selectedRulesList.ItemsSource = ruleJsonLines;

        modListController = new ModListDragDropController(
            this,
            () => modIdMap.Values,
            key => modKeyMap.TryGetValue(key, out ModRefViewModel? vm) ? vm : null,
            vm => vm.DfMod.ToString());
        modListController.RegisterList(modTreeList, allowReorder: false);
        modListController.RegisterList(rulesList, allowReorder: true);
        modListController.Dropped += HandleDrop;

        modTreeList.SelectionChanged += ModlistSelectionChanged;
        rulesList.SelectionChanged += ModlistSelectionChanged;
        rulesList.AddHandler(InputElement.PointerPressedEvent, RulesListPointerPressed, RoutingStrategies.Bubble, true);

        modTreeSearchBar.HideFiltered = true;
        rulesSearchBar.HideFiltered = true;
        searchDebounceTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(140)
        };
        searchDebounceTimer.Tick += (_, _) =>
        {
            searchDebounceTimer.Stop();
            ApplySearchFilter();
        };
        modTreeSearchBar.SearchTextChanged += (_, _) => ScheduleSearchFilter();
        rulesSearchBar.SearchTextChanged += (_, _) => ScheduleSearchFilter();
        saveButton.Click += (_, _) => SaveRules();
        KeyDown += SortRulesWindowKeyDown;
        Closing += SortRulesWindowClosing;

        undoRedoHandler = new UndoRedoKeyHandler(
            () => changesMade,
            () => UndoChangesAsync(),
            () => redoAvailable,
            () => RedoChanges());
        undoRedoHandler.Attach(this);
        Closed += (_, _) => searchDebounceTimer.Stop();

        ApplySearchBarStyles();
        ApplySearchFilter();
        UpdateRuleReferenceOverlay();
        UpdateRuleGapVisuals();
        UpdateRulesJsonPreview();
    }

    private void ModlistSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListBox list)
            return;

        if (modListController.HandleSelectionChanged(list))
            return;

        if (sender == modTreeList && modTreeList.SelectedItems?.Count > 0)
            rulesList.SelectedItems?.Clear();
        if (sender == rulesList && rulesList.SelectedItems?.Count > 0)
            modTreeList.SelectedItems?.Clear();

        modListController.UpdateSelectionState(modTreeList);
        modListController.UpdateSelectionState(rulesList);
        UpdateRuleReferenceOverlay();

        if (sender == rulesList)
        {
            ModRefViewModel? added = e.AddedItems?.OfType<ModRefViewModel>().FirstOrDefault();
            if (added != null)
                JumpToRuleLine(added);
        }

        if (rulesList.SelectedItems == null || rulesList.SelectedItems.Count == 0)
            ClearJsonSelection();
    }

    private void RulesListPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not ListBox list)
            return;

        PointerPoint point = e.GetCurrentPoint(list);
        if (!point.Properties.IsLeftButtonPressed)
            return;

        if (IsPointerOverScrollBar(list, point.Position))
            return;

        ModRefViewModel? hit = GetItemAtPoint(list, point.Position);
        if (hit == null)
            return;

        if (list.SelectedItems != null && list.SelectedItems.Contains(hit))
            JumpToRuleLine(hit);
    }

    private void SortRuleContextMenuOpened(object? sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu menu)
            return;

        ContextMenuCoordinator.Activate(menu);

        ModRefViewModel? contextVm = ResolveContextMenuMod(menu);
        if (contextVm == null)
            return;

        ListBox? contextList = GetListForMod(contextVm);
        if (contextList != null)
        {
            modListController.TryRestoreContextSelection(contextList, contextVm);
            if (contextList.SelectedItems == null || contextList.SelectedItems.Count == 0 || !contextList.SelectedItems.Contains(contextVm))
            {
                contextList.SelectedItems?.Clear();
                contextList.SelectedItems?.Add(contextVm);
            }

            if (contextList == modTreeList)
                rulesList.SelectedItems?.Clear();
            else if (contextList == rulesList)
                modTreeList.SelectedItems?.Clear();

            modListController.UpdateSelectionState(modTreeList);
            modListController.UpdateSelectionState(rulesList);
            UpdateRuleReferenceOverlay();
        }

        ConfigureAddRequiredSubmenu(menu);
    }

    private ModRefViewModel? ResolveContextMenuMod(ContextMenu menu)
    {
        Control? placementControl = menu.PlacementTarget as Control;
        return placementControl?.DataContext as ModRefViewModel ??
               menu.DataContext as ModRefViewModel ??
               menu.Items.OfType<MenuItem>()
                   .Select(item => item.DataContext)
                   .OfType<ModRefViewModel>()
                   .FirstOrDefault() ??
               rulesList.SelectedItems?.OfType<ModRefViewModel>().FirstOrDefault() ??
               modTreeList.SelectedItems?.OfType<ModRefViewModel>().FirstOrDefault();
    }

    private void ConfigureAddRequiredSubmenu(ContextMenu menu)
    {
        MenuItem? addRequiredRoot = menu.Items
            .OfType<MenuItem>()
            .FirstOrDefault(item => string.Equals(item.Tag?.ToString(), "add-required-root", StringComparison.Ordinal));
        if (addRequiredRoot == null)
            return;

        List<MenuItem> items = modIdMap.Values
            .Where(candidate =>
            {
                string id = candidate.ModReference.ID?.Trim() ?? string.Empty;
                return !string.IsNullOrWhiteSpace(id);
            })
            .OrderBy(candidate => candidate.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(candidate => candidate.ModReference.ID, StringComparer.OrdinalIgnoreCase)
            .Select(candidate =>
            {
                string requiredId = candidate.ModReference.ID?.Trim() ?? string.Empty;
                MenuItem item = new MenuItem
                {
                    Header = $"{candidate.DisplayName} ({requiredId})",
                    Tag = new RequiredMenuPayload(requiredId)
                };
                item.Click += AddRequiredMenuItemClick;
                return item;
            })
            .ToList();

        if (items.Count == 0)
        {
            addRequiredRoot.ItemsSource = new[]
            {
                new MenuItem
                {
                    Header = "No mods available",
                    IsEnabled = false
                }
            };
            addRequiredRoot.IsEnabled = false;
            return;
        }

        addRequiredRoot.IsEnabled = true;
        addRequiredRoot.ItemsSource = items;
    }

    private void AddRequiredMenuItemClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menuItem)
            return;
        if (menuItem.Tag is not RequiredMenuPayload payload)
            return;

        if (!modIdMap.TryGetValue(payload.RequiresId, out ModRefViewModel? requiredVm) || requiredVm == null)
            return;

        AddRequiredRule(requiredVm);
    }

    private void AddRequiredRule(ModRefViewModel requiredVm)
    {
        string requiredId = requiredVm.ModReference.ID?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(requiredId))
            return;

        EnsureModPresentInRuleList(requiredVm);
        explicitRequiredIds.Add(requiredId);

        modTreeList.SelectedItems?.Clear();
        rulesList.SelectedItems?.Clear();
        rulesList.SelectedItems?.Add(requiredVm);
        rulesList.ScrollIntoView(requiredVm);
        modListController.UpdateSelectionState(modTreeList);
        modListController.UpdateSelectionState(rulesList);

        NormalizeGaps();
        NormalizeExplicitRules();
        NormalizeExplicitRequiredIds();
        MarkChanged();
        RefreshRuleState(true);
    }

    private void EnsureModPresentInRuleList(ModRefViewModel vm, int? preferredIndex = null)
    {
        if (ruleMods.Contains(vm))
            return;

        availableMods.Remove(vm);
        int index = preferredIndex.HasValue
            ? Math.Max(0, Math.Min(preferredIndex.Value, ruleMods.Count))
            : ruleMods.Count;
        ruleMods.Insert(index, vm);
        ApplySearchFilter();
    }

    private void UpdateRuleReferenceOverlay()
    {
        foreach (ModRefViewModel vm in availableMods)
            vm.ReferenceOverlay = ModRefViewModel.ReferenceOverlayKind.None;

        foreach (ModRefViewModel vm in ruleMods)
            vm.ReferenceOverlay = ModRefViewModel.ReferenceOverlayKind.None;

        List<ModRefViewModel> selected = rulesList.SelectedItems?.Cast<ModRefViewModel>().ToList()
            ?? new List<ModRefViewModel>();
        if (selected.Count == 0)
            return;

        HashSet<ModRefViewModel> selectedSet = new HashSet<ModRefViewModel>(selected);
        int segmentStart = 0;
        for (int i = 0; i < ruleMods.Count; i++)
        {
            bool segmentEndsHere = (i == ruleMods.Count - 1) ||
                                   HasGapBetween(ruleMods[i].ModReference.ID, ruleMods[i + 1].ModReference.ID);
            if (!segmentEndsHere)
                continue;

            int segmentEnd = i;
            ApplyReferenceOverlayToSegment(segmentStart, segmentEnd, selectedSet);
            segmentStart = i + 1;
        }
    }

    private void ApplyReferenceOverlayToSegment(
        int segmentStart,
        int segmentEnd,
        HashSet<ModRefViewModel> selectedSet)
    {
        int firstSelected = -1;
        int lastSelected = -1;
        for (int i = segmentStart; i <= segmentEnd; i++)
        {
            if (!selectedSet.Contains(ruleMods[i]))
                continue;
            if (firstSelected < 0)
                firstSelected = i;
            lastSelected = i;
        }

        if (firstSelected < 0)
            return;

        for (int i = segmentStart; i < firstSelected; i++)
            ruleMods[i].ReferenceOverlay = ModRefViewModel.ReferenceOverlayKind.AboveSelection;
        for (int i = lastSelected + 1; i <= segmentEnd; i++)
            ruleMods[i].ReferenceOverlay = ModRefViewModel.ReferenceOverlayKind.BelowSelection;
    }

    private void HandleDrop(ModListDropContext context)
    {
        if (context.Items.Count == 0)
            return;

        bool gapDrop = context.GapDrop;
        bool sourceRight = context.SourceList == rulesList;
        if (context.SourceList == null)
            sourceRight = context.Items.Any(vm => ruleMods.Contains(vm));

        bool destinationRight = context.DestinationList == rulesList;

        if (!sourceRight && !destinationRight)
            return;

        if (sourceRight && destinationRight)
        {
            ReorderRuleMods(context.Items, context.InsertIndex);
            ApplyGapFromDrop(context, gapDrop);
            MarkChanged();
            SelectItemsInList(rulesList, context.Items);
            RefreshRuleState(true);
            return;
        }

        if (!sourceRight && destinationRight)
        {
            MoveToRuleMods(context.Items, context.InsertIndex);
            ApplyGapFromDrop(context, gapDrop);
            MarkChanged();
            SelectItemsInList(rulesList, context.Items);
            RefreshRuleState(true);
            return;
        }

        if (sourceRight && !destinationRight)
        {
            MoveToAvailableMods(context.Items);
            NormalizeGaps();
            MarkChanged();
            SelectItemsInList(modTreeList, context.Items);
            RefreshRuleState(true);
        }
    }

    private void MoveToRuleMods(List<ModRefViewModel> items, int insertIndex)
    {
        // Map insert index from filtered ruleMods to masterRuleList
        int masterIdx = MapFilteredToMasterIndex(ruleMods, masterRuleList, insertIndex);

        List<ModRefViewModel> unique = Deduplicate(items);
        if (unique.Count == 0)
            return;

        foreach (ModRefViewModel vm in unique)
            availableMods.Remove(vm);

        int clamped = Math.Max(0, Math.Min(masterIdx, masterRuleList.Count));
        for (int i = 0; i < unique.Count; i++)
            masterRuleList.Insert(clamped + i, unique[i]);

        ApplySearchFilter();
    }

    private void MoveToAvailableMods(List<ModRefViewModel> items)
    {
        List<ModRefViewModel> unique = Deduplicate(items);
        if (unique.Count == 0)
            return;

        foreach (ModRefViewModel vm in unique)
            masterRuleList.Remove(vm);

        ApplySearchFilter();
    }

    private void ReorderRuleMods(List<ModRefViewModel> items, int insertIndex)
    {
        // Map insert index from filtered ruleMods to masterRuleList
        int masterIdx = MapFilteredToMasterIndex(ruleMods, masterRuleList, insertIndex);

        HashSet<ModRefViewModel> selectedSet = new HashSet<ModRefViewModel>(items);
        List<ModRefViewModel> selectedInOrder = masterRuleList.Where(m => selectedSet.Contains(m)).ToList();
        if (selectedInOrder.Count == 0)
            return;

        int clampedIndex = Math.Max(0, Math.Min(masterIdx, masterRuleList.Count));
        int selectedBefore = masterRuleList.Take(clampedIndex).Count(m => selectedSet.Contains(m));
        int targetIndex = clampedIndex - selectedBefore;

        List<ModRefViewModel> remaining = masterRuleList.Where(m => !selectedSet.Contains(m)).ToList();
        targetIndex = Math.Max(0, Math.Min(targetIndex, remaining.Count));

        List<ModRefViewModel> newList = new List<ModRefViewModel>();
        newList.AddRange(remaining.Take(targetIndex));
        newList.AddRange(selectedInOrder);
        newList.AddRange(remaining.Skip(targetIndex));

        masterRuleList.Clear();
        masterRuleList.AddRange(newList);
    }

    private void SelectItemsInList(ListBox list, IEnumerable<ModRefViewModel> items)
    {
        if (list.SelectedItems == null)
            return;

        list.SelectedItems.Clear();
        foreach (ModRefViewModel vm in items)
            list.SelectedItems.Add(vm);

        modListController.UpdateSelectionState(list);
    }

    private void RefreshRuleState(bool rulesChanged)
    {
        if (rulesChanged)
        {
            NormalizeExplicitRules();
            NormalizeExplicitRequiredIds();
        }
        UpdateRuleReferenceOverlay();
        UpdateRuleGapVisuals();
        if (rulesChanged)
            UpdateRulesJsonPreview();
    }


    private void UpdateRuleGapVisuals()
    {
        foreach (ModRefViewModel vm in modIdMap.Values)
            vm.RuleGapMargin = new Thickness(0);

        for (int i = 0; i < masterRuleList.Count; i++)
        {
            bool hasGapAbove = i > 0 &&
                               HasGapBetween(masterRuleList[i - 1].ModReference.ID, masterRuleList[i].ModReference.ID);
            bool hasGapBelow = i < masterRuleList.Count - 1 &&
                               HasGapBetween(masterRuleList[i].ModReference.ID, masterRuleList[i + 1].ModReference.ID);

            double top = hasGapAbove ? 12 : 0;
            double bottom = hasGapBelow ? 12 : 0;
            masterRuleList[i].RuleGapMargin = new Thickness(0, top, 0, bottom);
        }
    }

    private void UpdateRulesJsonPreview()
    {
        ruleLineIndices.Clear();
        ruleJsonLines.Clear();

        IBrush gapBrush = GetGapLineBrush();
        List<PreviewRuleToken> tokens = BuildPreviewTokens(gapBrush);
        int totalRules = tokens.Count(token => token.Edge != null || !string.IsNullOrWhiteSpace(token.RequiresId));
        if (totalRules == 0)
        {
            ruleJsonLines.Add("[]");
            SyncJsonSelectionToRulesSelection();
            return;
        }

        ruleJsonLines.Add("[");
        int remainingRules = totalRules;
        foreach (PreviewRuleToken token in tokens)
        {
            if (token.Edge == null && string.IsNullOrWhiteSpace(token.RequiresId))
            {
                ruleJsonLines.Add(new RuleGapMarker(token.MarkerBrush ?? gapBrush));
                continue;
            }

            remainingRules--;
            bool isLast = remainingRules == 0;

            ruleJsonLines.Add("  {");
            if (token.Edge != null)
            {
                string beforeId = token.Edge.Value.BeforeId;
                string afterId = token.Edge.Value.AfterId;
                int beforeIndex = ruleJsonLines.Count;
                ruleJsonLines.Add($"    \"BeforeId\": {JsonSerializer.Serialize(beforeId)},");
                int afterIndex = ruleJsonLines.Count;
                ruleJsonLines.Add($"    \"AfterId\": {JsonSerializer.Serialize(afterId)}");

                RegisterRuleLine(beforeId, beforeIndex, RuleLineType.Before);
                RegisterRuleLine(afterId, afterIndex, RuleLineType.After);
            }
            else
            {
                string requiresId = token.RequiresId.Trim();
                int requiresIndex = ruleJsonLines.Count;
                ruleJsonLines.Add($"    \"RequiresId\": {JsonSerializer.Serialize(requiresId)}");
                RegisterRuleLine(requiresId, requiresIndex, RuleLineType.Requires);
            }

            ruleJsonLines.Add(isLast ? "  }" : "  },");
        }
        ruleJsonLines.Add("]");

        SyncJsonSelectionToRulesSelection();
    }

    private static IBrush GetGapLineBrush()
    {
        if (Style.instance != null)
            return new SolidColorBrush(Style.instance.textColor.ToAvaloniaColor());
        return Brushes.White;
    }

    private List<PreviewRuleToken> BuildPreviewTokens(IBrush gapBrush)
    {
        List<PreviewRuleToken> tokens = new List<PreviewRuleToken>();
        HashSet<RuleEdge> adjacencyEdges = new HashSet<RuleEdge>(RuleEdgeComparer.Instance);
        HashSet<string> ruleIds = new HashSet<string>(
            masterRuleList.Select(vm => vm.ModReference.ID?.Trim() ?? string.Empty)
                .Where(id => !string.IsNullOrWhiteSpace(id)),
            StringComparer.OrdinalIgnoreCase);

        int pairCount = Math.Max(0, masterRuleList.Count - 1);
        for (int i = 0; i < pairCount; i++)
        {
            RuleEdge edge = CreateEdge(masterRuleList[i].ModReference.ID, masterRuleList[i + 1].ModReference.ID);
            if (!IsValidEdge(edge))
                continue;

            if (HasGapBetween(edge.BeforeId, edge.AfterId))
            {
                tokens.Add(PreviewRuleToken.Marker(gapBrush));
                continue;
            }

            adjacencyEdges.Add(edge);
            tokens.Add(PreviewRuleToken.EdgeToken(edge));
        }

        List<RuleEdge> extraEdges = explicitRequiredRules
            .Where(IsValidEdge)
            .Where(edge => ruleIds.Contains(edge.BeforeId) && ruleIds.Contains(edge.AfterId))
            .Where(edge => !adjacencyEdges.Contains(edge))
            .OrderBy(edge => edge.BeforeId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(edge => edge.AfterId, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (extraEdges.Count > 0)
        {
            if (tokens.Count > 0)
                tokens.Add(PreviewRuleToken.Marker(gapBrush));
            tokens.AddRange(extraEdges.Select(PreviewRuleToken.EdgeToken));
        }

        List<string> requiredIds = explicitRequiredIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Where(ruleIds.Contains)
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (requiredIds.Count > 0)
        {
            if (tokens.Count > 0)
                tokens.Add(PreviewRuleToken.Marker(gapBrush));
            tokens.AddRange(requiredIds.Select(PreviewRuleToken.RequiresToken));
        }

        return tokens;
    }

    private void ApplyGapFromDrop(ModListDropContext context, bool addGap)
    {
        if (context.DestinationList != rulesList)
            return;

        List<ModRefViewModel> inserted = context.Items.Where(masterRuleList.Contains).ToList();
        if (inserted.Count == 0)
        {
            NormalizeGaps();
            return;
        }

        int minIndex = masterRuleList.Count;
        int maxIndex = -1;
        foreach (ModRefViewModel vm in inserted)
        {
            int index = masterRuleList.IndexOf(vm);
            if (index < 0)
                continue;
            if (index < minIndex)
                minIndex = index;
            if (index > maxIndex)
                maxIndex = index;
        }

        if (minIndex > maxIndex)
        {
            NormalizeGaps();
            return;
        }

        int aboveIndex = minIndex - 1;
        if (aboveIndex >= 0)
        {
            ModRefViewModel before = masterRuleList[aboveIndex];
            ModRefViewModel after = masterRuleList[minIndex];
            SetGapBetween(before.ModReference.ID, after.ModReference.ID, addGap);
        }

        int belowIndex = maxIndex + 1;
        if (belowIndex < masterRuleList.Count)
        {
            ModRefViewModel before = masterRuleList[maxIndex];
            ModRefViewModel after = masterRuleList[belowIndex];
            SetGapBetween(before.ModReference.ID, after.ModReference.ID, addGap);
        }

        NormalizeGaps();
    }

    private void NormalizeGaps()
    {
        if (masterRuleList.Count < 2)
        {
            ruleGaps.Clear();
            return;
        }

        HashSet<RuleGap> valid = new HashSet<RuleGap>(RuleGapComparer.Instance);
        for (int i = 0; i < masterRuleList.Count - 1; i++)
        {
            RuleGap gap = CreateGap(masterRuleList[i].ModReference.ID, masterRuleList[i + 1].ModReference.ID);
            if (!IsValidGap(gap))
                continue;
            if (ruleGaps.Contains(gap))
                valid.Add(gap);
        }

        ruleGaps.Clear();
        foreach (RuleGap gap in valid)
            ruleGaps.Add(gap);
    }

    private bool HasGapBetween(string beforeId, string afterId)
    {
        RuleGap gap = CreateGap(beforeId, afterId);
        return IsValidGap(gap) && ruleGaps.Contains(gap);
    }

    private void SetGapBetween(string beforeId, string afterId, bool addGap)
    {
        RuleGap gap = CreateGap(beforeId, afterId);
        if (!IsValidGap(gap))
            return;

        if (addGap)
            ruleGaps.Add(gap);
        else
            ruleGaps.Remove(gap);
    }

    private static RuleGap CreateGap(string? beforeId, string? afterId)
    {
        string before = beforeId?.Trim() ?? string.Empty;
        string after = afterId?.Trim() ?? string.Empty;
        return new RuleGap(before, after);
    }

    private static bool IsValidGap(RuleGap gap)
    {
        return !string.IsNullOrWhiteSpace(gap.BeforeId) &&
               !string.IsNullOrWhiteSpace(gap.AfterId) &&
               !string.Equals(gap.BeforeId, gap.AfterId, StringComparison.OrdinalIgnoreCase);
    }

    private void RegisterRuleLine(string? id, int index, RuleLineType lineType)
    {
        string trimmed = id?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmed))
            return;

        if (!ruleLineIndices.TryGetValue(trimmed, out RuleLineIndices? indices))
        {
            indices = new RuleLineIndices();
            ruleLineIndices[trimmed] = indices;
        }

        if (lineType == RuleLineType.Before)
        {
            if (indices.BeforeIndex == null)
                indices.BeforeIndex = index;
        }
        else if (lineType == RuleLineType.After)
        {
            if (indices.AfterIndex == null)
                indices.AfterIndex = index;
        }
        else if (indices.RequiresIndex == null)
        {
            indices.RequiresIndex = index;
        }
    }

    private void SyncJsonSelectionToRulesSelection()
    {
        List<ModRefViewModel> selected = rulesList.SelectedItems?.Cast<ModRefViewModel>().ToList()
            ?? new List<ModRefViewModel>();
        if (selected.Count == 0)
        {
            ClearJsonSelection();
            return;
        }

        ModRefViewModel target = selected[0];
        bool? preferAfter = null;

        if (!string.IsNullOrWhiteSpace(lastJumpModId))
        {
            ModRefViewModel? matching = selected.FirstOrDefault(vm =>
                string.Equals(vm.ModReference.ID, lastJumpModId, StringComparison.OrdinalIgnoreCase));
            if (matching != null)
            {
                target = matching;
                preferAfter = lastJumpWasAfter;
            }
        }

        JumpToRuleLine(target, preferAfter);
    }

    private void JumpToRuleLine(ModRefViewModel vm, bool? preferAfterOverride = null)
    {
        string id = vm.ModReference.ID?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(id))
        {
            ClearJsonSelection();
            return;
        }

        bool preferAfter;
        if (preferAfterOverride.HasValue)
        {
            preferAfter = preferAfterOverride.Value;
        }
        else if (!string.IsNullOrWhiteSpace(lastJumpModId) &&
                 string.Equals(lastJumpModId, id, StringComparison.OrdinalIgnoreCase))
        {
            preferAfter = !lastJumpWasAfter;
        }
        else
        {
            preferAfter = false;
        }

        if (TrySelectRuleLine(id, preferAfter, out bool usedAfter))
        {
            lastJumpModId = id;
            lastJumpWasAfter = usedAfter;
            return;
        }

        // The selected mod can be intentionally isolated by gaps, so no rule line exists.
        ClearJsonSelection();
        lastJumpModId = id;
        lastJumpWasAfter = false;
    }

    private bool TrySelectRuleLine(string id, bool preferAfter, out bool usedAfter)
    {
        usedAfter = preferAfter;
        if (!ruleLineIndices.TryGetValue(id, out RuleLineIndices? indices))
            return false;

        int? index = preferAfter ? indices.AfterIndex : indices.BeforeIndex;
        if (index == null)
        {
            index = preferAfter ? indices.BeforeIndex : indices.AfterIndex;
            usedAfter = !preferAfter;
        }
        if (index == null)
        {
            index = indices.RequiresIndex;
            usedAfter = false;
        }

        if (index == null || index.Value < 0 || index.Value >= ruleJsonLines.Count)
            return false;

        selectedRulesList.SelectedIndex = index.Value;
        if (selectedRulesList.SelectedItem != null)
            selectedRulesList.ScrollIntoView(selectedRulesList.SelectedItem);
        return true;
    }

    private void ClearJsonSelection()
    {
        selectedRulesList.SelectedItem = null;
        lastJumpModId = null;
        lastJumpWasAfter = false;
    }

    private void SortRulesWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled || e.Key != Key.Escape || e.KeyModifiers != KeyModifiers.None)
            return;

        if (!HandleEscapeKey(e.Source))
            return;

        e.Handled = true;
    }

    private bool HandleEscapeKey(object? source)
    {
        bool handled = false;

        if ((modTreeList.SelectedItems?.Count ?? 0) > 0 || (rulesList.SelectedItems?.Count ?? 0) > 0)
        {
            modTreeList.SelectedItems?.Clear();
            rulesList.SelectedItems?.Clear();
            modListController.UpdateSelectionState(modTreeList);
            modListController.UpdateSelectionState(rulesList);
            UpdateRuleReferenceOverlay();
            ClearJsonSelection();
            handled = true;
        }

        if (modTreeSearchBar.ClearSearchSelection())
            handled = true;
        if (rulesSearchBar.ClearSearchSelection())
            handled = true;

        if (source is Control control && control.FindAncestorOfType<ModSearchBar>() != null)
        {
            Focus();
            handled = true;
        }

        return handled;
    }

    private void BuildViewModels(IEnumerable<ModSortRule> existingRules, IEnumerable<ModReference> modRefs)
    {
        modKeyMap.Clear();
        modIdMap.Clear();
        availableMods.Clear();
        ruleMods.Clear();

        foreach (ModReference modref in modRefs)
        {
            if (modref == null)
                continue;
            string id = modref.ID?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(id))
                continue;
            if (modIdMap.ContainsKey(id))
                continue;

            ModRefViewModel vm = new ModRefViewModel(modref);
            (bool isVanilla, bool isLocal, bool isSteam) = ModSourceClassifier.Classify(
                modref,
                modsFolderPath,
                vanillaFolderPath);
            vm.IsVanillaModSource = isVanilla;
            vm.IsLocalModSource = isLocal;
            vm.IsSteamModSource = isSteam;
            vm.RefreshStyle();
            modIdMap[id] = vm;
            modKeyMap[vm.DfMod.ToString()] = vm;
        }

        foreach (ModSortRule rule in existingRules)
        {
            if (rule == null)
                continue;
            AddPlaceholderIfMissing(rule.BeforeId);
            AddPlaceholderIfMissing(rule.AfterId);
            AddPlaceholderIfMissing(rule.RequiresId);
        }

        masterRuleList.Clear();
        List<string> orderedRuleIds = BuildRuleOrder(existingRules, modIdMap.Keys);
        foreach (string id in orderedRuleIds)
        {
            if (modIdMap.TryGetValue(id, out ModRefViewModel? vm))
                masterRuleList.Add(vm);
        }

        ReplaceCollection(availableMods, GetMasterAvailable().ToList());
        ReplaceCollection(ruleMods, masterRuleList);

        initialRuleOrder.Clear();
        initialRuleOrder.AddRange(masterRuleList
            .Select(vm => vm.ModReference.ID)
            .Where(id => !string.IsNullOrWhiteSpace(id)));

        InitializeRuleGaps(existingRules);
    }

    private void InitializeRuleGaps(IEnumerable<ModSortRule> existingRules)
    {
        ruleGaps.Clear();
        initialRuleGaps.Clear();
        redoRuleGaps.Clear();
        explicitRequiredRules.Clear();
        initialExplicitRequiredRules.Clear();
        redoExplicitRequiredRules.Clear();
        explicitRequiredIds.Clear();
        initialExplicitRequiredIds.Clear();
        redoExplicitRequiredIds.Clear();

        HashSet<RuleEdge> explicitRules = new HashSet<RuleEdge>(RuleEdgeComparer.Instance);
        foreach (ModSortRule rule in existingRules ?? Array.Empty<ModSortRule>())
        {
            if (rule == null)
                continue;

            string requiredId = rule.RequiresId?.Trim() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(requiredId))
                explicitRequiredIds.Add(requiredId);

            RuleEdge edge = CreateEdge(rule.BeforeId, rule.AfterId);
            if (!IsValidEdge(edge))
                continue;
            explicitRules.Add(edge);
        }

        for (int i = 0; i < masterRuleList.Count - 1; i++)
        {
            RuleEdge edge = CreateEdge(masterRuleList[i].ModReference.ID, masterRuleList[i + 1].ModReference.ID);
            if (!IsValidEdge(edge))
                continue;
            if (!explicitRules.Contains(edge))
                ruleGaps.Add(CreateGap(edge.BeforeId, edge.AfterId));
        }

        foreach (RuleEdge edge in explicitRules)
            explicitRequiredRules.Add(edge);

        NormalizeGaps();
        NormalizeExplicitRules();
        NormalizeExplicitRequiredIds();

        foreach (RuleGap gap in ruleGaps)
            initialRuleGaps.Add(gap);
        foreach (RuleEdge edge in explicitRequiredRules)
            initialExplicitRequiredRules.Add(edge);
        foreach (string requiredId in explicitRequiredIds)
            initialExplicitRequiredIds.Add(requiredId);
    }

    private void AddPlaceholderIfMissing(string? id)
    {
        string trimmed = id?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmed))
            return;
        if (modIdMap.ContainsKey(trimmed))
            return;

        ModReference placeholder = new ModReference(
            trimmed,
            "0",
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            trimmed,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty);

        ModRefViewModel vm = new ModRefViewModel(placeholder);
        (bool isVanilla, bool isLocal, bool isSteam) = ModSourceClassifier.Classify(
            placeholder,
            modsFolderPath,
            vanillaFolderPath);
        vm.IsVanillaModSource = isVanilla;
        vm.IsLocalModSource = isLocal;
        vm.IsSteamModSource = isSteam;
        vm.RefreshStyle();
        modIdMap[trimmed] = vm;
        modKeyMap[vm.DfMod.ToString()] = vm;
    }

    private static List<string> BuildRuleOrder(IEnumerable<ModSortRule> rules, IEnumerable<string> candidateIds)
    {
        HashSet<string> candidates = new HashSet<string>(candidateIds, StringComparer.OrdinalIgnoreCase);
        HashSet<string> ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (ModSortRule rule in rules)
        {
            if (rule == null)
                continue;

            string requires = rule.RequiresId?.Trim() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(requires) && candidates.Contains(requires))
                ids.Add(requires);

            string before = rule.BeforeId?.Trim() ?? string.Empty;
            string after = rule.AfterId?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(before) || string.IsNullOrWhiteSpace(after))
                continue;
            if (string.Equals(before, after, StringComparison.OrdinalIgnoreCase))
                continue;
            if (!candidates.Contains(before) || !candidates.Contains(after))
                continue;
            ids.Add(before);
            ids.Add(after);
        }

        if (ids.Count == 0)
            return new List<string>();

        Dictionary<string, List<string>> edges = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, int> indegree = new(StringComparer.OrdinalIgnoreCase);
        foreach (string id in ids)
        {
            edges[id] = new List<string>();
            indegree[id] = 0;
        }

        foreach (ModSortRule rule in rules)
        {
            if (rule == null)
                continue;
            string before = rule.BeforeId?.Trim() ?? string.Empty;
            string after = rule.AfterId?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(before) || string.IsNullOrWhiteSpace(after))
                continue;
            if (string.Equals(before, after, StringComparison.OrdinalIgnoreCase))
                continue;
            if (!edges.ContainsKey(before) || !edges.ContainsKey(after))
                continue;
            if (edges[before].Contains(after))
                continue;
            edges[before].Add(after);
            indegree[after]++;
        }

        List<string> available = indegree.Where(kv => kv.Value == 0)
            .Select(kv => kv.Key)
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToList();

        List<string> ordered = new List<string>();
        while (available.Count > 0)
        {
            string next = available[0];
            available.RemoveAt(0);
            ordered.Add(next);
            foreach (string dest in edges[next])
            {
                indegree[dest]--;
                if (indegree[dest] == 0)
                {
                    available.Add(dest);
                    available.Sort(StringComparer.OrdinalIgnoreCase);
                }
            }
        }

        if (ordered.Count != ids.Count)
        {
            return ids.OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToList();
        }

        return ordered;
    }

    private IEnumerable<ModRefViewModel> GetMasterAvailable()
    {
        HashSet<ModRefViewModel> inRules = new HashSet<ModRefViewModel>(masterRuleList);
        return modIdMap.Values
            .Where(vm => !inRules.Contains(vm))
            .OrderBy(vm => vm.ModReference.ID, StringComparer.OrdinalIgnoreCase);
    }

    private static int MapFilteredToMasterIndex(
        IList<ModRefViewModel> filtered,
        IList<ModRefViewModel> master,
        int filteredIndex)
    {
        if (filteredIndex <= 0) return 0;
        if (filteredIndex >= filtered.Count)
        {
            ModRefViewModel last = filtered[^1];
            return master.IndexOf(last) + 1;
        }
        return master.IndexOf(filtered[filteredIndex]);
    }

    private void ApplySearchFilter()
    {
        ApplyFilterFlags(
             availableMods,
             GetMasterAvailable(),
             modTreeSearchBar.Text,
             modTreeSearchBar.SearchMode,
             modTreeSearchBar.HideFiltered,
             modTreeList);
        ApplyFilterFlags(
            ruleMods,
            masterRuleList,
            rulesSearchBar.Text,
            rulesSearchBar.SearchMode,
            rulesSearchBar.HideFiltered,
            rulesList);
    }

    private void ScheduleSearchFilter()
    {
        if (modIdMap.Count > 10)
        {
            TempSearchLog("ScheduleSearchFilter no-debounce -> immediate");
            ApplySearchFilter();
            return;
        }

        searchDebounceTimer.Stop();
        searchDebounceTimer.Start();
    }

    private void ApplySearchFilterImmediately()
    {
        TempSearchLog("ApplySearchFilterImmediately (stopping timer)");
        searchDebounceTimer.Stop();
        ApplySearchFilter();
    }

    private void ApplyFilterFlags(
        ObservableCollection<ModRefViewModel> targetCollection,
        IEnumerable<ModRefViewModel> source,
        string filter,
        SearchFilterMode searchMode,
        bool hideFiltered,
        ListBox list)
    {
        string trimmed = filter?.Trim() ?? string.Empty;
        bool hasFilter = !string.IsNullOrWhiteSpace(trimmed);
        List<ModRefViewModel> ordered = new List<ModRefViewModel>();

        foreach (ModRefViewModel vm in source)
        {
            bool match = !hasFilter || vm.MatchesFilter(trimmed, searchMode);
            vm.IsFilteredOut = hasFilter && !match;
            vm.IsVisible = !hideFiltered || match;
            ordered.Add(vm);
        }

        List<ModRefViewModel> displayItems = hideFiltered
            ? ordered.Where(vm => vm.IsVisible).ToList()
            : ordered;

        ReplaceCollection(targetCollection, displayItems);
        DropNonDisplayedSelections(list, displayItems);
    }

    private void DropNonDisplayedSelections(ListBox list, IReadOnlyCollection<ModRefViewModel> displayItems)
    {
        if (list.SelectedItems == null || list.SelectedItems.Count == 0)
            return;

        HashSet<ModRefViewModel> visibleSet = new HashSet<ModRefViewModel>(displayItems);
        List<ModRefViewModel> retained = list.SelectedItems.OfType<ModRefViewModel>().Where(visibleSet.Contains).ToList();
        if (retained.Count == list.SelectedItems.Count) return;

        list.SelectedItems.Clear();
        foreach (ModRefViewModel vm in retained) list.SelectedItems.Add(vm);
        modListController.UpdateSelectionState(list);
    }

    private void ApplySearchBarStyles()
    {
        if (Style.instance == null)
            return;

        modTreeSearchBar.ApplyStyle(Style.instance);
        rulesSearchBar.ApplyStyle(Style.instance);
    }

    private void SaveRules(bool closeAfterSave = false)
    {
        List<ModSortRule> rules = BuildCurrentRules();
        if (onSave == null)
        {
            changesMade = false;
            bypassUnsavedClosePrompt = true;
            try
            {
                Close(rules);
            }
            finally
            {
                bypassUnsavedClosePrompt = false;
            }
            return;
        }

        onSave(rules);
        CommitCurrentStateAsSaved();
        if (!closeAfterSave)
            return;

        bypassUnsavedClosePrompt = true;
        try
        {
            Close();
        }
        finally
        {
            bypassUnsavedClosePrompt = false;
        }
    }

    private async void SortRulesWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (bypassUnsavedClosePrompt || !changesMade)
            return;

        e.Cancel = true;
        if (unsavedClosePromptInFlight)
            return;

        unsavedClosePromptInFlight = true;
        try
        {
            UnsavedChangesChoice choice = await DialogService.ShowUnsavedChangesPromptAsync(
                this,
                "Sort Rules",
                "exit");
            if (choice == UnsavedChangesChoice.Cancel)
                return;

            if (choice == UnsavedChangesChoice.Save)
                SaveRules(closeAfterSave: true);
            else
            {
                changesMade = false;
                bypassUnsavedClosePrompt = true;
                try
                {
                    Close();
                }
                finally
                {
                    bypassUnsavedClosePrompt = false;
                }
            }
        }
        finally
        {
            unsavedClosePromptInFlight = false;
        }
    }

    private List<ModSortRule> BuildCurrentRules()
    {
        HashSet<string> ruleIds = new HashSet<string>(
            masterRuleList.Select(vm => vm.ModReference.ID?.Trim() ?? string.Empty)
                .Where(id => !string.IsNullOrWhiteSpace(id)),
            StringComparer.OrdinalIgnoreCase);

        List<RuleCandidate> candidates = new List<RuleCandidate>();
        int order = 0;

        for (int i = 0; i < ruleMods.Count - 1; i++)
        {
            string before = ruleMods[i].ModReference.ID?.Trim() ?? string.Empty;
            string after = ruleMods[i + 1].ModReference.ID?.Trim() ?? string.Empty;
            RuleEdge edge = CreateEdge(before, after);
            if (!IsValidEdge(edge))
                continue;
            if (HasGapBetween(before, after))
                continue;

            candidates.Add(new RuleCandidate(edge, 1, order++));
        }

        foreach (RuleEdge edge in explicitRequiredRules
            .OrderBy(item => item.BeforeId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.AfterId, StringComparer.OrdinalIgnoreCase))
        {
            if (!IsValidEdge(edge))
                continue;
            if (!ruleIds.Contains(edge.BeforeId) || !ruleIds.Contains(edge.AfterId))
                continue;

            candidates.Add(new RuleCandidate(edge, 2, order++));
        }

        List<ModSortRule> resolved = ResolveConflicts(candidates);

        foreach (string requiredId in explicitRequiredIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase))
        {
            if (!ruleIds.Contains(requiredId))
                continue;

            resolved.Add(new ModSortRule
            {
                RequiresId = requiredId
            });
        }

        return resolved;
    }

    private Task UndoChangesAsync()
    {
        UndoChanges();
        return Task.CompletedTask;
    }

    private void UndoChanges()
    {
        if (!changesMade)
            return;

        redoRuleOrder = GetRuleOrder();
        redoRuleGaps = new HashSet<RuleGap>(ruleGaps, RuleGapComparer.Instance);
        redoExplicitRequiredRules = new HashSet<RuleEdge>(explicitRequiredRules, RuleEdgeComparer.Instance);
        redoExplicitRequiredIds = new HashSet<string>(explicitRequiredIds, StringComparer.OrdinalIgnoreCase);
        redoAvailable = true;

        isRedoing = true;
        SetRuleOrder(initialRuleOrder);
        ruleGaps.Clear();
        foreach (RuleGap gap in initialRuleGaps)
            ruleGaps.Add(gap);
        explicitRequiredRules.Clear();
        foreach (RuleEdge edge in initialExplicitRequiredRules)
            explicitRequiredRules.Add(edge);
        explicitRequiredIds.Clear();
        foreach (string requiredId in initialExplicitRequiredIds)
            explicitRequiredIds.Add(requiredId);
        NormalizeGaps();
        NormalizeExplicitRules();
        NormalizeExplicitRequiredIds();
        isRedoing = false;

        changesMade = false;
        RefreshRuleState(true);
    }

    private void RedoChanges()
    {
        if (!redoAvailable || redoRuleOrder.Count == 0)
            return;

        isRedoing = true;
        SetRuleOrder(redoRuleOrder);
        ruleGaps.Clear();
        foreach (RuleGap gap in redoRuleGaps)
            ruleGaps.Add(gap);
        explicitRequiredRules.Clear();
        foreach (RuleEdge edge in redoExplicitRequiredRules)
            explicitRequiredRules.Add(edge);
        explicitRequiredIds.Clear();
        foreach (string requiredId in redoExplicitRequiredIds)
            explicitRequiredIds.Add(requiredId);
        NormalizeGaps();
        NormalizeExplicitRules();
        NormalizeExplicitRequiredIds();
        isRedoing = false;

        redoAvailable = false;
        redoRuleOrder.Clear();
        redoExplicitRequiredRules.Clear();
        redoExplicitRequiredIds.Clear();
        changesMade = true;
        RefreshRuleState(true);
    }

    private void SetRuleOrder(IEnumerable<string> orderedIds)
    {
        masterRuleList.Clear();
        HashSet<ModRefViewModel> used = new HashSet<ModRefViewModel>();
        foreach (string id in orderedIds)
        {
            if (string.IsNullOrWhiteSpace(id))
                continue;
            if (!modIdMap.TryGetValue(id, out ModRefViewModel? vm))
                continue;
            if (!used.Add(vm))
                continue;
            masterRuleList.Add(vm);
        }

        ApplySearchFilter();
        modTreeList.SelectedItems?.Clear();
        rulesList.SelectedItems?.Clear();
        modListController.UpdateSelectionState(modTreeList);
        modListController.UpdateSelectionState(rulesList);
    }

    private List<string> GetRuleOrder()
    {
        return masterRuleList
            .Select(vm => vm.ModReference.ID)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToList();
    }

    private void CommitCurrentStateAsSaved()
    {
        initialRuleOrder.Clear();
        initialRuleOrder.AddRange(GetRuleOrder());

        initialRuleGaps.Clear();
        foreach (RuleGap gap in ruleGaps)
            initialRuleGaps.Add(gap);

        initialExplicitRequiredRules.Clear();
        foreach (RuleEdge edge in explicitRequiredRules)
            initialExplicitRequiredRules.Add(edge);

        initialExplicitRequiredIds.Clear();
        foreach (string requiredId in explicitRequiredIds)
            initialExplicitRequiredIds.Add(requiredId);

        changesMade = false;
        ClearRedo();
    }

    private void MarkChanged()
    {
        if (!isRedoing)
            ClearRedo();
        changesMade = true;
    }

    private void ClearRedo()
    {
        redoAvailable = false;
        redoRuleOrder.Clear();
        redoRuleGaps.Clear();
        redoExplicitRequiredRules.Clear();
        redoExplicitRequiredIds.Clear();
    }

    private static ModRefViewModel? GetItemAtPoint(ListBox list, Point point)
    {
        IInputElement? element = list.InputHitTest(point) as IInputElement;
        Control? control = element as Control;
        ListBoxItem? item = control?.FindAncestorOfType<ListBoxItem>();
        return item?.DataContext as ModRefViewModel;
    }

    private static bool IsPointerOverScrollBar(ListBox list, Point point)
    {
        IInputElement? element = list.InputHitTest(point) as IInputElement;
        if (element is not Control control)
            return false;

        if (control is ScrollBar)
            return true;

        return control.FindAncestorOfType<ScrollBar>() != null;
    }

    private List<ModSortRule> ResolveConflicts(List<RuleCandidate> candidates)
    {
        Dictionary<RuleEdge, RuleCandidate> deduped = new Dictionary<RuleEdge, RuleCandidate>(RuleEdgeComparer.Instance);
        foreach (RuleCandidate candidate in candidates)
        {
            if (!IsValidEdge(candidate.Edge))
                continue;

            if (!deduped.TryGetValue(candidate.Edge, out RuleCandidate existing))
            {
                deduped[candidate.Edge] = candidate;
                continue;
            }

            if (candidate.Priority > existing.Priority ||
                (candidate.Priority == existing.Priority && candidate.Order < existing.Order))
            {
                deduped[candidate.Edge] = candidate;
            }
        }

        List<RuleCandidate> ordered = deduped.Values
            .OrderByDescending(candidate => candidate.Priority)
            .ThenBy(candidate => candidate.Order)
            .ThenBy(candidate => candidate.Edge.BeforeId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(candidate => candidate.Edge.AfterId, StringComparer.OrdinalIgnoreCase)
            .ToList();

        Dictionary<string, HashSet<string>> graph = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        List<ModSortRule> resolved = new List<ModSortRule>();
        foreach (RuleCandidate candidate in ordered)
        {
            RuleEdge edge = candidate.Edge;
            if (WouldCreateCycle(graph, edge))
                continue;

            if (!graph.TryGetValue(edge.BeforeId, out HashSet<string>? destinations))
            {
                destinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                graph[edge.BeforeId] = destinations;
            }

            if (!destinations.Add(edge.AfterId))
                continue;

            resolved.Add(new ModSortRule
            {
                BeforeId = edge.BeforeId,
                AfterId = edge.AfterId
            });
        }

        return resolved;
    }

    private static bool WouldCreateCycle(Dictionary<string, HashSet<string>> graph, RuleEdge edge)
    {
        if (string.Equals(edge.BeforeId, edge.AfterId, StringComparison.OrdinalIgnoreCase))
            return true;

        if (!graph.ContainsKey(edge.AfterId))
            return false;

        HashSet<string> visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Stack<string> stack = new Stack<string>();
        stack.Push(edge.AfterId);

        while (stack.Count > 0)
        {
            string current = stack.Pop();
            if (!visited.Add(current))
                continue;

            if (string.Equals(current, edge.BeforeId, StringComparison.OrdinalIgnoreCase))
                return true;

            if (!graph.TryGetValue(current, out HashSet<string>? destinations))
                continue;

            foreach (string destination in destinations)
                stack.Push(destination);
        }

        return false;
    }

    private void NormalizeExplicitRules()
    {
        HashSet<string> ruleIds = new HashSet<string>(
            masterRuleList.Select(vm => vm.ModReference.ID?.Trim() ?? string.Empty)
                .Where(id => !string.IsNullOrWhiteSpace(id)),
            StringComparer.OrdinalIgnoreCase);

        HashSet<RuleEdge> normalized = new HashSet<RuleEdge>(RuleEdgeComparer.Instance);
        foreach (RuleEdge edge in explicitRequiredRules)
        {
            if (!IsValidEdge(edge))
                continue;
            if (!ruleIds.Contains(edge.BeforeId) || !ruleIds.Contains(edge.AfterId))
                continue;
            normalized.Add(edge);
        }

        explicitRequiredRules.Clear();
        foreach (RuleEdge edge in normalized)
            explicitRequiredRules.Add(edge);
    }

    private void NormalizeExplicitRequiredIds()
    {
        HashSet<string> ruleIds = new HashSet<string>(
            masterRuleList.Select(vm => vm.ModReference.ID?.Trim() ?? string.Empty)
                .Where(id => !string.IsNullOrWhiteSpace(id)),
            StringComparer.OrdinalIgnoreCase);

        HashSet<string> normalized = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string requiredId in explicitRequiredIds)
        {
            string id = requiredId?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(id))
                continue;
            if (!ruleIds.Contains(id))
                continue;
            normalized.Add(id);
        }

        explicitRequiredIds.Clear();
        foreach (string id in normalized)
            explicitRequiredIds.Add(id);
    }

    private static RuleEdge CreateEdge(string? beforeId, string? afterId)
    {
        string before = beforeId?.Trim() ?? string.Empty;
        string after = afterId?.Trim() ?? string.Empty;
        return new RuleEdge(before, after);
    }

    private static bool IsValidEdge(RuleEdge edge)
    {
        return !string.IsNullOrWhiteSpace(edge.BeforeId) &&
               !string.IsNullOrWhiteSpace(edge.AfterId) &&
               !string.Equals(edge.BeforeId, edge.AfterId, StringComparison.OrdinalIgnoreCase);
    }

    private ListBox? GetListForMod(ModRefViewModel vm)
    {
        if (ruleMods.Contains(vm))
            return rulesList;
        if (availableMods.Contains(vm))
            return modTreeList;
        return null;
    }

    private sealed class RuleLineIndices
    {
        public int? BeforeIndex { get; set; }
        public int? AfterIndex { get; set; }
        public int? RequiresIndex { get; set; }
    }

    private readonly record struct RequiredMenuPayload(string RequiresId);
    private enum RuleLineType
    {
        Before,
        After,
        Requires
    }

    private readonly record struct PreviewRuleToken(RuleEdge? Edge, string RequiresId, IBrush? MarkerBrush)
    {
        public static PreviewRuleToken EdgeToken(RuleEdge edge) => new(edge, string.Empty, null);
        public static PreviewRuleToken RequiresToken(string requiresId) => new(null, requiresId, null);
        public static PreviewRuleToken Marker(IBrush markerBrush) => new(null, string.Empty, markerBrush);
    }
    private readonly record struct RuleEdge(string BeforeId, string AfterId);
    private readonly record struct RuleCandidate(RuleEdge Edge, int Priority, int Order);

    private readonly record struct RuleGap(string BeforeId, string AfterId);

    private sealed class RuleGapComparer : IEqualityComparer<RuleGap>
    {
        public static readonly RuleGapComparer Instance = new();

        public bool Equals(RuleGap x, RuleGap y)
        {
            return string.Equals(x.BeforeId, y.BeforeId, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(x.AfterId, y.AfterId, StringComparison.OrdinalIgnoreCase);
        }

        public int GetHashCode(RuleGap obj)
        {
            return HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.BeforeId ?? string.Empty),
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.AfterId ?? string.Empty));
        }
    }

    private sealed class RuleEdgeComparer : IEqualityComparer<RuleEdge>
    {
        public static readonly RuleEdgeComparer Instance = new();

        public bool Equals(RuleEdge x, RuleEdge y)
        {
            return string.Equals(x.BeforeId, y.BeforeId, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(x.AfterId, y.AfterId, StringComparison.OrdinalIgnoreCase);
        }

        public int GetHashCode(RuleEdge obj)
        {
            return HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.BeforeId ?? string.Empty),
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.AfterId ?? string.Empty));
        }
    }

    private static void ReplaceCollection(ObservableCollection<ModRefViewModel> target, List<ModRefViewModel> items)
    {
        if (target.Count == items.Count)
        {
            bool same = true;
            for (int i = 0; i < target.Count; i++)
            {
                if (!ReferenceEquals(target[i], items[i]))
                {
                    same = false;
                    break;
                }
            }
            if (same) return;
        }

        target.Clear();
        foreach (ModRefViewModel vm in items) target.Add(vm);
    }

    private static List<ModRefViewModel> Deduplicate(IEnumerable<ModRefViewModel> items)
    {
        List<ModRefViewModel> unique = new List<ModRefViewModel>();
        HashSet<ModRefViewModel> seen = new HashSet<ModRefViewModel>();
        foreach (ModRefViewModel vm in items)
        {
            if (seen.Add(vm))
                unique.Add(vm);
        }
        return unique;
    }
    private static void TempSearchLog(string message)
    {
        if (!DevMode.IsEnabled)
            return;

        Console.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [TEMP][SearchFlow] {message}");
    }
}
