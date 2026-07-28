using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Threading;
using ModHearth.Models;

namespace ModHearth.UI;

/// <summary>
/// Orchestrates the relationship between a ModSearchBar and a ListBox,
/// handling filtering, sorting, debouncing, and selection state restoration.
/// </summary>
public sealed class ModSearchController : IDisposable
{
    private readonly ModSearchBar searchBar;
    private readonly ListBox? listBox;
    private readonly ListSelectionController<ModRefViewModel>? selectionController;
    private readonly ObservableCollection<ModRefViewModel> targetCollection;
    private readonly IEnumerable<ModRefViewModel> sourceItems;
    private readonly DispatcherTimer debounceTimer;
    private bool isInitialApply = true;

    public ModSearchController(
        ModSearchBar searchBar,
        ObservableCollection<ModRefViewModel> targetCollection,
        IEnumerable<ModRefViewModel> sourceItems,
        ListBox? listBox = null,
        ListSelectionController<ModRefViewModel>? selectionController = null,
        double debounceMs = 200)
    {
        this.searchBar = searchBar;
        this.targetCollection = targetCollection;
        this.sourceItems = sourceItems;
        this.listBox = listBox;
        this.selectionController = selectionController;

        debounceTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(debounceMs)
        };
        debounceTimer.Tick += (s, e) =>
        {
            debounceTimer.Stop();
            ApplyFilter();
        };

        // Wire up search bar events
        searchBar.SearchTextChanged += (_, _) => ScheduleFilter();
        searchBar.SearchModeChanged += (_, _) => ApplyFilterImmediately();
        searchBar.SortOrderChanged += (_, _) => ApplyFilterImmediately();
        searchBar.HideFilteredToggled += (_, _) => ApplyFilterImmediately();

        // Initial setup
        SyncAvailableColors();
    }

    /// <summary>
    /// Schedules a filter operation with debouncing.
    /// </summary>
    public void ScheduleFilter()
    {
        debounceTimer.Stop();
        debounceTimer.Start();
    }

    /// <summary>
    /// Applies the filter immediately, bypassing the debounce timer.
    /// </summary>
    public void ApplyFilterImmediately()
    {
        debounceTimer.Stop();
        ApplyFilter();
    }

    /// <summary>
    /// Synchronizes the search bar's available colors with the current source items.
    /// </summary>
    public void SyncAvailableColors()
    {
        var colors = sourceItems
            .Select(vm => vm.ModReference.AssignedColor)
            .Where(c => c != ModColor.None)
            .Distinct();
        searchBar.SetAvailableColors(colors);
    }

    private void ApplyFilter()
    {
        var filteredAndSorted = SearchFilterHelper.ApplyFilterAndSort(
            sourceItems,
            searchBar.Text,
            searchBar.SearchMode,
            searchBar.HideFiltered,
            searchBar.SortDescending,
            searchBar.IsSortingEnabled);

        SearchFilterHelper.ReplaceCollection(targetCollection, filteredAndSorted);

        if (listBox != null && selectionController != null)
        {
            selectionController.UpdateSelectionState(listBox);
        }

        if (isInitialApply)
        {
            isInitialApply = false;
        }
    }

    public void Dispose()
    {
        try { debounceTimer.Stop(); } catch { }
    }
}
