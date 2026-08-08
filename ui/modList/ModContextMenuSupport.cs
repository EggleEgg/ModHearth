using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia;
using Avalonia.Layout;
using Avalonia.Media;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ModHearth.Utilities.Steam;
using Avalonia.Controls.ApplicationLifetimes;

namespace ModHearth.UI;

internal readonly record struct ModContextMenuState(
    int LocalCount,
    int SteamCount,
    int SteamRedownloadCount,
    int LocalSteamCmdRedownloadCount,
    bool CanOpenFolder,
    bool HasSteamPage,
    bool ShowSteamActionsForContext,
    bool IsSteamLocal,
    bool IsSteamCmdAvailable)
{
    public bool HasLocalActions => LocalCount > 0;
    public bool HasSteamActions => ShowSteamActionsForContext && SteamCount > 0;
    public int TotalRedownloadCount => SteamRedownloadCount + LocalSteamCmdRedownloadCount;
    public bool HasRedownloadActions => TotalRedownloadCount > 0;
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
    public const string DeleteDuplicateModTag = "delete-duplicate-mod-root";

    public static void PrepareContextMenu(
        ContextMenu menu,
        ModHearthManager manager,
        ModReference contextMod,
        IEnumerable<ModReference> selectedMods)
    {
        ContextMenuCoordinator.Activate(menu);
        ModContextMenuState state = BuildState(manager, contextMod, selectedMods);
        ApplyState(menu, state);
        ConfigureDuplicateModSubmenu(menu, manager, contextMod);
    }

    public static void EnsureContextItemSelected(IList? selectedItems, object contextItem)
    {
        if (selectedItems == null)
            return;

        if (selectedItems.Count == 0 || !selectedItems.Contains(contextItem))
        {
            selectedItems.Clear();
            _ = selectedItems.Add(contextItem);
        }
    }

    public static bool TryGetContextModReferences<T>(
        object? sender,
        IList? selectedItems,
        Func<T, ModReference> getModReference,
        out List<ModReference> modReferences) where T : class
    {
        modReferences = [];
        if (sender is not MenuItem menuItem)
            return false;

        if (menuItem.DataContext is not T contextItem)
        {
            // The menu item might inherit DataContext from its parent context menu or placement target.
            // If the MenuItem itself doesn't have the T, we check if the MenuItem's parent ContextMenu has it, or App's current context menu VM.
            switch (menuItem.Parent)
            {
                case ContextMenu menu when menu.DataContext is T menuContext:
                    contextItem = menuContext;
                    break;
                default:
                    if (Application.Current is App app && app.GetCurrentContextMenuVm() is T appVm)
                    {
                        contextItem = appVm;
                        break;
                    }
                    return false;
            }
        }

        List<T> selected = selectedItems?.Cast<T>().ToList() ?? [];
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
            ?? [];
        manager.SplitActionableMods(selection, out List<ModReference> localMods, out List<ModReference> steamMods);

        bool canOpenFolder = !string.IsNullOrWhiteSpace(contextMod.path) && Directory.Exists(contextMod.path);
        bool hasSteamPage = ModHearthManager.TryGetSteamWorkshopItemId(contextMod, out _);

        (_, _, _, bool isSteamLocal) = ModSourceClassifier.Classify(
            contextMod,
            ModHearthManager.GetModsPath(),
            ModHearthManager.GetVanillaModsPath());

        bool isSteamCmdAvailable = new SteamCmdService().IsAvailable();

        int steamRedownloadCount = steamMods.Count;

        // SteamLocalMods local folders are ignored on purpose for SteamCMD redownloads
        int localSteamCmdRedownloadCount = isSteamCmdAvailable
            ? localMods.Count(m =>
            {
                if (!ModHearthManager.TryGetSteamWorkshopItemId(m, out _))
                    return false;

                (_, _, _, bool isSteamLocalMod) = ModSourceClassifier.Classify(
                    m,
                    ModHearthManager.GetModsPath(),
                    ModHearthManager.GetVanillaModsPath());

                return !isSteamLocalMod;
            })
            : 0;

        return new ModContextMenuState(
            localMods.Count,
            steamMods.Count,
            steamRedownloadCount,
            localSteamCmdRedownloadCount,
            canOpenFolder,
            hasSteamPage,
            steamMods.Count > 0,
            isSteamLocal,
            isSteamCmdAvailable);
    }

    public static void ApplyState(ContextMenu menu, ModContextMenuState state)
    {
        string deleteText = BuildDeleteText(state.LocalCount, state.IsSteamLocal);
        string unsubscribeText = BuildUnsubscribeText(state.SteamCount, state.IsSteamLocal);
        string redownloadText = BuildRedownloadText(state.SteamRedownloadCount, state.LocalSteamCmdRedownloadCount, state.IsSteamLocal);

        SetMenuItem(
            menu,
            DeleteTag,
            state.HasLocalActions,
            state.HasLocalActions,
            deleteText);
        SetMenuItem(
            menu,
            UnsubscribeTag,
            state.HasSteamActions,
            state.HasSteamActions,
            unsubscribeText);
        SetMenuItem(
            menu,
            RedownloadTag,
            state.HasRedownloadActions,
            state.HasRedownloadActions,
            redownloadText);
        SetMenuItem(menu, OpenFolderTag, true, state.CanOpenFolder);
        SetMenuItem(menu, OpenSteamTag, state.HasSteamPage, state.HasSteamPage);
    }

    public static string BuildDeleteText(int localCount, bool isSteamLocal)
    {
        if (localCount > 1)
            return $"Delete {localCount} local mods";

        return isSteamLocal ? "Delete local mod copy" : "Delete local mod";
    }

    public static string BuildUnsubscribeText(int steamCount, bool isSteamLocal)
    {
        if (steamCount > 1)
            return $"Unsubscribe from {steamCount} steam mods";

        return isSteamLocal ? "Unsubscribe from steam mod copy" : "Unsubscribe from steam";
    }

    public static string BuildRedownloadText(int steamCount, int localSteamCmdCount, bool isSteamLocal)
    {
        if (steamCount > 0 && localSteamCmdCount > 0)
        {
            int total = steamCount + localSteamCmdCount;
            return $"Redownload {total} mods ({steamCount} Steam, {localSteamCmdCount} SteamCMD)";
        }

        if (localSteamCmdCount > 0)
        {
            return localSteamCmdCount == 1
                ? "Redownload using SteamCMD"
                : $"Redownload {localSteamCmdCount} mods using SteamCMD";
        }

        if (steamCount > 0)
        {
            if (steamCount == 1)
            {
                return isSteamLocal
                    ? "Redownload steam mod copy"
                    : "Redownload from steam";
            }

            return $"Redownload {steamCount} steam mods";
        }

        return "Redownload from steam";
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
        return await DialogService.RunConfirmedActionAsync(owner, prompt, "Delete Mod", () => DeleteLocalMods(manager, localTargets));
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
        await DialogService.RunConfirmedActionAsync(owner, prompt, "Unsubscribe Steam Mod", () => UnsubscribeSteamMods(manager, steamTargets));
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
        await DialogService.RunConfirmedActionAsync(owner, prompt, "Redownload Steam Mod", () => RedownloadSteamMods(manager, steamTargets));
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
        List<string> failures = [];
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
        if (ConfigManager.GetOpenSteamFolder() &&
            ConfigManager.IsLikelySteamShadowCopy(modref.path, modref.steamID, out string workshopId) &&
            !string.IsNullOrWhiteSpace(workshopId))
        {
            foreach (string workshopContentRoot in ConfigManager.GetSteamWorkshopContentPaths())
            {
                string steamFolder = Path.Combine(workshopContentRoot, workshopId);
                if (Directory.Exists(steamFolder))
                {
                    path = steamFolder;
                    break;
                }
            }
        }

        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            await DialogService.ShowMessageAsync(owner, "Mod folder not found.", "Open Folder");
            return;
        }

        try
        {
            using Process? process = Process.Start(new ProcessStartInfo
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
            string actionTitle = ConfigManager.GetOpenSteamInClient() ? "Open URL in steam" : "Open URL in browser";
            await DialogService.ShowMessageAsync(owner, "Steam ID not available for this mod.", actionTitle);
            return;
        }

        string url = ConfigManager.GetOpenSteamInClient()
            ? $"steam://url/CommunityFilePage/{steamId}"
            : $"https://steamcommunity.com/sharedfiles/filedetails/?id={steamId}";

        try
        {
            using Process? process = Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            string actionTitle = ConfigManager.GetOpenSteamInClient() ? "Open URL in steam" : "Open URL in browser";
            await DialogService.ShowMessageAsync(owner, ex.Message, actionTitle);
        }
    }

    public static async Task CopyModIdAsync(Window owner, ModReference modref)
    {
        string? id;
        if (ModHearthManager.TryGetSteamWorkshopItemId(modref, out string steamId))
        {
            id = ConfigManager.GetCopySteamFileId() ? steamId : modref.ID;
        }
        else
        {
            id = ConfigManager.GetCopySteamFileId() ? string.Empty : modref.ID;
        }


        if (string.IsNullOrWhiteSpace(id))
        {
            if (ConfigManager.GetCopySteamFileId())
            {
                await DialogService.ShowMessageAsync(owner, "Steam ID not available for this mod.", "Copy Steam File Id");
            }
            return;
        }

        IClipboard? clipboard = TopLevel.GetTopLevel(owner)?.Clipboard;
        if (clipboard != null)
        {
            await clipboard.SetTextAsync(id);

            if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop &&
                desktop.MainWindow is MainWindow mainWindow)
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

    public static void ConfigureRelationsMenu(MenuItem relationsRoot, ModRefViewModel vm)
    {
        bool hasRelationships = vm.HasRelationships;
        foreach (object? sub in relationsRoot.Items)
        {
            switch (sub)
            {
                case MenuItem subMenuItem:
                    {
                        string subTag = subMenuItem.Tag?.ToString() ?? string.Empty;
                        if (string.Equals(subTag, "relation-clear-all", StringComparison.Ordinal))
                        {
                            subMenuItem.IsVisible = hasRelationships;
                        }

                        break;
                    }

                case Separator sep:
                    sep.IsVisible = hasRelationships;
                    break;
            }
        }
    }

    public static void ConfigureDuplicateModSubmenu(
        ContextMenu menu,
        ModHearthManager manager,
        ModReference contextMod)
    {
        MenuItem? duplicateRoot = menu.Items
            .OfType<MenuItem>()
            .FirstOrDefault(item => string.Equals(item.Tag?.ToString(), DeleteDuplicateModTag, StringComparison.Ordinal));
        if (duplicateRoot == null)
            return;

        string key = contextMod.DFHackCompatibleString();
        var duplicateRefs = manager.GetDuplicateModRefs();
        bool hasDuplicates = duplicateRefs.TryGetValue(key, out var duplicates) && duplicates != null && duplicates.Count > 1;

        if (!hasDuplicates || duplicates == null)
        {
            duplicateRoot.IsVisible = false;
            duplicateRoot.ItemsSource = null;
            return;
        }

        duplicateRoot.IsVisible = true;
        duplicateRoot.Header = $"Delete a duplicate mod, {duplicates.Count} folders";

        List<MenuItem> items = [];
        foreach (ModReference dupRef in duplicates)
        {
            ModReference modRefToDel = dupRef;
            TextBlock textBlock = new()
            {
                Text = modRefToDel.path,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 350,
            };
            ToolTip.SetTip(textBlock, $"Delete folder: {modRefToDel.path}");

            MenuItem item = new()
            {
                Header = textBlock
            };

            item.Click += async (_, _) =>
            {
                // Safely resolve owner Window via PlacementTarget or Desktop Lifetime
                Window? owner = TopLevel.GetTopLevel(menu.PlacementTarget) as Window
                    ?? (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

                if (owner == null)
                    return;

                bool confirm = await DialogService.ShowConfirmAsync(owner, $"Delete duplicate mod folder '{modRefToDel.path}'?", "Delete Duplicate Mod");

                if (confirm)
                {
                    if (manager.DeleteModFromModsFolder(modRefToDel, out string msg))
                        ContextMenuCoordinator.DismissActive();
                    else
                        await DialogService.ShowMessageAsync(owner, msg, "Delete Duplicate Mod");
                }
            };

            items.Add(item);
        }

        duplicateRoot.ItemsSource = items;
    }
}
