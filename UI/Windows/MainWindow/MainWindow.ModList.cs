using Avalonia.Controls;
using Avalonia.Interactivity;
using System.Collections;
using System.Collections.ObjectModel;

namespace ModHearth.UI;

public partial class MainWindow
{
    private void BuildModViewModels()
    {
        MainWindowModListBuilder.SyncViewModels(manager, modViewMap);
    }

    private void RefreshModlistPanels()
    {
        RebuildPanelCollectionsFromManager();
        UpdateCachedIndicators();
        UpdateProblemIndicators();
        UpdateDuplicateWarningIndicators();
        UpdateModlistHeaders();
        ApplySearchFilter();
    }

    private void RebuildPanelCollectionsFromManager()
    {
        List<ModRefViewModel> newInactive = MainWindowModListBuilder.BuildInactiveList(manager, modViewMap);
        List<ModRefViewModel> newActive = MainWindowModListBuilder.BuildActiveList(manager, modViewMap);
        ReplaceCollection(inactiveMods, newInactive);
        ReplaceCollection(activeMods, newActive);
    }

    private void SelectModsInList(bool destinationLeft, IEnumerable<DFHMod> mods)
    {
        ListBox list = destinationLeft ? leftModlist : rightModlist;
        ObservableCollection<ModRefViewModel> source = destinationLeft ? inactiveMods : activeMods;

        List<ModRefViewModel> toSelect = mods
            .Select(mod => source.FirstOrDefault(m => m.DfMod == mod))
            .Where(vm => vm != null)
            .Cast<ModRefViewModel>()
            .ToList();

        isBatchSelecting = true;
        try
        {
            list.SelectedItems?.Clear();
            foreach (ModRefViewModel vm in toSelect)
                list.SelectedItems?.Add(vm);
        }
        finally
        {
            isBatchSelecting = false;
        }

        modListController.UpdateSelectionState(list);

        ModRefViewModel? primary = toSelect.FirstOrDefault();
        if (primary != null)
        {
            list.ScrollIntoView(primary);
            TrackSelectedMod(primary);
            ShowModInfo(primary.ModReference);
        }
    }

    private void UpdateModlistHeaders()
    {
        leftHeaderLabel.Text = $"Inactive [{manager?.disabledMods?.Count ?? 0}]";
        rightHeaderLabel.Text = $"Active [{manager?.enabledMods?.Count ?? 0}]";
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

            if (same)
                return;
        }

        target.Clear();
        foreach (ModRefViewModel vm in items)
            target.Add(vm);
    }

    private void ModlistSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (isApplyingSearchFilter || isBatchSelecting)
        {
            if (sender is ListBox filteredList)
                modListController.UpdateSelectionState(filteredList);
            return;
        }

        if (sender is ListBox list && modListController.HandleSelectionChanged(list))
            return;

        if (sender == leftModlist && leftModlist.SelectedItems?.Count > 0)
            rightModlist.SelectedItems?.Clear();
        if (sender == rightModlist && rightModlist.SelectedItems?.Count > 0)
            leftModlist.SelectedItems?.Clear();

        modListController.UpdateSelectionState(leftModlist);
        modListController.UpdateSelectionState(rightModlist);

        ModRefViewModel? selected = (sender as ListBox)?.SelectedItem as ModRefViewModel;
        if (selected != null)
        {
            TrackSelectedMod(selected);
            ShowModInfo(selected.ModReference);
        }
    }

    private void ModlistDropped(ModListDropContext context)
    {
        if (context.Items.Count == 0)
            return;

        bool sourceLeft = context.SourceList == leftModlist;
        if (context.SourceList == null)
            sourceLeft = context.Items.Any(vm => inactiveMods.Contains(vm));
        bool destinationLeft = context.DestinationList == leftModlist;

        if (sourceLeft && destinationLeft)
            return;

        List<DFHMod> mods = context.Items.Select(vm => vm.DfMod).ToList();
        manager.MoveMods(mods, context.InsertIndex, sourceLeft, destinationLeft);
        SetAndMarkChanges(true);
        RefreshModlistPanels();
        if (sourceLeft != destinationLeft)
            SelectModsInList(destinationLeft, mods);
    }

    private void MoveSelectedBetweenLists(bool sourceLeft)
    {
        ListBox source = sourceLeft ? leftModlist : rightModlist;
        if (source.SelectedItems == null || source.SelectedItems.Count == 0)
            return;

        List<ModRefViewModel> selected = source.SelectedItems.Cast<ModRefViewModel>().ToList();
        List<DFHMod> mods = selected.Select(vm => vm.DfMod).ToList();
        int index = manager.enabledMods.Count;
        manager.MoveMods(mods, index, sourceLeft, !sourceLeft);
        SetAndMarkChanges(true);
        RefreshModlistPanels();
        SelectModsInList(!sourceLeft, mods);
    }

    private ListBox? GetListForMod(ModRefViewModel vm)
    {
        if (inactiveMods.Contains(vm))
            return leftModlist;
        if (activeMods.Contains(vm))
            return rightModlist;
        return null;
    }
}
