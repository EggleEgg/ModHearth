using System.Text;
using Avalonia.Media;
using ModHearth.Utilities;

namespace ModHearth.UI;

internal static class ModListIndicatorUpdater
{
    public static void UpdateCachedIndicators(IEnumerable<ModRefViewModel> viewModels, HashSet<string>? cachedIds)
    {
        foreach (ModRefViewModel vm in viewModels)
            vm.IsCached = cachedIds != null && cachedIds.Contains(vm.DfMod.id);
    }

    public static void UpdateRelationshipBadges(
        IEnumerable<ModRefViewModel> viewModels,
        IReadOnlyDictionary<string, ModRelationshipRule> rules)
    {
        List<ModRefViewModel> mods = viewModels.ToList();
        Dictionary<string, ModRefViewModel> byId = mods
            .Where(vm => !string.IsNullOrWhiteSpace(vm.ModReference.ID))
            .GroupBy(vm => vm.ModReference.ID.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        foreach (ModRefViewModel vm in mods)
        {
            string id = vm.ModReference.ID?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(id) ||
                rules == null ||
                !rules.TryGetValue(id, out ModRelationshipRule? rule) ||
                rule.IsEmpty)
            {
                vm.RuleBadges = Array.Empty<RuleBadgeInfo>();
                vm.RuleBadgesTooltip = null;
                vm.RelationshipCount = 0;
                continue;
            }

            List<RuleBadgeInfo> badges = [];

            if (rule.BeforeIds.Count > 0)
                badges.Add(CreateBadgeInfo("arrowUpIcon.svg", rule.BeforeIds.Count, RelationshipBrush(ModRelationshipKind.Before)));
            if (rule.AfterIds.Count > 0)
                badges.Add(CreateBadgeInfo("arrowDownIcon.svg", rule.AfterIds.Count, RelationshipBrush(ModRelationshipKind.After)));
            if (rule.RequiredIds.Count > 0)
                badges.Add(CreateBadgeInfo("linkIcon.svg", rule.RequiredIds.Count, RelationshipBrush(ModRelationshipKind.Required)));
            if (rule.IncompatibleIds.Count > 0)
                badges.Add(CreateBadgeInfo("cancelCircleIcon.svg", rule.IncompatibleIds.Count, RelationshipBrush(ModRelationshipKind.Incompatible)));

            vm.RuleBadges = badges;
            vm.RuleBadgesTooltip = BuildRelationshipTooltip(rule, byId);
            vm.RelationshipCount = rule.BeforeIds.Count + rule.AfterIds.Count + rule.RequiredIds.Count + rule.IncompatibleIds.Count;
            vm.BeforeCount = rule.BeforeIds.Count;
            vm.AfterCount = rule.AfterIds.Count;
            vm.RequiredCount = rule.RequiredIds.Count;
            vm.IncompatibleCount = rule.IncompatibleIds.Count;
        }
    }

    // Helper method to generate a unified Icon + Number badge control
    private static RuleBadgeInfo CreateBadgeInfo(string iconName, int count, IBrush iconBrush)
    {
        Color? tint = (iconBrush as ISolidColorBrush)?.Color;
        return new RuleBadgeInfo(ImageSourceLoader.LoadFromAssetUri(iconName, tint), count);
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

            return [];
        }

        Dictionary<string, List<ModProblem>> problemMap = new(StringComparer.OrdinalIgnoreCase);
        foreach (ModProblem problem in manager.modproblems)
        {
            if (!problemMap.TryGetValue(problem.problemThrowerID, out List<ModProblem>? list))
            {
                list = [];
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
                vm.DuplicateWarningTooltip = BuildDuplicateWarningTooltip(manager, vm.ModReference.ID, duplicates);
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
        StringBuilder builder = new("Problems:");
        foreach (ModProblem problem in problems)
            _ = builder.AppendLine().Append(problem.ToString());
        return builder.ToString();
    }

    public static string BuildDuplicateWarningTooltip(ModHearthManager? manager, string modId, IEnumerable<string> duplicates)
    {
        StringBuilder builder = new();

        bool hasCache = duplicates.Any(d => d.Contains("[Cache]"));
        bool hasErrorLog = manager != null && manager.HasErrorLogDuplicateWarning(modId);

        if (hasErrorLog)
        {
            string errorLogPath = ConfigManager.GetErrorLogPath() ?? "errorlog.txt";
            _ = builder.Append($"Duplicate raw definitions ({errorLogPath}):");
        }
        else if (hasCache)
        {
            string cachePath = ModRawDependencyCacheStore.trimmedCachePath ?? "mod_raw_dependency_cache.json";
            _ = builder.Append($"Potential duplicate raw definitions ({cachePath}):");
        }

        _ = builder.AppendLine().AppendLine();

        if (manager != null)
        {
            var groups = manager.GetDuplicateWarningGroups();
            var offendingModIds = groups
                .Where(g => g.Contains(modId))
                .SelectMany(g => g)
                .Where(id => !string.Equals(id, modId, StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (offendingModIds.Count > 0)
            {
                _ = builder.Append("Offending mods:");
                foreach (string id in offendingModIds)
                {
                    string name;
                    try
                    {
                        name = manager.GetModRef(id).name;
                    }
                    catch
                    {
                        name = id;
                    }
                    _ = builder.AppendLine().Append("- ").Append(name);
                }
                _ = builder.AppendLine();
            }
        }

        foreach (string entry in duplicates)
            _ = builder.AppendLine().Append(entry);
        return builder.ToString();
    }

    private static string BuildRelationshipTooltip(
        ModRelationshipRule rule,
        IReadOnlyDictionary<string, ModRefViewModel> byId)
    {
        StringBuilder builder = new("Relationships:");
        AppendCategory(builder, "Before", rule.BeforeIds, byId);
        AppendCategory(builder, "After", rule.AfterIds, byId);
        AppendCategory(builder, "Required", rule.RequiredIds, byId);
        AppendCategory(builder, "Incompatible", rule.IncompatibleIds, byId);
        return builder.ToString();
    }

    private static void AppendCategory(
        StringBuilder builder,
        string title,
        IReadOnlyList<string> ids,
        IReadOnlyDictionary<string, ModRefViewModel> byId)
    {
        if (ids.Count == 0)
            return;

        _ = builder.AppendLine().Append(title).Append(':');
        foreach (string id in ids
            .OrderBy(value => byId.TryGetValue(value, out ModRefViewModel? vm) ? vm.DisplayName : value, StringComparer.OrdinalIgnoreCase))
        {
            string label = byId.TryGetValue(id, out ModRefViewModel? vm)
                ? $"{vm.DisplayName} ({id})"
                : $"Missing Mod ({id})";
            _ = builder.AppendLine().Append("- ").Append(label);
        }
    }

    public static string? BuildWarningIssuesTooltip(int problemCount, int duplicateCount)
    {
        if (problemCount <= 0 && duplicateCount <= 0)
            return null;

        string problemText;
        switch (problemCount)
        {
            case > 0:
                problemText = $"{problemCount} mod{(problemCount == 1 ? string.Empty : "s")} with issues";
                break;
            default:
                problemText = string.Empty;
                break;
        }

        string duplicateText;
        switch (duplicateCount)
        {
            case > 0:
                duplicateText = $"{duplicateCount} mod{(duplicateCount == 1 ? string.Empty : "s")} with duplicate raws";
                break;
            default:
                duplicateText = string.Empty;
                break;
        }

        if (string.IsNullOrEmpty(problemText))
            return duplicateText;
        if (string.IsNullOrEmpty(duplicateText))
            return problemText;

        return $"{problemText}{Environment.NewLine}{duplicateText}";
    }
    public static IBrush RelationshipBrush(ModRelationshipKind kind)
    {
        return kind switch
        {
            ModRelationshipKind.Before => BrushCache.GetBrush(Color.Parse("#3B82F6")),
            ModRelationshipKind.After => BrushCache.GetBrush(Color.Parse("#22C55E")),
            ModRelationshipKind.Required => BrushCache.GetBrush(Color.Parse("#EAB308")),
            ModRelationshipKind.Incompatible => BrushCache.GetBrush(Color.Parse("#EF4444")),
            _ => Brushes.Gray
        };
    }
}
