using ModHearth.Utilities;

namespace ModHearth.UI;

internal static class MainWindowModListBuilder
{
    public static void SyncViewModels(ModHearthManager manager, IDictionary<string, ModRefViewModel> modViewMap)
    {
        modViewMap.Clear();
        string modsFolderPath = ConfigManager.GetModsPath();
        string vanillaFolderPath = ConfigManager.GetVanillaModsPath();
        foreach (DFHMod dfm in manager.modPool)
        {
            ModReference modref = manager.GetModRef(dfm.ToString());
            ModRefViewModel vm = new ModRefViewModel(modref);
            (bool isVanillaMod, bool isLocalMod, bool isSteamMod) = ModSourceClassifier.Classify(
                modref,
                modsFolderPath,
                vanillaFolderPath);
            vm.IsVanillaModSource = isVanillaMod;
            vm.IsLocalModSource = isLocalMod;
            vm.IsSteamModSource = isSteamMod;
            vm.RefreshStyle();
            modViewMap[dfm.ToString()] = vm;
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
