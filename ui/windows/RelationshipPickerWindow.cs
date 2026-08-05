using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using ModHearth.Models;

namespace ModHearth.UI;

internal sealed class RelationshipPickerWindow : Window
{
    private readonly string ownerId;
    private readonly HashSet<string> alreadyAdded;
    private readonly List<ModRefViewModel> allMods;
    private readonly ObservableCollection<ModRefViewModel> visibleItems = [];
    private readonly ModSearchBar searchBar = new();
    private readonly ListBox listBox = new();
    private readonly ListSelectionController<ModRefViewModel> selectionController = new();
    private readonly ModSearchController searchController;

    public RelationshipPickerWindow(
        string ownerId,
        ModRelationshipKind kind,
        IEnumerable<ModRefViewModel> candidates,
        IReadOnlySet<string> alreadyAdded,
        IReadOnlyDictionary<string, ModRelationshipRule> relationshipRules)
    {
        this.ownerId = ownerId.Trim();
        this.alreadyAdded = new HashSet<string>(alreadyAdded, StringComparer.OrdinalIgnoreCase);

        allMods = candidates
            .Where(vm => !string.Equals(vm.ModReference.ID, this.ownerId, StringComparison.OrdinalIgnoreCase))
            .Select(vm =>
            {
                ModRefViewModel copy = new ModRefViewModel(vm.ModReference);
                MainWindowModListBuilder.CopyClassification(copy, vm);
                return copy;
            })
            .OrderBy(vm => vm.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(vm => vm.ModReference.ID, StringComparer.OrdinalIgnoreCase)
            .ToList();
        ModListIndicatorUpdater.UpdateRelationshipBadges(allMods, relationshipRules);

        Title = $"Add {LabelFor(kind)}";
        Width = 460;
        Height = 560;
        MinWidth = 360;
        MinHeight = 360;

        Grid rootGrid = new()
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"),
            Margin = new Thickness(12)
        };

        TextBlock searchLabel = new() { Text = "Search Mods", FontWeight = FontWeight.Bold, Margin = new Thickness(0, 0, 0, 8) };
        Grid.SetRow(searchLabel, 0);
        rootGrid.Children.Add(searchLabel);

        searchBar.HideFiltered = true;
        searchBar.Margin = new Thickness(0, 0, 0, 8);
        Grid.SetRow(searchBar, 1);
        rootGrid.Children.Add(searchBar);

        listBox.ItemsSource = visibleItems;
        listBox.SelectionMode = SelectionMode.Single;

        listBox.ItemContainerTheme = new ControlTheme(typeof(ListBoxItem))
        {
            Setters =
            {
                new Setter(ListBoxItem.BackgroundProperty, Brushes.Transparent),
                new Setter(ListBoxItem.PaddingProperty, new Thickness(0)),
                new Setter(ListBoxItem.MarginProperty, new Thickness(0))
            }
        };

        selectionController.RegisterList(listBox);

        listBox.ItemTemplate = new FuncDataTemplate<ModRefViewModel>((vm, _) =>
        {
            bool isAdded = this.alreadyAdded.Contains(vm.ModReference.ID.Trim());

            Grid row = new() { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Opacity = isAdded ? 0.5 : 1 };

            ModRefControl modControl = new()
            {
                DataContext = vm,
                Padding = new Thickness(2, 0),
                ShowDetailedRuleBadges = true,
                AllowContextActions = false,
                AllowColorEditing = false,
                AllowRelationshipEditing = false,
                AllowContextMenu = false
            };

            if (isAdded)
            {
                TextBlock addedLabel = new()
                {
                    Text = "(Already added)",
                    FontSize = 12,
                    FontStyle = FontStyle.Italic,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(10, 0, 5, 0),
                    Opacity = 0.7
                };
                if (Style.instance != null)
                    addedLabel.Background = BrushCache.GetBrush(Style.instance.panelColorDark.ToAvaloniaColor());
                Grid.SetColumn(addedLabel, 1);
                row.Children.Add(addedLabel);
            }

            row.Children.Add(modControl);
            return row;
        }, true);

        Grid.SetRow(listBox, 2);
        rootGrid.Children.Add(listBox);

        Content = rootGrid;

        // Use the new ModSearchController
        searchController = new ModSearchController(searchBar, visibleItems, allMods, listBox, selectionController);

        listBox.DoubleTapped += (_, _) => SelectHighlighted();

        // Prevent selecting already added items
        listBox.SelectionChanged += (_, e) =>
        {
            if (selectionController.HandleSelectionChanged(listBox))
                return;

            selectionController.UpdateSelectionState(listBox);

            if (listBox.SelectedItem is ModRefViewModel vm && this.alreadyAdded.Contains(vm.ModReference.ID.Trim()))
            {
                Dispatcher.UIThread.Post(() => listBox.SelectedItem = visibleItems.FirstOrDefault(i => !this.alreadyAdded.Contains(i.ModReference.ID.Trim())));
            }
        };

        KeyDown += PickerKeyDown;
        TextInput += (_, _) => searchBar.FocusSearchBox();

        ApplyFilter();
        listBox.SelectedItem = visibleItems.FirstOrDefault(vm => !this.alreadyAdded.Contains(vm.ModReference.ID.Trim()));
        Opened += (_, _) => searchBar.FocusSearchBox();
        Closed += (_, _) => searchController?.Dispose();
        WindowThemeManager.Register(this);
    }

    private void ApplyFilter()
    {
        searchController.ApplyFilterImmediately();
    }

    private void PickerKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                Close(null);
                e.Handled = true;
                return;
            case Key.Enter:
                SelectHighlighted();
                e.Handled = true;
                return;

        }

        if (e.Key != Key.Down && e.Key != Key.Up)
            return;

        int delta = e.Key == Key.Down ? 1 : -1;
        int current = Math.Max(0, listBox.SelectedIndex);
        int index = current;
        do
        {
            index += delta;
            if (index < 0 || index >= visibleItems.Count)
                break;
        }
        while (alreadyAdded.Contains(visibleItems[index].ModReference.ID.Trim()));

        if (index >= 0 && index < visibleItems.Count)
        {
            listBox.SelectedIndex = index;
            listBox.ScrollIntoView(visibleItems[index]);
        }

        e.Handled = true;
    }

    private void SelectHighlighted()
    {
        if (listBox.SelectedItem is not ModRefViewModel vm || alreadyAdded.Contains(vm.ModReference.ID.Trim()))
            return;

        Close(vm.ModReference.ID.Trim());
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
}
