using System.Collections.Concurrent;
using ModHearth.Utilities.Logging;

namespace ModHearth.Utilities.ModRaws
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

        public VanillaRawBaseline(IEnumerable<ObjectKey> keys)
        {
            _baseline = [.. keys];
            _objectTypes = new HashSet<string>(
                _baseline.Select(key => key.ObjectType).Where(type => !string.IsNullOrWhiteSpace(type)),
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
        /// </summary>
        public static VanillaRawBaseline Load(string vanillaModsPath)
        {
            if (string.IsNullOrWhiteSpace(vanillaModsPath) || !Directory.Exists(vanillaModsPath))
                return Empty;

            try
            {
                List<string> modDirs = [.. Directory.EnumerateDirectories(vanillaModsPath)];
                ConcurrentBag<ObjectKey> keys = [];

                _ = Parallel.ForEach(modDirs, new ParallelOptions
                {
                    MaxDegreeOfParallelism = Environment.ProcessorCount
                }, modDir =>
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

                return new VanillaRawBaseline(keys);
            }
            catch (Exception ex)
            {
                AppLogging.LogException("Failed to load vanilla raw baseline", ex);
                return Empty;
            }
        }
    }
}