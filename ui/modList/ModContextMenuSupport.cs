using System.Collections;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Avalonia.Media;
using ModHearth.Config;
using ModHearth.Manager;
using ModHearth.Models;
using ModHearth.UI.Dialogs;
using ModHearth.UI.Helpers;
using ModHearth.UI.ViewModels;
using ModHearth.Utils.IO;
using ModHearth.Utils.Steam;

namespace ModHearth.UI.ModList
{
    internal readonly record struct ModContextMenuState(
        int LocalCount,
        int SteamCount,
        int SteamRedownloadCount,
        int LocalSteamCmdRedownloadCount,
        int CacheOnlyCount,
        bool CanOpenFolder,
        bool HasSteamPage,
        bool ShowSteamActionsForContext,
        bool IsSteamLocal,
        bool IsSteamCmdAvailable)
    {
        public bool HasLocalActions => LocalCount > 0;
        public bool HasSteamActions => ShowSteamActionsForContext && SteamCount > 0;
        public bool HasCacheOnlyActions => CacheOnlyCount > 0;
        public int TotalRedownloadCount => SteamRedownloadCount + LocalSteamCmdRedownloadCount;
        public bool HasRedownloadActions => TotalRedownloadCount > 0;
    }

    internal static class ModContextMenuSupport
    {
        public const string DeleteTag = "delete-mod";
        public const string DeleteCacheModTag = "delete-cache-mod";
        public const string UnsubscribeTag = "unsubscribe-steam";
        public const string RedownloadTag = "redownload-steam";
        public const string OpenFolderTag = "open";
        public const string OpenSteamTag = "open-steam";
        public const string CopyIdTag = "copy-id";
        public const string SetModColorTag = "set-mod-color";
        public const string DeleteDuplicateModTag = "delete-duplicate-mod-root";

        /// <summary>
        /// Runs the default action for a context-menu item that maps directly onto one of the shared
        /// confirm dialogs, so windows using the standard action set don't repeat the tag switch.
        /// </summary>
        public static async Task DispatchDefaultActionAsync(
            Window owner,
            ModHearthManager manager,
            MenuItem item,
            ModReference modref)
        {
            switch (item.Tag?.ToString())
            {
                case DeleteTag:
                    _ = await DeleteLocalModsWithConfirmAsync(owner, manager, [modref]);
                    break;
                case DeleteCacheModTag:
                    _ = await DeleteCacheOnlyModsWithConfirmAsync(owner, manager, [modref]);
                    break;
                case UnsubscribeTag:
                    await UnsubscribeSteamWithConfirmAsync(owner, manager, [modref]);
                    break;
                case RedownloadTag:
                    await RedownloadSteamWithConfirmAsync(owner, manager, [modref]);
                    break;
                case OpenFolderTag:
                    await OpenFolderAsync(owner, modref);
                    break;
                case OpenSteamTag:
                    await OpenSteamPageAsync(owner, modref);
                    break;
                case CopyIdTag:
                    await CopyModIdAsync(owner, modref);
                    break;
            }
        }

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
                // If the MenuItem itself doesn't have the T, we check if the MenuItem's parent ContextMenu has it.
                if (menuItem.Parent is ContextMenu menu && menu.DataContext is T menuContext)
                    contextItem = menuContext;
                else
                    return false;
            }

            List<T> selected = selectedItems?.Cast<T>().ToList() ?? [];
            IEnumerable<T> targets = selected.Count > 0 && selected.Contains(contextItem)
                ? selected
                : new[] { contextItem };
            modReferences = [.. targets.Select(getModReference)];
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

            bool canOpenFolder = !string.IsNullOrWhiteSpace(contextMod.Path) && Directory.Exists(contextMod.Path);
            bool hasSteamPage = ModHearthManager.TryGetSteamWorkshopItemId(contextMod, out _);

            ModSourceContext sourceContext = ModSourceContext.FromConfig();

            (_, _, _, bool isSteamLocal, _) = ModSourceClassifier.Classify(contextMod, sourceContext);

            bool isSteamCmdAvailable = new SteamCmdService().IsAvailable();

            int steamRedownloadCount = steamMods.Count;

            // SteamLocalMods local folders are ignored on purpose for SteamCMD redownloads
            int localSteamCmdRedownloadCount = isSteamCmdAvailable
                ? localMods.Count(m =>
                {
                    if (!ModHearthManager.TryGetSteamWorkshopItemId(m, out _))
                        return false;

                    (_, _, _, bool isSteamLocalMod, _) = ModSourceClassifier.Classify(m, sourceContext);

                    return !isSteamLocalMod;
                })
                : 0;

            int cacheOnlyCount = GetCacheOnlyDeletableMods(selection).Count;

            return new ModContextMenuState(
                localMods.Count,
                steamMods.Count,
                steamRedownloadCount,
                localSteamCmdRedownloadCount,
                cacheOnlyCount,
                canOpenFolder,
                hasSteamPage,
                steamMods.Count > 0,
                isSteamLocal,
                isSteamCmdAvailable);
        }

        public static void ApplyState(ContextMenu menu, ModContextMenuState state)
        {
            string deleteText = BuildDeleteText(state.LocalCount, state.IsSteamLocal);
            string deleteCacheText = BuildDeleteCacheText(state.CacheOnlyCount);
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
                DeleteCacheModTag,
                state.HasCacheOnlyActions,
                state.HasCacheOnlyActions,
                deleteCacheText);
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

        public static string BuildDeleteCacheText(int cacheOnlyCount)
            => cacheOnlyCount > 1 ? $"Delete {cacheOnlyCount} cache-only mods" : "Delete cache-only mod";

        /// <summary>
        /// Returns the selection's references that point at deletable folders inside Dwarf Fortress's
        /// disposable installed_mods cache, de-duplicated by canonical path so a multi-selection
        /// cannot delete the same folder twice.
        /// </summary>
        public static List<ModReference> GetCacheOnlyDeletableMods(IEnumerable<ModReference>? mods)
        {
            List<ModReference> targets = [];
            if (mods == null)
                return targets;

            HashSet<string> seenPaths = new(PathHelper.Comparer);
            foreach (ModReference modref in mods)
            {
                if (!ModHearthManager.CanDeleteCacheOnlyMod(modref))
                    continue;

                string canonicalPath = ConfigManager.ResolveCanonicalPath(modref.Path);
                if (string.IsNullOrWhiteSpace(canonicalPath) || !seenPaths.Add(canonicalPath))
                    continue;

                targets.Add(modref);
            }

            return targets;
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

        public static string BuildDeletePrompt(IReadOnlyCollection<ModReference> localTargets) => localTargets.Count == 1
                ? $"Delete '{localTargets.First().Name}' from the Mods folder?"
                : $"Delete {localTargets.Count} mods from the Mods folder?";

        public static string BuildDeleteCachePrompt(IReadOnlyCollection<ModReference> cacheTargets) => cacheTargets.Count == 1
                ? $"Delete '{cacheTargets.First().Name}' from the installed_mods cache?\n\n" +
                  "Dwarf Fortress recreates this disposable copy the next time the mod is enabled."
                : $"Delete {cacheTargets.Count} cache-only mods from the installed_mods cache?\n\n" +
                  "Dwarf Fortress recreates these disposable copies the next time the mods are enabled.";

        public static string BuildUnsubscribePrompt(IReadOnlyCollection<ModReference> steamTargets) => steamTargets.Count == 1
                ? $"Unsubscribe from '{steamTargets.First().Name}' on Steam Workshop?"
                : $"Unsubscribe from {steamTargets.Count} Steam mods?";

        public static string BuildRedownloadPrompt(IReadOnlyCollection<ModReference> steamTargets) => steamTargets.Count == 1
                ? $"Redownload '{steamTargets.First().Name}' from Steam Workshop?"
                : $"Redownload {steamTargets.Count} Steam mods?";

        public static Task<bool> DeleteLocalModsWithConfirmAsync(
            Window owner,
            ModHearthManager manager,
            IEnumerable<ModReference> modReferences,
            bool requestUIReload = true)
            => RunSplitConfirmedActionAsync(
                owner,
                modReferences,
                mods => ResolveLocalTargets(manager, mods),
                "Selected mods cannot be deleted from the Mods folder.",
                "Delete Mod",
                BuildDeletePrompt,
                targets => DeleteLocalMods(manager, targets, requestUIReload));

        public static Task<bool> DeleteCacheOnlyModsWithConfirmAsync(
            Window owner,
            ModHearthManager manager,
            IEnumerable<ModReference> modReferences,
            bool requestUIReload = true)
            => RunSplitConfirmedActionAsync(
                owner,
                modReferences,
                GetCacheOnlyDeletableMods,
                "Selected mods are not deletable installed_mods cache entries.",
                "Delete Cache-Only Mod",
                BuildDeleteCachePrompt,
                targets => DeleteCacheOnlyMods(manager, targets, requestUIReload));

        public static Task UnsubscribeSteamWithConfirmAsync(
            Window owner,
            ModHearthManager manager,
            IEnumerable<ModReference> modReferences)
            => RunSplitConfirmedActionAsync(
                owner,
                modReferences,
                mods => ResolveSteamTargets(manager, mods),
                "Selected mods are not Steam Workshop mods.",
                "Unsubscribe Steam Mod",
                BuildUnsubscribePrompt,
                targets => UnsubscribeSteamMods(manager, targets));

        public static Task RedownloadSteamWithConfirmAsync(
            Window owner,
            ModHearthManager manager,
            IEnumerable<ModReference> modReferences)
            => RunSplitConfirmedActionAsync(
                owner,
                modReferences,
                mods => ResolveSteamTargets(manager, mods),
                "Selected mods are not Steam Workshop mods.",
                "Redownload Steam Mod",
                BuildRedownloadPrompt,
                targets => RedownloadSteamMods(manager, targets));

        /// <summary>
        /// Shared skeleton for the four context-menu actions: resolve the actionable targets, bail
        /// out with a message when there are none, confirm, then run the action on a worker thread.
        /// </summary>
        private static async Task<bool> RunSplitConfirmedActionAsync(
            Window owner,
            IEnumerable<ModReference> modReferences,
            Func<IEnumerable<ModReference>, List<ModReference>> resolveTargets,
            string noTargetsMessage,
            string title,
            Func<List<ModReference>, string> prompt,
            Func<List<ModReference>, List<string>> action)
        {
            List<ModReference> targets = resolveTargets(modReferences);
            if (targets.Count == 0)
            {
                await DialogService.ShowMessageAsync(owner, noTargetsMessage, title);
                return false;
            }

            return await DialogService.RunConfirmedActionAsync(owner, prompt(targets), title, () => action(targets));
        }

        private static List<ModReference> ResolveLocalTargets(ModHearthManager manager, IEnumerable<ModReference> modReferences)
        {
            manager.SplitActionableMods(modReferences, out List<ModReference> localTargets, out _);
            return localTargets;
        }

        private static List<ModReference> ResolveSteamTargets(ModHearthManager manager, IEnumerable<ModReference> modReferences)
        {
            manager.SplitActionableMods(modReferences, out _, out List<ModReference> steamTargets);
            return steamTargets;
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

        public static List<string> DeleteLocalMods(ModHearthManager manager, IEnumerable<ModReference> localTargets, bool requestUIReload = true)
        {
            using (manager.BeginDeletionBatch())
            {
                return RunDeletionBatch(
                    localTargets,
                    modref => manager.DeleteModFromModsFolder(modref, out string message, requestUIReload) ? null : message);
            }
        }

        public static List<string> DeleteCacheOnlyMods(ModHearthManager manager, IEnumerable<ModReference> cacheTargets, bool requestUIReload = true)
        {
            using (manager.BeginDeletionBatch())
            {
                return RunDeletionBatch(
                    cacheTargets,
                    modref => manager.DeleteCacheOnlyMod(modref, out string message, requestUIReload) ? null : message);
            }
        }

        /// <summary>
        /// Deletes each target inside the caller's deletion batch, collecting per-mod failure
        /// messages. A null/empty return from <paramref name="delete"/> means success.
        /// </summary>
        private static List<string> RunDeletionBatch(
            IEnumerable<ModReference> targets,
            Func<ModReference, string?> delete)
        {
            List<string> failures = [];
            foreach (ModReference modref in targets)
            {
                string? failure = delete(modref);
                if (!string.IsNullOrEmpty(failure))
                    failures.Add(failure);
            }

            return failures;
        }

        public static List<string> UnsubscribeSteamMods(ModHearthManager manager, IEnumerable<ModReference> steamTargets) => manager.UnsubscribeSteamMods(steamTargets);

        public static List<string> RedownloadSteamMods(ModHearthManager manager, IEnumerable<ModReference> steamTargets) => manager.RedownloadSteamMods(steamTargets);

        public static async Task OpenFolderAsync(Window owner, ModReference modref)
        {
            string path = modref.Path;
            if (ConfigManager.GetOpenSteamFolder() &&
                ConfigManager.IsLikelySteamShadowCopy(modref.Path, modref.SteamID, out string workshopId) &&
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

            _ = await ShellLauncher.TryOpenAsync(owner, path, "Open Folder");
        }

        public static async Task OpenSteamPageAsync(Window owner, ModReference modref)
        {
            string actionTitle = ConfigManager.GetOpenSteamInClient() ? "Open URL in steam" : "Open URL in browser";

            if (!ModHearthManager.TryGetSteamWorkshopItemId(modref, out string steamId))
            {
                await DialogService.ShowMessageAsync(owner, "Steam ID not available for this mod.", actionTitle);
                return;
            }

            string url = ConfigManager.GetOpenSteamInClient()
                ? $"steam://url/CommunityFilePage/{steamId}"
                : $"https://steamcommunity.com/sharedfiles/filedetails/?id={steamId}";

            _ = await ShellLauncher.TryOpenAsync(owner, url, actionTitle);
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

                if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop &&
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
            IReadOnlyDictionary<string, List<ModReference>> duplicateRefs = manager.GetDuplicateModRefs();
            bool hasDuplicates = duplicateRefs.TryGetValue(key, out List<ModReference>? duplicates) && duplicates != null && duplicates.Count > 1;

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
                    Text = modRefToDel.Path,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    MaxWidth = 350,
                };
                ToolTip.SetTip(textBlock, $"Delete folder: {modRefToDel.Path}");

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

                    bool confirm = await DialogService.ShowConfirmAsync(owner, $"Delete duplicate mod folder '{modRefToDel.Path}'?", "Delete Duplicate Mod");

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
}
