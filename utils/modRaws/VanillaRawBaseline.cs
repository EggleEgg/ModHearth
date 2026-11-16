using System.Collections.Concurrent;
using ModHearth.Utils.Logging;
using ModHearth.Utils.Thread;

namespace ModHearth.Utils.ModRaws
{
    /// <summary>
    /// A set of structural keys (OBJECT_TYPE:ID) for every object defined by the
    /// official Dwarf Fortress vanilla raws. This baseline is loaded once and used
    /// to determine whether an asset originates from vanilla by set containment
    /// rather than by string matching a hardcoded list.
    /// </summary>
    public sealed class VanillaRawBaseline
    {
        public static VanillaRawBaseline Empty { get; } = new VanillaRawBaseline([]);

        private readonly HashSet<ObjectKey> _baseline;
        private readonly HashSet<string> _objectTypes;
        private readonly HashSet<string> _cutIgnoredDyeReactions;

        public VanillaRawBaseline(IEnumerable<ObjectKey> keys)
            : this(keys, [])
        {
        }

        public VanillaRawBaseline(
            IEnumerable<ObjectKey> keys,
            IEnumerable<string> cutIgnoredDyeReactions)
        {
            _baseline = [.. keys];
            _objectTypes = new HashSet<string>(
                _baseline.Select(key => key.ObjectType).Where(type => !string.IsNullOrWhiteSpace(type)),
                StringComparer.OrdinalIgnoreCase);
            _cutIgnoredDyeReactions = new HashSet<string>(
                cutIgnoredDyeReactions
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Select(id => id.Trim()),
                StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Distinct object types (CREATURE, PLANT, ...) the vanilla raws define. Used to tell a
        /// top-level object reference from a nested selector such as SELECT_CASTE or SELECT_MATERIAL.
        /// </summary>
        public IReadOnlyCollection<string> ObjectTypes => _objectTypes;

        /// <summary>
        /// True when no vanilla object keys were loaded. Missing-reference analysis cannot run
        /// reliably without the baseline, because every vanilla-derived target would look missing.
        /// </summary>
        public bool IsEmpty => _baseline.Count == 0;

        /// <summary>Number of vanilla object definitions captured by the baseline scan.</summary>
        public int Count => _baseline.Count;

        /// <summary>
        /// Vanilla dye reactions whose <c>CUT_REACTION</c> DF ignores, derived from
        /// <c>reaction_dyes.txt</c> and <c>entity_default.txt</c>. Empty when the raws were not
        /// loaded or the files could not be read; the hardcoded report surfaces that as a warning.
        /// </summary>
        public IReadOnlyCollection<string> CutIgnoredDyeReactions => _cutIgnoredDyeReactions;

        /// <summary>
        /// True when DF silently ignores a <c>CUT_REACTION</c> for this vanilla dye reaction.
        /// </summary>
        public bool IsCutIgnoredDyeReaction(string? id)
            => !string.IsNullOrWhiteSpace(id) && _cutIgnoredDyeReactions.Contains(id.Trim());

        /// <summary>
        /// Ids the vanilla raws define for one object type. The analyzer seeds its merged
        /// load-order state with these ids and lets active CUTs remove them.
        /// </summary>
        public IEnumerable<string> IdsOf(string objectType)
        {
            if (string.IsNullOrWhiteSpace(objectType))
                yield break;

            foreach (ObjectKey key in _baseline)
            {
                if (string.Equals(key.ObjectType, objectType, StringComparison.OrdinalIgnoreCase))
                    yield return key.Id;
            }
        }

        public bool Contains(ObjectKey? key) => key != null && _baseline.Contains(key);

        public bool Contains(string objectType, string id)
        {
            if (string.IsNullOrWhiteSpace(objectType) || string.IsNullOrWhiteSpace(id))
                return false;

            return _baseline.Contains(new ObjectKey(objectType, id));
        }

        /// <summary>
        /// Convenience overload for the "ObjectType:Id" composite keys produced by
        /// <see cref="ObjectKey.ToString"/>, which is how the raw scan persists references.
        /// </summary>
        public bool Contains(string compositeKey)
            => ObjectKey.TryParse(compositeKey, out string objectType, out string id) && Contains(objectType, id);

        /// <summary>
        /// Loads the vanilla baseline by scanning the objects/ folders of all mods
        /// located under <paramref name="vanillaModsPath"/>. Each mod folder's scan is
        /// independent, so this mirrors EnsureModRawDependencyCacheAsync's parallel raw scan.
        /// The cut-ignored dye reaction ids are derived from the vanilla raw definitions and
        /// permissions so they follow a game update without a catalog edit.
        /// </summary>
        public static VanillaRawBaseline Load(
            string vanillaModsPath,
            ICollection<string>? warnings = null)
        {
            if (string.IsNullOrWhiteSpace(vanillaModsPath) || !Directory.Exists(vanillaModsPath))
                return Empty;

            try
            {
                List<string> modDirs = [.. Directory.EnumerateDirectories(vanillaModsPath)];
                ConcurrentBag<ObjectKey> keys = [];

                _ = Parallel.ForEach(modDirs, Parallelism.CpuBound, modDir =>
                {
                    RawDatabase db = ModRawObjectScanner.Scan(modDir, Path.GetFileName(modDir));
                    foreach (KeyValuePair<string, HashSet<string>> kvp in db.DefinedObjects)
                    {
                        string objectType = kvp.Key;
                        foreach (string id in kvp.Value)
                        {
                            keys.Add(new ObjectKey(objectType, id));
                        }
                    }
                });

                return new VanillaRawBaseline(
                    keys,
                    VanillaDyeReactionCatalog.Load(vanillaModsPath, warnings));
            }
            catch (Exception ex)
            {
                AppLogging.LogException("Failed to load vanilla raw baseline", ex);
                return Empty;
            }
        }
    }
}