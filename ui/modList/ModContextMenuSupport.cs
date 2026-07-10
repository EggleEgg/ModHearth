using Avalonia.Controls;
using Avalonia.Input.Platform;
using System.Collections;
using System.Diagnostics;

namespace ModHearth.UI;

internal readonly record struct ModContextMenuState(
    int LocalCount,
    int SteamCount,
    bool CanOpenFolder,
    bool HasSteamPage,
    bool ShowSteamActionsForContext)
{
    public bool HasLocalActions => LocalCount > 0;
    public bool HasSteamActions => ShowSteamActionsForContext && SteamCount > 0;
}

internal static class ModContextMenuSupport
{
    public const string DeleteTag = "delete-mod";
    public const string UnsubscribeTag = "unsubscribe-steam";
    public const string RedownloadTag = "redownload-steam";
    public const string OpenFolderTag = "open";
    public const string OpenSteamTag = "open-steam";
    public const string CopyIdTag = "copy-id";
    public const string SetModColorTag = "set-mod-color";

    public static void PrepareContextMenu(
        ContextMenu menu,
        ModHearthManager manager,
        ModReference contextMod,
        IEnumerable<ModReference> selectedMods)
    {
        ContextMenuCoordinator.Activate(menu);
        ModContextMenuState state = BuildState(manager, contextMod, selectedMods);
        ApplyState(menu, state);
    }

    public static void EnsureContextItemSelected(IList? selectedItems, object contextItem)
    {
        if (selectedItems == null)
            return;

        if (selectedItems.Count == 0 || !selectedItems.Contains(contextItem))
        {
            selectedItems.Clear();
            selectedItems.Add(contextItem);
        }
    }

    public static bool TryGetContextModReferences<T>(
        object? sender,
        IList? selectedItems,
        Func<T, ModReference> getModReference,
        out List<ModReference> modReferences) where T : class
    {
        modReferences = new List<ModReference>();
        if (sender is not MenuItem menuItem)
            return false;

        if (menuItem.DataContext is not T contextItem)
        {
            // The menu item might inherit DataContext from its parent context menu or placement target.
            // If the MenuItem itself doesn't have the T, we check if the MenuItem's parent ContextMenu has it.
            if (menuItem.Parent is ContextMenu menu && menu.DataContext is T menuContext)
            {
                contextItem = menuContext;
            }
            else
            {
                return false;
            }
        }

        List<T> selected = selectedItems?.Cast<T>().ToList() ?? new List<T>();
        IEnumerable<T> targets = selected.Count > 0 && selected.Contains(contextItem)
            ? selected
            : new[] { contextItem };
        modReferences = targets.Select(getModReference).ToList();
        return true;
    }

    public static ModContextMenuState BuildState(
        ModHearthManager manager,
        ModReference contextMod,
        IEnumerable<ModReference> selectedMods)
    {
        List<ModReference> selection = selectedMods?.Where(mod => mod != null).ToList()
            ?? new List<ModReference>();
        manager.SplitActionableMods(selection, out List<ModReference> localMods, out List<ModReference> steamMods);
        manager.SplitActionableMods(new[] { contextMod }, out _, out List<ModReference> contextSteamMods);

        bool canOpenFolder = !string.IsNullOrWhiteSpace(contextMod.path) && Directory.Exists(contextMod.path);
        bool hasSteamPage = ModHearthManager.TryGetSteamWorkshopItemId(contextMod, out _);

        return new ModContextMenuState(
            localMods.Count,
            steamMods.Count,
            canOpenFolder,
            hasSteamPage,
            steamMods.Count > 0);
    }

    public static void ApplyState(ContextMenu menu, ModContextMenuState state)
    {
        SetMenuItem(
            menu,
            DeleteTag,
            state.HasLocalActions,
            state.HasLocalActions,
            state.LocalCount > 1 ? $"Delete {state.LocalCount} local mods" : "Delete local mod");
        SetMenuItem(
            menu,
            UnsubscribeTag,
            state.HasSteamActions,
            state.HasSteamActions,
            state.SteamCount > 1 ? $"Unsubscribe from {state.SteamCount} steam mods" : "Unsubscribe from steam mod");
        SetMenuItem(
            menu,
            RedownloadTag,
            state.HasSteamActions,
            state.HasSteamActions,
            state.SteamCount > 1 ? $"Redownload {state.SteamCount} mods" : "Redownload steam mod");
        SetMenuItem(menu, OpenFolderTag, true, state.CanOpenFolder);
        SetMenuItem(menu, OpenSteamTag, state.HasSteamPage, state.HasSteamPage);
    }

    public static string BuildDeletePrompt(IReadOnlyCollection<ModReference> localTargets)
    {
        return localTargets.Count == 1
            ? $"Delete '{localTargets.First().name}' from the Mods folder?"
            : $"Delete {localTargets.Count} mods from the Mods folder?";
    }

    public static string BuildUnsubscribePrompt(IReadOnlyCollection<ModReference> steamTargets)
    {
        return steamTargets.Count == 1
            ? $"Unsubscribe from '{steamTargets.First().name}' on Steam Workshop?"
            : $"Unsubscribe from {steamTargets.Count} Steam mods?";
    }

    public static string BuildRedownloadPrompt(IReadOnlyCollection<ModReference> steamTargets)
    {
        return steamTargets.Count == 1
            ? $"Redownload '{steamTargets.First().name}' from Steam Workshop?"
            : $"Redownload {steamTargets.Count} Steam mods?";
    }

    public static async Task<bool> DeleteLocalModsWithConfirmAsync(
        Window owner,
        ModHearthManager manager,
        IEnumerable<ModReference> modReferences)
    {
        manager.SplitActionableMods(
            modReferences,
            out List<ModReference> localTargets,
            out _);

        if (localTargets.Count == 0)
        {
            await DialogService.ShowMessageAsync(owner, "Selected mods cannot be deleted from the Mods folder.", "Delete Mod");
            return false;
        }

        string prompt = BuildDeletePrompt(localTargets);
        bool confirm = await DialogService.ShowConfirmAsync(owner, prompt, "Delete Mod");
        if (!confirm)
            return false;

        List<string> failures = DeleteLocalMods(manager, localTargets);
        if (failures.Count > 0)
            await DialogService.ShowMessageAsync(owner, string.Join(Environment.NewLine, failures), "Delete Mod");

        return true;
    }

    public static async Task UnsubscribeSteamWithConfirmAsync(
        Window owner,
        ModHearthManager manager,
        IEnumerable<ModReference> modReferences)
    {
        manager.SplitActionableMods(
            modReferences,
            out _,
            out List<ModReference> steamTargets);

        if (steamTargets.Count == 0)
        {
            await DialogService.ShowMessageAsync(owner, "Selected mods are not Steam Workshop mods.", "Unsubscribe Steam Mod");
            return;
        }

        string prompt = BuildUnsubscribePrompt(steamTargets);
        bool confirm = await DialogService.ShowConfirmAsync(owner, prompt, "Unsubscribe Steam Mod");
        if (!confirm)
            return;

        List<string> failures = await Task.Run(() => UnsubscribeSteamMods(manager, steamTargets));
        if (failures.Count > 0)
            await DialogService.ShowMessageAsync(owner, string.Join(Environment.NewLine, failures), "Unsubscribe Steam Mod");
    }

    public static async Task RedownloadSteamWithConfirmAsync(
        Window owner,
        ModHearthManager manager,
        IEnumerable<ModReference> modReferences)
    {
        manager.SplitActionableMods(
            modReferences,
            out _,
            out List<ModReference> steamTargets);

        if (steamTargets.Count == 0)
        {
            await DialogService.ShowMessageAsync(owner, "Selected mods are not Steam Workshop mods.", "Redownload Steam Mod");
            return;
        }

        string prompt = BuildRedownloadPrompt(steamTargets);
        bool confirm = await DialogService.ShowConfirmAsync(owner, prompt, "Redownload Steam Mod");
        if (!confirm)
            return;

        List<string> failures = await Task.Run(() => RedownloadSteamMods(manager, steamTargets));
        if (failures.Count > 0)
            await DialogService.ShowMessageAsync(owner, string.Join(Environment.NewLine, failures), "Redownload Steam Mod");
    }

    public static async Task OpenFolderFromContextMenuAsync<T>(
        object? sender,
        Window owner,
        IList? selectedItems,
        Func<T, ModReference> getModReference) where T : class
    {
        if (!TryGetContextModReferences(sender, selectedItems, getModReference, out List<ModReference> modReferences))
            return;

        await OpenFolderAsync(owner, modReferences[0]);
    }

    public static async Task CopyModIdFromContextMenuAsync<T>(
        object? sender,
        Window owner,
        IList? selectedItems,
        Func<T, ModReference> getModReference) where T : class
    {
        if (!TryGetContextModReferences(sender, selectedItems, getModReference, out List<ModReference> modReferences))
            return;

        await CopyModIdAsync(owner, modReferences[0]);
    }

    public static async Task OpenSteamPageFromContextMenuAsync<T>(
        object? sender,
        Window owner,
        IList? selectedItems,
        Func<T, ModReference> getModReference) where T : class
    {
        if (!TryGetContextModReferences(sender, selectedItems, getModReference, out List<ModReference> modReferences))
            return;

        await OpenSteamPageAsync(owner, modReferences[0]);
    }

    public static List<string> DeleteLocalMods(ModHearthManager manager, IEnumerable<ModReference> localTargets)
    {
        List<string> failures = new List<string>();
        foreach (ModReference modref in localTargets)
        {
            if (!manager.DeleteModFromModsFolder(modref, out string message))
                failures.Add(message);
        }

        return failures;
    }

    public static List<string> UnsubscribeSteamMods(ModHearthManager manager, IEnumerable<ModReference> steamTargets)
    {
        return manager.UnsubscribeSteamMods(steamTargets);
    }

    public static List<string> RedownloadSteamMods(ModHearthManager manager, IEnumerable<ModReference> steamTargets)
    {
        return manager.RedownloadSteamMods(steamTargets);
    }

    public static async Task OpenFolderAsync(Window owner, ModReference modref)
    {
        string path = modref.path;
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            await DialogService.ShowMessageAsync(owner, "Mod folder not found.", "Open Folder");
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            await DialogService.ShowMessageAsync(owner, ex.Message, "Open Folder");
        }
    }

    public static async Task OpenSteamPageAsync(Window owner, ModReference modref)
    {
        if (!ModHearthManager.TryGetSteamWorkshopItemId(modref, out string steamId))
        {
            await DialogService.ShowMessageAsync(owner, "Steam ID not available for this mod.", "Open Steam Page");
            return;
        }

        string url = $"https://steamcommunity.com/sharedfiles/filedetails/?id={steamId}";
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            await DialogService.ShowMessageAsync(owner, ex.Message, "Open Steam Page");
        }
    }

    public static async Task CopyModIdAsync(Window owner, ModReference modref)
    {
        string? id = modref.ID;
        if (string.IsNullOrWhiteSpace(id))
            return;

        IClipboard? clipboard = TopLevel.GetTopLevel(owner)?.Clipboard;
        if (clipboard != null)
        {
            await clipboard.SetTextAsync(id);

            if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop &&
                desktop.MainWindow is UI.MainWindow mainWindow)
            {
                mainWindow.ShowNotification($"Copied ID: {id}", "copyIcon.svg");
            }
        }
    }

    private static void SetMenuItem(
        ContextMenu menu,
        string tag,
        bool isVisible,
        bool isEnabled,
        string? header = null)
    {
        foreach (MenuItem item in menu.Items.OfType<MenuItem>())
        {
            if (!string.Equals(item.Tag?.ToString(), tag, StringComparison.Ordinal))
                continue;

            item.IsVisible = isVisible;
            item.IsEnabled = isEnabled;
            if (!string.IsNullOrWhiteSpace(header))
                item.Header = header;
            return;
        }
    }
}
