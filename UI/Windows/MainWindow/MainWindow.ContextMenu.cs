using Avalonia.Controls;
using Avalonia.Interactivity;
using System.Collections;

namespace ModHearth.UI;

public partial class MainWindow
{
    private void ModContextMenuOpened(object? sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu menu)
            return;

        Control? placementControl = menu.PlacementTarget as Control;
        ModRefViewModel? vm =
            placementControl?.DataContext as ModRefViewModel ??
            menu.DataContext as ModRefViewModel ??
            menu.Items.OfType<MenuItem>()
                .Select(item => item.DataContext)
                .OfType<ModRefViewModel>()
                .FirstOrDefault() ??
            rightModlist.SelectedItems?.OfType<ModRefViewModel>().FirstOrDefault() ??
            leftModlist.SelectedItems?.OfType<ModRefViewModel>().FirstOrDefault();
        if (vm == null)
            return;

        ListBox? list = GetListForMod(vm);
        if (list == null)
        {
            if (rightModlist.SelectedItems?.OfType<ModRefViewModel>().Contains(vm) ?? true)
                list = rightModlist;
            else if (leftModlist.SelectedItems?.OfType<ModRefViewModel>().Contains(vm) ?? true)
                list = leftModlist;
            else if (rightModlist.SelectedItems?.Count > 0)
                list = rightModlist;
            else if (leftModlist.SelectedItems?.Count > 0)
                list = leftModlist;
        }

        if (list != null)
        {
            modListController.TryRestoreContextSelection(list, vm);
            ModContextMenuSupport.EnsureContextItemSelected(list.SelectedItems, vm);
        }

        List<ModRefViewModel> selected = list?.SelectedItems?.Cast<ModRefViewModel>().ToList()
            ?? new List<ModRefViewModel>();
        ModContextMenuSupport.PrepareContextMenu(
            menu,
            manager,
            vm.ModReference,
            selected.Select(item => item.ModReference));
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
