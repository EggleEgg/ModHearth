using ModHearth.Models;
using ModHearth.UI.ViewModels;

namespace ModHearth.UI.ModList
{
    internal static class MainWindowModListBuilder
    {
        public static void SyncViewModels(ModHearthManager manager, IDictionary<string, ModRefViewModel> modViewMap)
        {
            modViewMap.Clear();
            string modsFolderPath = ConfigManager.GetModsPath();
            string vanillaFolderPath = ConfigManager.GetVanillaModsPath();

            List<DfhMod> modList = [.. manager.modPool];

            // ModRefViewModel construction, source classification, and RefreshStyle() are
            // independent per mod. Computed into an indexed array and merged into modViewMap
            // sequentially (Dictionary writes aren't thread-safe even though the per-item work is).
            (string Key, ModRefViewModel Vm)?[] results = new (string, ModRefViewModel)?[modList.Count];
            _ = Parallel.For(0, modList.Count, new ParallelOptions
            {
                MaxDegreeOfParallelism = Environment.ProcessorCount
            }, i =>
            {
                DfhMod dfm = modList[i];
                string key = dfm.ToString();
                ModReference? modref = manager.FindModRef(key);
                if (modref == null)
                    return; // catalog changed concurrently; the next refresh will pick it up

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
            ModRefViewModel vm = new(modref);
            ApplyClassification(vm, modref, modsFolderPath, vanillaFolderPath);
            return vm;
        }

        public static void ApplyClassification(ModRefViewModel vm, ModReference modref, string modsFolderPath, string vanillaFolderPath)
        {
            (bool isVanilla, bool isLocal, bool isSteam, bool isSteamLocal, bool isCacheOnly) = ModSourceClassifier.Classify(modref, modsFolderPath, vanillaFolderPath);
            vm.IsVanillaModSource = isVanilla;
            vm.IsLocalModSource = isLocal;
            vm.IsSteamModSource = isSteam;
            vm.IsSteamLocalModSource = isSteamLocal;
            vm.IsCacheOnlyModSource = isCacheOnly;
            vm.RefreshStyle();
        }

        public static void CopyClassification(ModRefViewModel target, ModRefViewModel source)
        {
            target.IsVanillaModSource = source.IsVanillaModSource;
            target.IsLocalModSource = source.IsLocalModSource;
            target.IsSteamModSource = source.IsSteamModSource;
            target.IsSteamLocalModSource = source.IsSteamLocalModSource;
            target.IsCacheOnlyModSource = source.IsCacheOnlyModSource;
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

            return [.. best.Values];
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
            // installed_mods cache copies are a fallback, never a reason to hide the real folder.
            if (vm.ModReference.IsIgnored) return -1;
            if (vm.IsSteamLocalModSource) return 3;
            if (vm.IsLocalModSource) return 2;
            if (vm.IsSteamModSource) return 1;
            return 0;
        }

        public static List<ModRefViewModel> BuildInactiveList(
            ModHearthManager manager,
            IReadOnlyDictionary<string, ModRefViewModel> modViewMap)
        {
            List<ModRefViewModel> vms = [.. manager.disabledMods
                .OrderBy(m => manager.FindModRef(m)?.Name ?? string.Empty)
                .Select(m => modViewMap.TryGetValue(m.ToString(), out ModRefViewModel? vm) ? vm : null)
                .Where(vm => vm != null)
                .Cast<ModRefViewModel>()];
            return CollapseByModId(vms);
        }

        public static List<ModRefViewModel> BuildActiveList(
            ModHearthManager manager,
            IReadOnlyDictionary<string, ModRefViewModel> modViewMap)
        {
            List<ModRefViewModel> vms = [.. manager.enabledMods
                .Select(m => modViewMap.TryGetValue(m.ToString(), out ModRefViewModel? vm) ? vm : null)
                .Where(vm => vm != null)
                .Cast<ModRefViewModel>()];
            return CollapseByModId(vms);
        }
    }
}
