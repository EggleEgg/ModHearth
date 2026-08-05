namespace ModHearth.UI;

internal static class MainWindowModListBuilder
{
    public static void SyncViewModels(ModHearthManager manager, IDictionary<string, ModRefViewModel> modViewMap)
    {
        modViewMap.Clear();
        string modsFolderPath = ConfigManager.GetModsPath();
        string vanillaFolderPath = ConfigManager.GetVanillaModsPath();

        List<DFHMod> modList = manager.modPool.ToList();

        // ModRefViewModel construction, source classification, and RefreshStyle() are
        // independent per mod. Computed into an indexed array and merged into modViewMap
        // sequentially (Dictionary writes aren't thread-safe even though the per-item work is).
        (string Key, ModRefViewModel Vm)?[] results = new (string, ModRefViewModel)?[modList.Count];
        _ = Parallel.For(0, modList.Count, new ParallelOptions
        {
            MaxDegreeOfParallelism = Environment.ProcessorCount
        }, i =>
        {
            DFHMod dfm = modList[i];
            string key = dfm.ToString();
            ModReference modref = manager.GetModRef(key);
            results[i] = (key, CreateViewModel(modref, modsFolderPath, vanillaFolderPath));
        });

        for (int i = 0; i < results.Length; i++)
        {
            (string Key, ModRefViewModel Vm)? result = results[i];

            if (result != null)
                modViewMap[result.Value.Key] = result.Value.Vm;
        }
    }

    public static ModRefViewModel CreateViewModel(ModReference modref, string modsFolderPath, string vanillaFolderPath)
    {
        ModRefViewModel vm = new ModRefViewModel(modref);
        ApplyClassification(vm, modref, modsFolderPath, vanillaFolderPath);
        return vm;
    }

    public static void ApplyClassification(ModRefViewModel vm, ModReference modref, string modsFolderPath, string vanillaFolderPath)
    {
        (bool isVanilla, bool isLocal, bool isSteam, bool isSteamLocal) = ModSourceClassifier.Classify(modref, modsFolderPath, vanillaFolderPath);
        vm.IsVanillaModSource = isVanilla;
        vm.IsLocalModSource = isLocal;
        vm.IsSteamModSource = isSteam;
        vm.IsSteamLocalModSource = isSteamLocal;
        vm.RefreshStyle();
    }

    public static void CopyClassification(ModRefViewModel target, ModRefViewModel source)
    {
        target.IsVanillaModSource = source.IsVanillaModSource;
        target.IsLocalModSource = source.IsLocalModSource;
        target.IsSteamModSource = source.IsSteamModSource;
        target.IsSteamLocalModSource = source.IsSteamLocalModSource;
        target.RefreshStyle();
    }

    public static List<ModRefViewModel> CollapseByModId(IEnumerable<ModRefViewModel> viewModels)
    {
        Dictionary<string, ModRefViewModel> best = new(StringComparer.OrdinalIgnoreCase);

        foreach (ModRefViewModel vm in viewModels)
        {
            string id = vm.ModReference.ID?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(id))
                continue;

            if (!best.TryGetValue(id, out ModRefViewModel? existing) || IsPreferredForCollapsing(vm, existing))
                best[id] = vm;
        }

        return best.Values.ToList();
    }

    private static bool IsPreferredForCollapsing(ModRefViewModel candidate, ModRefViewModel current)
    {
        int candidateRank = CollapsingRank(candidate);
        int currentRank = CollapsingRank(current);
        if (candidateRank != currentRank)
            return candidateRank > currentRank;

        return (candidate.LastModifiedTime ?? DateTime.MinValue) > (current.LastModifiedTime ?? DateTime.MinValue);
    }

    private static int CollapsingRank(ModRefViewModel vm)
    {
        if (vm.IsSteamLocalModSource) return 3;
        if (vm.IsLocalModSource) return 2;
        if (vm.IsSteamModSource) return 1;
        return 0;
    }

    public static List<ModRefViewModel> BuildInactiveList(
        ModHearthManager manager,
        IReadOnlyDictionary<string, ModRefViewModel> modViewMap)
    {
        var vms = manager.disabledMods
            .OrderBy(m => manager.GetRefFromDFHMod(m).name ?? string.Empty)
            .Select(m => modViewMap.TryGetValue(m.ToString(), out ModRefViewModel? vm) ? vm : null)
            .Where(vm => vm != null)
            .Cast<ModRefViewModel>()
            .ToList();
        return CollapseByModId(vms);
    }

    public static List<ModRefViewModel> BuildActiveList(
        ModHearthManager manager,
        IReadOnlyDictionary<string, ModRefViewModel> modViewMap)
    {
        var vms = manager.enabledMods
            .Select(m => modViewMap.TryGetValue(m.ToString(), out ModRefViewModel? vm) ? vm : null)
            .Where(vm => vm != null)
            .Cast<ModRefViewModel>()
            .ToList();
        return CollapseByModId(vms);
    }
}