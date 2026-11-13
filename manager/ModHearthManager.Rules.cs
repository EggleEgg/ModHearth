using System.Text.Json;
using ModHearth.Models;
using ModHearth.Utils;
using ModHearth.Utils.IO;
using ModHearth.Utils.Logging;

namespace ModHearth.Manager
{
    public partial class ModHearthManager
    {
        public IReadOnlyDictionary<string, ModRelationshipRule> GetModRelationshipRules()
        {
            lock (stateGate)
            {
                if (cachedClonedRelationshipRules != null)
                    return cachedClonedRelationshipRules;
                cachedClonedRelationshipRules = ModRelationshipRule.CloneAll(relationshipRules);
                return cachedClonedRelationshipRules;
            }
        }

        public int GetRelationshipRulesVersion()
        {
            lock (stateGate)
            {
                return relationshipRulesVersion;
            }
        }

        public void SetModRelationshipRules(IDictionary<string, ModRelationshipRule>? rules)
        {
            lock (stateGate)
            {
                relationshipRules = ModRelationshipRule.NormalizeAll(rules);
                sortRules = RelationshipRulesToSortRules(relationshipRules);
                cachedClonedRelationshipRules = null;
                relationshipRulesVersion++;
            }
            SaveRelationshipRules();
        }

        private static List<ModSortRule> RelationshipRulesToSortRules(
            IDictionary<string, ModRelationshipRule> rules)
        {
            List<ModSortRule> converted = [];
            foreach (KeyValuePair<string, ModRelationshipRule> kvp in rules)
            {
                string ownerId = kvp.Key.Trim();
                foreach (string targetId in kvp.Value.BeforeIds)
                    converted.Add(new ModSortRule { BeforeId = ownerId, AfterId = targetId });
                foreach (string targetId in kvp.Value.AfterIds)
                    converted.Add(new ModSortRule { BeforeId = targetId, AfterId = ownerId });
            }

            return NormalizeSortRules(converted);
        }

        private static List<ModSortRule> NormalizeSortRules(IEnumerable<ModSortRule>? rules)
        {
            List<ModSortRule> normalized = [];
            if (rules == null)
                return normalized;

            HashSet<string> seenEdges = new(StringComparer.OrdinalIgnoreCase);
            HashSet<string> seenRequires = new(StringComparer.OrdinalIgnoreCase);
            foreach (ModSortRule rule in rules)
            {
                if (rule == null)
                    continue;

                string before = rule.BeforeId?.Trim() ?? string.Empty;
                string after = rule.AfterId?.Trim() ?? string.Empty;
                string requires = rule.RequiresId?.Trim() ?? string.Empty;

                bool hasEdge = !string.IsNullOrWhiteSpace(before) &&
                               !string.IsNullOrWhiteSpace(after) &&
                               !string.Equals(before, after, StringComparison.OrdinalIgnoreCase);
                bool hasRequires = !string.IsNullOrWhiteSpace(requires);

                if (!hasEdge && !hasRequires)
                    continue;

                if (hasEdge)
                {
                    string edgeKey = $"{before}>>{after}";
                    if (seenEdges.Add(edgeKey))
                    {
                        normalized.Add(new ModSortRule
                        {
                            BeforeId = before,
                            AfterId = after
                        });
                    }
                }

                if (hasRequires && seenRequires.Add(requires))
                {
                    normalized.Add(new ModSortRule
                    {
                        RequiresId = requires
                    });
                }
            }

            return normalized;
        }

        private void LoadSortRules()
        {
            relationshipRules = LoadRelationshipRules();
            if (File.Exists(modRelationshipRulesPath))
            {
                sortRules = RelationshipRulesToSortRules(relationshipRules);
                return;
            }

            sortRules = [];
            if (!JsonFileStore.TryLoad(modSortRulesPath, "SortRules", out List<ModSortRule>? loadedRules))
                return;

            sortRules = NormalizeSortRules(loadedRules);
            relationshipRules = LegacySortRulesToRelationshipRules(sortRules);
        }

        private static Dictionary<string, ModRelationshipRule> LoadRelationshipRules()
        {
            _ = JsonFileStore.TryLoad(modRelationshipRulesPath, "Rules", out Dictionary<string, ModRelationshipRule>? loaded);
            return ModRelationshipRule.NormalizeAll(loaded);
        }

        private static Dictionary<string, ModRelationshipRule> LegacySortRulesToRelationshipRules(IEnumerable<ModSortRule> rules)
        {
            Dictionary<string, ModRelationshipRule> converted = new(StringComparer.OrdinalIgnoreCase);
            foreach (ModSortRule rule in NormalizeSortRules(rules))
            {
                string before = rule.BeforeId?.Trim() ?? string.Empty;
                string after = rule.AfterId?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(before) || string.IsNullOrWhiteSpace(after))
                    continue;

                if (!converted.TryGetValue(before, out ModRelationshipRule? relationshipRule))
                {
                    relationshipRule = new ModRelationshipRule();
                    converted[before] = relationshipRule;
                }

                relationshipRule.BeforeIds.Add(after);
            }

            return ModRelationshipRule.NormalizeAll(converted);
        }

        private void SaveRelationshipRules()
            => _ = JsonFileStore.TrySave(modRelationshipRulesPath, relationshipRules, "Rules");

        public async Task<bool> FetchCommunitySortRulesAsync(string repositoryUrl, CancellationToken cancellationToken = default)
        {
            string? rawUrl = GitHubUrlParser.ToRawFileUrl(repositoryUrl);
            if (string.IsNullOrWhiteSpace(rawUrl))
            {
                communitySortRules = [];
                return false;
            }

            try
            {
                using HttpResponseMessage response = await GitHubFileClient.Instance.GetAsync(rawUrl, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    communitySortRules = [];
                    return false;
                }

                string jsonContent = await response.Content.ReadAsStringAsync(cancellationToken);
                List<ModSortRule>? loadedRules = JsonSerializer.Deserialize<List<ModSortRule>>(jsonContent, JsonFileStore.CaseInsensitive);
                communitySortRules = NormalizeSortRules(loadedRules);
                return true;
            }
            catch (Exception ex)
            {
                AppLogging.LogError($"[CommunityRules] {ex.Message}");
                communitySortRules = [];
                return false;
            }
        }

        private static void LoadCommunitySortRules()
        {
            //TODO
        }
    }
}
