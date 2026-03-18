using Avalonia.Controls;
using Avalonia.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace ModHearth.UI;

public partial class SortRulesWindow : Window
{
    private readonly ObservableCollection<ModRefViewModel> availableMods = new();
    private readonly ObservableCollection<ModRefViewModel> ruleMods = new();
    private readonly ObservableCollection<ModRefViewModel> selectedRuleMods = new();

    private readonly Dictionary<string, ModRefViewModel> modKeyMap = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ModRefViewModel> modIdMap = new(StringComparer.OrdinalIgnoreCase);

    private readonly ModListDragDropController modListController;
    private readonly UndoRedoKeyHandler undoRedoHandler;

    private readonly List<string> initialRuleOrder = new();
    private bool changesMade;
    private bool redoAvailable;
    private bool isRedoing;
    private List<string> redoRuleOrder = new();

    public SortRulesWindow()
        : this(Array.Empty<ModSortRule>(), Array.Empty<ModReference>())
    {
    }

    public SortRulesWindow(IEnumerable<ModSortRule> existingRules)
        : this(existingRules, Array.Empty<ModReference>())
    {
    }

    public SortRulesWindow(IEnumerable<ModSortRule> existingRules, IEnumerable<ModReference> modRefs)
    {
        InitializeComponent();

        BuildViewModels(existingRules ?? Array.Empty<ModSortRule>(), modRefs ?? Array.Empty<ModReference>());

        modTreeList.ItemsSource = availableMods;
        rulesList.ItemsSource = ruleMods;
        selectedRulesList.ItemsSource = selectedRuleMods;

        modListController = new ModListDragDropController(
            this,
            () => availableMods.Concat(ruleMods),
            key => modKeyMap.TryGetValue(key, out ModRefViewModel? vm) ? vm : null,
            vm => vm.DfMod.ToString());
        modListController.RegisterList(modTreeList, allowReorder: false);
        modListController.RegisterList(rulesList, allowReorder: true);
        modListController.Dropped += HandleDrop;

        modTreeList.SelectionChanged += ModlistSelectionChanged;
        rulesList.SelectionChanged += ModlistSelectionChanged;

        searchBox.TextChanged += (_, _) => ApplySearchFilter();
        saveButton.Click += (_, _) => SaveAndClose();

        undoRedoHandler = new UndoRedoKeyHandler(
            () => changesMade,
            () => UndoChangesAsync(),
            () => redoAvailable,
            () => RedoChanges());
        undoRedoHandler.Attach(this);

        ApplySearchFilter();
        UpdateSelectedRuleMods();
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
        UpdateSelectedRuleMods();
    }

    private void HandleDrop(ModListDropContext context)
    {
        if (context.Items.Count == 0)
            return;

        bool sourceRight = context.SourceList == rulesList;
        if (context.SourceList == null)
            sourceRight = context.Items.Any(vm => ruleMods.Contains(vm));

        bool destinationRight = context.DestinationList == rulesList;

        if (!sourceRight && !destinationRight)
            return;

        if (sourceRight && destinationRight)
        {
            ReorderRuleMods(context.Items, context.InsertIndex);
            MarkChanged();
            SelectItemsInList(rulesList, context.Items);
            UpdateSelectedRuleMods();
            return;
        }

        if (!sourceRight && destinationRight)
        {
            MoveToRuleMods(context.Items, context.InsertIndex);
            MarkChanged();
            SelectItemsInList(rulesList, context.Items);
            UpdateSelectedRuleMods();
            return;
        }

        if (sourceRight && !destinationRight)
        {
            MoveToAvailableMods(context.Items);
            MarkChanged();
            SelectItemsInList(modTreeList, context.Items);
            UpdateSelectedRuleMods();
        }
    }

    private void MoveToRuleMods(List<ModRefViewModel> items, int insertIndex)
    {
        List<ModRefViewModel> unique = Deduplicate(items);
        if (unique.Count == 0)
            return;

        foreach (ModRefViewModel vm in unique)
            availableMods.Remove(vm);

        int clamped = Math.Max(0, Math.Min(insertIndex, ruleMods.Count));
        for (int i = 0; i < unique.Count; i++)
            ruleMods.Insert(clamped + i, unique[i]);

        ApplySearchFilter();
    }

    private void MoveToAvailableMods(List<ModRefViewModel> items)
    {
        List<ModRefViewModel> unique = Deduplicate(items);
        if (unique.Count == 0)
            return;

        foreach (ModRefViewModel vm in unique)
            ruleMods.Remove(vm);

        List<ModRefViewModel> available = availableMods.Concat(unique)
            .Distinct()
            .OrderBy(vm => vm.ModReference.ID, StringComparer.OrdinalIgnoreCase)
            .ToList();
        ResetCollection(availableMods, available);
        ApplySearchFilter();
    }

    private void ReorderRuleMods(List<ModRefViewModel> items, int insertIndex)
    {
        HashSet<ModRefViewModel> selectedSet = new HashSet<ModRefViewModel>(items);
        List<ModRefViewModel> selectedInOrder = ruleMods.Where(m => selectedSet.Contains(m)).ToList();
        if (selectedInOrder.Count == 0)
            return;

        int clampedIndex = Math.Max(0, Math.Min(insertIndex, ruleMods.Count));
        int selectedBefore = ruleMods.Take(clampedIndex).Count(m => selectedSet.Contains(m));
        int targetIndex = clampedIndex - selectedBefore;

        List<ModRefViewModel> remaining = ruleMods.Where(m => !selectedSet.Contains(m)).ToList();
        targetIndex = Math.Max(0, Math.Min(targetIndex, remaining.Count));

        List<ModRefViewModel> newList = new List<ModRefViewModel>();
        newList.AddRange(remaining.Take(targetIndex));
        newList.AddRange(selectedInOrder);
        newList.AddRange(remaining.Skip(targetIndex));

        ResetCollection(ruleMods, newList);
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

    private void UpdateSelectedRuleMods()
    {
        selectedRuleMods.Clear();
        List<ModRefViewModel> selected = rulesList.SelectedItems?.Cast<ModRefViewModel>().ToList()
            ?? new List<ModRefViewModel>();
        if (selected.Count == 0)
            return;

        HashSet<ModRefViewModel> selectedSet = new HashSet<ModRefViewModel>(selected);
        foreach (ModRefViewModel vm in ruleMods)
        {
            if (selectedSet.Contains(vm))
                selectedRuleMods.Add(vm);
        }
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
        }

        List<ModRefViewModel> sorted = modIdMap.Values
            .OrderBy(vm => vm.ModReference.ID, StringComparer.OrdinalIgnoreCase)
            .ToList();
        ResetCollection(availableMods, sorted);

        List<string> orderedRuleIds = BuildRuleOrder(existingRules, modIdMap.Keys);
        foreach (string id in orderedRuleIds)
        {
            if (!modIdMap.TryGetValue(id, out ModRefViewModel? vm))
                continue;
            if (availableMods.Contains(vm))
            {
                availableMods.Remove(vm);
                ruleMods.Add(vm);
            }
        }

        initialRuleOrder.Clear();
        initialRuleOrder.AddRange(ruleMods
            .Select(vm => vm.ModReference.ID)
            .Where(id => !string.IsNullOrWhiteSpace(id)));
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

    private void ApplySearchFilter()
    {
        string filter = searchBox.Text?.Trim() ?? string.Empty;
        bool hasFilter = !string.IsNullOrWhiteSpace(filter);

        foreach (ModRefViewModel vm in availableMods)
        {
            bool matches = true;
            if (hasFilter)
            {
                string name = vm.DisplayName ?? string.Empty;
                string id = vm.ModReference.ID ?? string.Empty;
                matches = name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                          id.Contains(filter, StringComparison.OrdinalIgnoreCase);
            }

            vm.IsVisible = matches;
            vm.IsFilteredOut = hasFilter && !matches;
        }
    }

    private void SaveAndClose()
    {
        List<string> orderedIds = ruleMods
            .Select(vm => vm.ModReference.ID)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToList();

        List<ModSortRule> rules = new();
        for (int i = 0; i < orderedIds.Count - 1; i++)
        {
            string before = orderedIds[i];
            string after = orderedIds[i + 1];
            if (string.Equals(before, after, StringComparison.OrdinalIgnoreCase))
                continue;
            rules.Add(new ModSortRule { BeforeId = before, AfterId = after });
        }

        Close(rules);
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
        redoAvailable = true;

        isRedoing = true;
        SetRuleOrder(initialRuleOrder);
        isRedoing = false;

        changesMade = false;
        UpdateSelectedRuleMods();
    }

    private void RedoChanges()
    {
        if (!redoAvailable || redoRuleOrder.Count == 0)
            return;

        isRedoing = true;
        SetRuleOrder(redoRuleOrder);
        isRedoing = false;

        redoAvailable = false;
        redoRuleOrder.Clear();
        changesMade = true;
        UpdateSelectedRuleMods();
    }

    private void SetRuleOrder(IEnumerable<string> orderedIds)
    {
        HashSet<ModRefViewModel> used = new HashSet<ModRefViewModel>();
        List<ModRefViewModel> newRules = new List<ModRefViewModel>();
        foreach (string id in orderedIds)
        {
            if (string.IsNullOrWhiteSpace(id))
                continue;
            if (!modIdMap.TryGetValue(id, out ModRefViewModel? vm))
                continue;
            if (!used.Add(vm))
                continue;
            newRules.Add(vm);
        }

        List<ModRefViewModel> newAvailable = modIdMap.Values
            .Where(vm => !used.Contains(vm))
            .OrderBy(vm => vm.ModReference.ID, StringComparer.OrdinalIgnoreCase)
            .ToList();

        ResetCollection(ruleMods, newRules);
        ResetCollection(availableMods, newAvailable);
        ApplySearchFilter();
        modTreeList.SelectedItems?.Clear();
        rulesList.SelectedItems?.Clear();
        modListController.UpdateSelectionState(modTreeList);
        modListController.UpdateSelectionState(rulesList);
    }

    private List<string> GetRuleOrder()
    {
        return ruleMods
            .Select(vm => vm.ModReference.ID)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToList();
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
    }

    private static void ResetCollection(ObservableCollection<ModRefViewModel> target, List<ModRefViewModel> items)
    {
        if (target.SequenceEqual(items))
            return;

        target.Clear();
        foreach (ModRefViewModel vm in items)
            target.Add(vm);
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
}
