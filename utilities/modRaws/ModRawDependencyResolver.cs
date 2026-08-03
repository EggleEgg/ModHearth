using ModHearth.Utilities;
using ModHearth.Utilities.Logging;
using System.Collections.Concurrent;

namespace ModHearth;

// Orchestrates the raw-file dependency scan: loads the persistent cache, scans only what's stale (in parallel, since each mod folder is fully
// independent), saves, and publishes the result for ModSort to read. Deliberately not called from Initialize()/ReloadModpacksFromDisk(). 
// This is invoked explicitly from exactly three UI-level call sites (startup, the manual reload button, ModSort) so it doesn't run on every automatic
// reload from the auto-reload timer or file watcher.
public partial class ModHearthManager
{
    private Dictionary<string, ModRawDependencyInfo>? rawDependencyInfoByModId;
    private readonly object rawDependencyGate = new();
    private readonly object vanillaBaselineGate = new();
    private VanillaRawBaseline? cachedVanillaBaseline;
    private string? cachedVanillaBaselinePath;

    public async Task EnsureModRawDependencyCacheAsync()
    {
        List<ModReference> modsSnapshot = modrefMap.Values.ToList();
        Dictionary<string, ModRawDependencyInfo> cache = ModRawDependencyCacheStore.Load();

        VanillaRawBaseline vanillaBaseline = GetVanillaBaseline();

        ConcurrentDictionary<string, ModRawDependencyInfo> resolved = new(StringComparer.OrdinalIgnoreCase);
        int cacheHitCount = 0;
        int scannedCount = 0;

        await Parallel.ForEachAsync(modsSnapshot, new ParallelOptions
        {
            MaxDegreeOfParallelism = Environment.ProcessorCount
        }, (modref, _) =>
        {
            string modId = modref.ID?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(modId) || string.IsNullOrWhiteSpace(modref.path))
                return ValueTask.CompletedTask;

            string objectsPath = Path.Combine(modref.path, "objects");
            long stamp = GetFolderStampTicks(objectsPath);
            string key = ModRawDependencyCacheStore.BuildKey(modId, modref.numericVersion ?? string.Empty);

            if (cache.TryGetValue(key, out ModRawDependencyInfo? cached) && cached.ObjectsFolderStampTicks == stamp)
            {
                resolved[modId] = cached;
                Interlocked.Increment(ref cacheHitCount);
                return ValueTask.CompletedTask;
            }

            RawDatabase rawDatabase = ModRawObjectScanner.Scan(modref.path, modId);
            ModRawDependencyInfo scanned = rawDatabase.ToDependencyInfo(
                modId,
                modref.numericVersion ?? string.Empty,
                stamp,
                vanillaBaseline);
            resolved[modId] = scanned;
            Interlocked.Increment(ref scannedCount);
            return ValueTask.CompletedTask;
        });

        ModRawDependencyCacheStore.Save(resolved.Values);

        lock (rawDependencyGate)
            rawDependencyInfoByModId = new Dictionary<string, ModRawDependencyInfo>(resolved, StringComparer.OrdinalIgnoreCase);

        InfoLogger.Log($"Raw dependency scan complete: {resolved.Count} mod(s), {cacheHitCount} cache hit(s), {scannedCount} rescanned.");
    }

    private ModRawDependencyInfo? GetRawDependencyInfo(ModReference modref)
    {
        if (modref?.ID == null)
            return null;

        lock (rawDependencyGate)
        {
            switch (rawDependencyInfoByModId)
            {
                case null:
                    return null;
                default:
                    return rawDependencyInfoByModId.TryGetValue(modref.ID, out ModRawDependencyInfo? info) ? info : null;
            }
        }
    }

    private VanillaRawBaseline GetVanillaBaseline()
    {
        string? vanillaPath = GetVanillaModsPath();

        lock (vanillaBaselineGate)
        {
            if (cachedVanillaBaseline != null
                && string.Equals(cachedVanillaBaselinePath, vanillaPath, StringComparison.OrdinalIgnoreCase))
            {
                return cachedVanillaBaseline;
            }

            cachedVanillaBaseline = VanillaRawBaseline.Load(vanillaPath);
            cachedVanillaBaselinePath = vanillaPath;
            return cachedVanillaBaseline;
        }
    }

    private static long GetFolderStampTicks(string folderPath)
    {
        return FolderTimestampHelper.GetLatestModifiedTimeUtc(folderPath)?.Ticks ?? 0;
    }
}