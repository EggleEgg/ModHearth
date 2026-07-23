using ModHearth.Utilities;

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
        Parallel.For(0, modList.Count, new ParallelOptions
        {
            MaxDegreeOfParallelism = Environment.ProcessorCount
        }, i =>
        {
            DFHMod dfm = modList[i];
            string key = dfm.ToString();
            ModReference modref = manager.GetModRef(key);
            ModRefViewModel vm = new ModRefViewModel(modref);
            (bool isVanillaMod, bool isLocalMod, bool isSteamMod) = ModSourceClassifier.Classify(
                modref,
                modsFolderPath,
                vanillaFolderPath);
            vm.IsVanillaModSource = isVanillaMod;
            vm.IsLocalModSource = isLocalMod;
            vm.IsSteamModSource = isSteamMod;
            vm.RefreshStyle();
            results[i] = (key, vm);
        });

        foreach ((string Key, ModRefViewModel Vm)? result in results)
        {
            if (result != null)
                modViewMap[result.Value.Key] = result.Value.Vm;
        }
    }

    public static List<ModRefViewModel> BuildInactiveList(
        ModHearthManager manager,
        IReadOnlyDictionary<string, ModRefViewModel> modViewMap)
    {
        return manager.disabledMods
            .OrderBy(m => manager.GetRefFromDFHMod(m).name ?? string.Empty)
            .Select(m => modViewMap.TryGetValue(m.ToString(), out ModRefViewModel? vm) ? vm : null)
            .Where(vm => vm != null)
            .Cast<ModRefViewModel>()
            .ToList();
    }

    public static List<ModRefViewModel> BuildActiveList(
        ModHearthManager manager,
        IReadOnlyDictionary<string, ModRefViewModel> modViewMap)
    {
        return manager.enabledMods
            .Select(m => modViewMap.TryGetValue(m.ToString(), out ModRefViewModel? vm) ? vm : null)
            .Where(vm => vm != null)
            .Cast<ModRefViewModel>()
            .ToList();
    }
}