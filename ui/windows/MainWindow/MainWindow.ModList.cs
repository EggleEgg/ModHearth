using Avalonia.Controls;
using Avalonia.Interactivity;
using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using ModHearth.Models;
using ModHearth.Utilities;
using ModHearth.Utilities.Logging;
using ModHearth.UI;

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
        UpdateRelationshipBadges();
        UpdateProblemIndicators();
        UpdateDuplicateWarningIndicators();
        UpdateModlistHeaders();
        UpdateSearchBarAvailableColors();
        ApplySearchFilter();
    }

    private void RebuildPanelCollectionsFromManager()
    {
        List<ModRefViewModel> newInactive = MainWindowModListBuilder.BuildInactiveList(manager, modViewMap);
        List<ModRefViewModel> newActive = MainWindowModListBuilder.BuildActiveList(manager, modViewMap);
        SearchFilterHelper.ReplaceCollection(inactiveMods, newInactive);
        SearchFilterHelper.ReplaceCollection(activeMods, newActive);
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
        int inactiveCount = manager?.disabledMods?.Count ?? 0;
        int inactiveSelected = leftModlist.SelectedItems?.Count ?? 0;
        int activeCount = manager?.enabledMods?.Count ?? 0;
        int activeSelected = rightModlist.SelectedItems?.Count ?? 0;

        leftHeaderLabel.Text = inactiveSelected > 0
            ? $"Inactive [{inactiveCount} / {inactiveSelected} selected]"
            : $"Inactive [{inactiveCount}]";
        rightHeaderLabel.Text = activeSelected > 0
            ? $"Active [{activeCount} / {activeSelected} selected]"
            : $"Active [{activeCount}]";

        if (clearModlistButton != null)
            clearModlistButton.IsEnabled = activeCount > 0;
    }



    private void ModlistSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (isApplyingSearchFilter || isBatchSelecting)
        {
            if (sender is ListBox filteredList)
                modListController.UpdateSelectionState(filteredList);
            UpdateModlistHeaders();
            return;
        }

        if (sender is ListBox list && modListController.HandleSelectionChanged(list))
        {
            UpdateModlistHeaders();
            return;
        }

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

        UpdateModlistHeaders();
    }

    private async void ModlistDropped(ModListDropContext context)
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

        int insertIndex = destinationLeft
            ? context.InsertIndex
            : MapFilteredToMasterIndex(activeMods, BuildEnabledOrderViewModels(), context.InsertIndex);

        manager.MoveMods(mods, insertIndex, sourceLeft, destinationLeft);
        await SetAndMarkChangesAsync(true);
        RefreshModlistPanels();
        if (sourceLeft != destinationLeft)
            SelectModsInList(destinationLeft, mods);
    }

    // The real, order-defining sequence backing the active list. Built by
    // walking manager.enabledMods (the source of truth for order) through
    // modViewMap, rather than trusting activeMods' displayed order directly.
    private List<ModRefViewModel> BuildEnabledOrderViewModels()
    {
        List<ModRefViewModel> master = new List<ModRefViewModel>(manager.enabledMods.Count);
        foreach (DFHMod mod in manager.enabledMods)
        {
            if (modViewMap.TryGetValue(mod.ToString(), out ModRefViewModel? vm) && vm != null)
                master.Add(vm);
        }
        return master;
    }

    // Same translation SortRulesWindow already uses for its own drag-drop:
    // find where a displayed/filtered index actually falls within the true,
    // unfiltered backing order.
    private static int MapFilteredToMasterIndex(
        IList<ModRefViewModel> filtered,
        IList<ModRefViewModel> master,
        int filteredIndex)
    {
        if (filteredIndex <= 0)
            return 0;

        if (filteredIndex >= filtered.Count)
        {
            if (filtered.Count == 0)
                return master.Count;

            int lastIndex = master.IndexOf(filtered[^1]);
            return lastIndex >= 0 ? lastIndex + 1 : master.Count;
        }

        int idx = master.IndexOf(filtered[filteredIndex]);
        return idx >= 0 ? idx : master.Count;
    }

    private async Task MoveSelectedBetweenListsAsync(bool sourceLeft)
    {
        ListBox source = sourceLeft ? leftModlist : rightModlist;
        if (source.SelectedItems == null || source.SelectedItems.Count == 0)
            return;

        List<ModRefViewModel> selected = source.SelectedItems.Cast<ModRefViewModel>().ToList();
        List<DFHMod> mods = selected.Select(vm => vm.DfMod).ToList();
        int index = manager.enabledMods.Count;
        manager.MoveMods(mods, index, sourceLeft, !sourceLeft);
        await SetAndMarkChangesAsync(true);
        RefreshModlistPanels();
        SelectModsInList(!sourceLeft, mods);

        // Async focus to target ListBox and its selected container
        ListBox target = !sourceLeft ? leftModlist : rightModlist;
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            var item = target.SelectedItem;
            if (item != null)
            {
                var container = target.ContainerFromItem(item) as Control;
                if (container != null)
                {
                    container.Focus();
                }
                else
                {
                    target.Focus();
                }
            }
            else
            {
                target.Focus();
            }
        }, Avalonia.Threading.DispatcherPriority.Background);
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
