using Avalonia.Controls;

namespace ModHearth.UI;

internal readonly record struct ModSelectionToken(string DfModKey, string ModId);

internal readonly record struct ModSelectionSnapshot(
    bool? IsLeftList,
    List<ModSelectionToken> Tokens,
    string? CurrentSelectedId,
    string? PreviousSelectedId);

internal readonly record struct SearchFilterStateSnapshot(
    string LeftText,
    bool LeftHideFiltered,
    SearchFilterMode LeftMode,
    string RightText,
    bool RightHideFiltered,
    SearchFilterMode RightMode);

internal static class MainWindowSelectionState
{
    public static List<ModSelectionToken> CaptureSelectionTokens(ListBox list)
    {
        if (list.SelectedItems == null || list.SelectedItems.Count == 0)
            return [];

        List<ModSelectionToken> tokens = [];
        HashSet<string> seenKeys = new(StringComparer.OrdinalIgnoreCase);
        foreach (ModRefViewModel vm in list.SelectedItems.OfType<ModRefViewModel>())
        {
            string key = vm.DfMod.ToString();
            if (string.IsNullOrWhiteSpace(key))
                continue;
            if (!seenKeys.Add(key))
                continue;

            string id = vm.ModReference.ID?.Trim() ?? string.Empty;
            tokens.Add(new ModSelectionToken(key, id));
        }

        return tokens;
    }

    public static List<ModRefViewModel> ResolveSelectionTokens(
        IEnumerable<ModRefViewModel> candidates,
        IReadOnlyList<ModSelectionToken> tokens)
    {
        Dictionary<string, ModRefViewModel> byKey = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, ModRefViewModel> byId = new(StringComparer.OrdinalIgnoreCase);
        foreach (ModRefViewModel vm in candidates)
        {
            string key = vm.DfMod.ToString();
            if (!string.IsNullOrWhiteSpace(key) && !byKey.ContainsKey(key))
                byKey[key] = vm;

            string id = vm.ModReference.ID?.Trim() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(id) && !byId.ContainsKey(id))
                byId[id] = vm;
        }

        List<ModRefViewModel> restored = [];
        HashSet<ModRefViewModel> seen = [];
        foreach (ModSelectionToken token in tokens)
        {
            ModRefViewModel? vm = null;
            if (!string.IsNullOrWhiteSpace(token.DfModKey))
                _ = byKey.TryGetValue(token.DfModKey, out vm);

            if (vm == null && !string.IsNullOrWhiteSpace(token.ModId))
                _ = byId.TryGetValue(token.ModId, out vm);

            if (vm == null || !seen.Add(vm))
                continue;

            restored.Add(vm);
        }

        return restored;
    }
}
