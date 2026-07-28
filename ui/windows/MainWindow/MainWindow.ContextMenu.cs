using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Layout;
using ModHearth.Metadata;
using System.Collections;
using ModHearth.Models;


namespace ModHearth.UI;

public partial class MainWindow : IModRefContextMenuProvider
{
    public void OnModRefContextMenuOpened(ContextMenu menu, ModRefViewModel vm)
    {
        // ModRefControl has already called PrepareContextMenu via GetManager/GetSelectedModReferences.
        var selected = GetSelectedModReferences(vm).Select(mod =>
        {
            string key = mod.ToDFHMod().ToString();
            return modViewMap.TryGetValue(key, out var v) ? v : null;
        }).OfType<ModRefViewModel>().ToList();

        ConfigureModColorSubmenu(menu, vm, selected);
    }

    public ModHearthManager? GetManager() => manager;

    public IEnumerable<ModReference> GetSelectedModReferences(ModRefViewModel contextVm)
    {
        ListBox? list = GetListForMod(contextVm);
        if (list == null)
        {
            if (rightModlist.SelectedItems?.OfType<ModRefViewModel>().Contains(contextVm) ?? false)
                list = rightModlist;
            else if (leftModlist.SelectedItems?.OfType<ModRefViewModel>().Contains(contextVm) ?? false)
                list = leftModlist;
        }

        if (list != null && list.SelectedItems != null)
        {
            modListController.TryRestoreContextSelection(list, contextVm);
            ModContextMenuSupport.EnsureContextItemSelected(list.SelectedItems, contextVm);
            return list.SelectedItems.Cast<ModRefViewModel>().Select(v => v.ModReference).ToList();
        }

        return new[] { contextVm.ModReference };
    }

    public void OnModRefContextMenuItemClicked(MenuItem item, ModRefViewModel vm)
    {
        string? tag = item.Tag?.ToString();
        switch (tag)
        {
            case "delete-mod":
                ModContextDeleteMod(item, new RoutedEventArgs());
                break;
            case "unsubscribe-steam":
                ModContextUnsubscribeSteam(item, new RoutedEventArgs());
                break;
            case "redownload-steam":
                ModContextRedownloadSteam(item, new RoutedEventArgs());
                break;
            case "open":
                ModContextOpenFolder(item, new RoutedEventArgs());
                break;
            case "open-steam":
                ModContextOpenSteam(item, new RoutedEventArgs());
                break;
            case "copy-id":
                ModContextCopyId(item, new RoutedEventArgs());
                break;
        }
    }

    private async void ModContextDeleteMod(object? sender, RoutedEventArgs e)
    {
        if (!TryGetContextModReferences(sender, out List<ModReference> modReferences))
            return;

        await DeleteSelectedModsAsync(modReferences);
    }

    private async Task DeleteSelectedModsAsync(IReadOnlyList<ModReference> modReferences)
    {
        if (modReferences == null || modReferences.Count == 0)
            return;

        string? targetAfterDeleteId = previousSelectedModId;
        if (!string.IsNullOrWhiteSpace(targetAfterDeleteId) &&
            modReferences.Any(mod => string.Equals(mod.ID, targetAfterDeleteId, StringComparison.OrdinalIgnoreCase)))
        {
            targetAfterDeleteId = null;
        }

        if (!await ModContextMenuSupport.DeleteLocalModsWithConfirmAsync(this, manager, modReferences))
            return;

        try
        {
            await ReloadModpacksFromDisk();
        }
        catch (Exception ex)
        {
            await DialogService.ShowMessageAsync(this, ex.Message, "Reload failed");
            return;
        }

        if (!TrySelectModById(targetAfterDeleteId))
            ShowFallbackInfo();
    }

    private async void ModContextUnsubscribeSteam(object? sender, RoutedEventArgs e)
    {
        if (!TryGetContextModReferences(sender, out List<ModReference> modReferences))
            return;

        await ModContextMenuSupport.UnsubscribeSteamWithConfirmAsync(this, manager, modReferences);
    }

    private async void ModContextRedownloadSteam(object? sender, RoutedEventArgs e)
    {
        if (!TryGetContextModReferences(sender, out List<ModReference> modReferences))
            return;

        await ModContextMenuSupport.RedownloadSteamWithConfirmAsync(this, manager, modReferences);
    }

    private async void ModContextOpenFolder(object? sender, RoutedEventArgs e)
    {
        await ModContextMenuSupport.OpenFolderFromContextMenuAsync(
            sender,
            this,
            GetContextMenuSelectedItems(sender),
            (ModRefViewModel vm) => vm.ModReference);
    }

    private async void ModContextCopyId(object? sender, RoutedEventArgs e)
    {
        await ModContextMenuSupport.CopyModIdFromContextMenuAsync(
            sender,
            this,
            GetContextMenuSelectedItems(sender),
            (ModRefViewModel vm) => vm.ModReference);
    }

    private async void ModContextOpenSteam(object? sender, RoutedEventArgs e)
    {
        await ModContextMenuSupport.OpenSteamPageFromContextMenuAsync(
            sender,
            this,
            GetContextMenuSelectedItems(sender),
            (ModRefViewModel vm) => vm.ModReference);
    }

    private void ConfigureModColorSubmenu(ContextMenu menu, ModRefViewModel contextVm, List<ModRefViewModel> selected)
    {
        MenuItem? colorRoot = menu.Items
            .OfType<MenuItem>()
            .FirstOrDefault(item => string.Equals(item.Tag?.ToString(), "set-mod-color-root", StringComparison.Ordinal));
        if (colorRoot == null)
            return;

        List<ModRefViewModel> targets = selected.Count > 0 && selected.Contains(contextVm)
            ? selected
            : new List<ModRefViewModel> { contextVm };
        List<ModReference> modReferences = targets.Select(t => t.ModReference).ToList();

        colorRoot.Header = targets.Count > 1
            ? $"Set ({targets.Count}) Mods Color"
            : $"Set Mod Color";

        // Get all available colors as ModColorInfo objects
        var allColorInfos = Enum.GetValues<ModColor>()
            .Where(c => c != ModColor.None)
            .Select(c => new ModColorInfo
            {
                ModColor = c,
                Name = ModColorMap.ColorNames[c],
                Color = ModColorMap.GetColor(c),
                IsSelected = (contextVm.ModReference.AssignedColor == c)
            }).ToList();

        // Compute the optimal column count using the square root
        int targetColumns = (int)Math.Sqrt(allColorInfos.Count + 1); // +1 for the "None" option
        if (targetColumns < 1) targetColumns = 1; // Safety fallback

        UniformGrid swatchPanel = new UniformGrid
        {
            Columns = targetColumns
        };

        void ApplyColor(ModColor color)
        {
            foreach (ModReference modRef in modReferences)
            {
                modRef.AssignedColor = color;
                ModColorMetadataStore.SetModColor(modRef.ID, color);
            }
            RefreshModColorUnderlays(modReferences);
            // Closes the whole menu tree, including this open submenu. The submenu itself has no independent "close" concept, 
            // it lives inside the root ContextMenu's popup.
            ContextMenuCoordinator.DismissActive();
        }

        // Add the "None" option first
        swatchPanel.Children.Add(CreateColorSwatchButton(new ModColorInfo
        {
            ModColor = ModColor.None,
            Name = "None (clear color)",
            Color = Colors.Transparent,
            IsSelected = contextVm.ModReference.AssignedColor == ModColor.None
        }, ApplyColor));

        foreach (ModColorInfo colorInfo in allColorInfos)
        {
            swatchPanel.Children.Add(CreateColorSwatchButton(colorInfo, ApplyColor));
        }

        // Same trick SortRulesWindow's "Add required mod" submenu uses to host a live search box: a single submenu row whose Header is an arbitrary
        // control rather than text. StaysOpenOnClick keeps the grid usable. ApplyColor above is what actually closes the menu once a color is
        // picked, not the framework's default click-to-close behavior.
        MenuItem swatchHost = new MenuItem
        {
            Header = swatchPanel,
            StaysOpenOnClick = true,
            Focusable = false
        };
        swatchHost.Classes.Add("color-grid");

        colorRoot.ItemsSource = new[] { swatchHost };
    }

    private static Button CreateColorSwatchButton(ModColorInfo colorInfo, Action<ModColor> onSelected)
    {
        Border swatch = new Border
        {
            Width = 30,
            Height = 30,
            CornerRadius = new CornerRadius(3),
            Background = (colorInfo.ModColor == ModColor.None) ? Brushes.Transparent : BrushCache.GetBrush(colorInfo.Color),
            BorderBrush = colorInfo.IsSelected ? BrushCache.GetBrush(Style.instance!.buttonSelectionColor.ToAvaloniaColor()) : Brushes.Gray,
            BorderThickness = new Thickness(colorInfo.IsSelected ? 4 : 1)
        };

        if (colorInfo.ModColor == ModColor.None)
        {
            swatch.Child = new TextBlock
            {
                Text = "\u2715",
                FontSize = 11,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = Brushes.Gray
            };
        }

        Button button = new Button
        {
            Content = swatch,
            Padding = new Thickness(0),
            Margin = new Thickness(2),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0)
        };

        ToolTip.SetTip(button, colorInfo.Name);
        button.Click += (_, _) => onSelected(colorInfo.ModColor);

        return button;
    }

    // Refreshes only the specific mods that changed, looked up via modViewMap.
    private void RefreshModColorUnderlays(IEnumerable<ModReference> modReferences)
    {
        foreach (ModReference modref in modReferences)
        {
            string key = modref.ToDFHMod().ToString();
            if (modViewMap.TryGetValue(key, out ModRefViewModel? vm) && vm != null)
                vm.RefreshBackground();
        }
        UpdateSearchBarAvailableColors();
        ApplySearchFilterImmediately();
    }

    private bool TryGetContextModReferences(object? sender, out List<ModReference> modReferences)
    {
        modReferences = new List<ModReference>();
        if (sender is not MenuItem menuItem || menuItem.DataContext is not ModRefViewModel vm)
            return false;

        ListBox? list = GetListForMod(vm);
        if (list == null)
            return false;

        return ModContextMenuSupport.TryGetContextModReferences<ModRefViewModel>(
            sender,
            list.SelectedItems,
            contextVm => contextVm.ModReference,
            out modReferences);
    }

    private IList? GetContextMenuSelectedItems(object? sender)
    {
        if (sender is not MenuItem menuItem || menuItem.DataContext is not ModRefViewModel vm)
            return null;

        return GetListForMod(vm)?.SelectedItems;
    }
}
