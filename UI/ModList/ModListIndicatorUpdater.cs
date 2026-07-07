using System.Text;
using ModHearth.Utilities;

namespace ModHearth.UI;

internal static class ModListIndicatorUpdater
{
    public static void UpdateCachedIndicators(IEnumerable<ModRefViewModel> viewModels, HashSet<string>? cachedIds)
    {
        foreach (ModRefViewModel vm in viewModels)
            vm.IsCached = cachedIds != null && cachedIds.Contains(vm.DfMod.id);
    }

    public static List<DFHMod> UpdateProblemIndicators(ModHearthManager? manager, IEnumerable<ModRefViewModel> viewModels)
    {
        if (manager?.modproblems == null)
        {
            foreach (ModRefViewModel vm in viewModels)
            {
                vm.IsProblem = false;
                vm.ProblemTooltip = null;
            }

            return new List<DFHMod>();
        }

        Dictionary<string, List<ModProblem>> problemMap = new(StringComparer.OrdinalIgnoreCase);
        foreach (ModProblem problem in manager.modproblems)
        {
            if (!problemMap.TryGetValue(problem.problemThrowerID, out List<ModProblem>? list))
            {
                list = new List<ModProblem>();
                problemMap[problem.problemThrowerID] = list;
            }

            list.Add(problem);
        }

        List<DFHMod> problemMods = manager.enabledMods
            .Where(m => problemMap.ContainsKey(m.id))
            .ToList();

        foreach (ModRefViewModel vm in viewModels)
        {
            if (problemMap.TryGetValue(vm.DfMod.id, out List<ModProblem>? problems))
            {
                vm.IsProblem = true;
                vm.ProblemTooltip = BuildProblemTooltip(problems);
            }
            else
            {
                vm.IsProblem = false;
                vm.ProblemTooltip = null;
            }
        }

        return problemMods;
    }

    public static List<DFHMod> UpdateDuplicateWarningIndicators(
        ModHearthManager manager,
        IEnumerable<ModRefViewModel> viewModels)
    {
        IReadOnlyDictionary<string, List<string>> duplicateMap = manager.GetDuplicateWarningMap();

        List<DFHMod> duplicateWarningMods = manager.enabledMods
            .Where(m => duplicateMap.ContainsKey(m.id))
            .ToList();

        foreach (ModRefViewModel vm in viewModels)
        {
            if (duplicateMap.TryGetValue(vm.ModReference.ID, out List<string>? duplicates) &&
                duplicates.Count > 0)
            {
                vm.IsDuplicateWarning = true;
                vm.DuplicateWarningTooltip = BuildDuplicateWarningTooltip(manager, duplicates);
            }
            else
            {
                vm.IsDuplicateWarning = false;
                vm.DuplicateWarningTooltip = null;
            }
        }

        return duplicateWarningMods;
    }

    public static string BuildProblemTooltip(List<ModProblem> problems)
    {
        StringBuilder builder = new StringBuilder("Problems:");
        foreach (ModProblem problem in problems)
            builder.AppendLine().Append(problem.ToString());
        return builder.ToString();
    }

    public static string BuildDuplicateWarningTooltip(ModHearthManager? manager, IEnumerable<string> duplicates)
    {
        string errorLogPath = ConfigManager.GetErrorLogPath() ?? "errorlog.txt";
        StringBuilder builder = new StringBuilder($"Duplicate raw definitions ({errorLogPath}):");
        foreach (string entry in duplicates)
            builder.AppendLine().Append(entry);
        return builder.ToString();
    }

    public static string? BuildWarningIssuesTooltip(int problemCount, int duplicateCount)
    {
        if (problemCount <= 0 && duplicateCount <= 0)
            return null;

        string problemText;
        if (problemCount > 0)
            problemText = $"{problemCount} mod{(problemCount == 1 ? string.Empty : "s")} with issues";
        else
            problemText = string.Empty;

        string duplicateText;
        if (duplicateCount > 0)
            duplicateText = $"{duplicateCount} mod{(duplicateCount == 1 ? string.Empty : "s")} with duplicate raws";
        else
            duplicateText = string.Empty;

        if (string.IsNullOrEmpty(problemText))
            return duplicateText;
        if (string.IsNullOrEmpty(duplicateText))
            return problemText;

        return $"{problemText}{Environment.NewLine}{duplicateText}";
    }
}
