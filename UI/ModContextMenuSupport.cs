using Avalonia.Controls;
using Avalonia.Input.Platform;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

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
            contextSteamMods.Count > 0);
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
            await clipboard.SetTextAsync(id);
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
