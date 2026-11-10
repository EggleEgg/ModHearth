using System.Collections.Concurrent;
using ModHearth.Models;
using ModHearth.Utilities;
using ModHearth.Utilities.Logging;
using ModHearth.Utilities.ModRaws;

namespace ModHearth
{
    // Orchestrates the raw-file dependency scan: loads the persistent cache, scans only what's stale (in parallel, since each mod folder is fully
    // independent), saves, and publishes the result for ModSort and the possible-issues analysis to read. Deliberately not called from
    // Initialize()/ReloadModpacksFromDisk(): this is invoked explicitly from the UI-level call sites (startup, the manual reload button, ModSort,
    // and enabling the "Search possible issues" setting) so it doesn't run on every automatic reload from the auto-reload timer or file watcher.
    public partial class ModHearthManager
    {
        private Dictionary<string, ModRawDependencyInfo>? rawDependencyInfoByModId;
        private readonly object rawDependencyGate = new();

        // A manual reload and the issue-search toggle can request a scan at the same time.
        // Serialize them through a plain task chain so two scans cannot interleave their saves or
        // overwrite each other's in-memory snapshot with a stale pass.
        private readonly object rawDependencyScanGate = new();
        private Task rawDependencyScanTail = Task.CompletedTask;

        private readonly object vanillaBaselineGate = new();
        private VanillaRawBaseline? cachedVanillaBaseline;
        private string? cachedVanillaBaselinePath;

        public Task EnsureModRawDependencyCacheAsync()
        {
            lock (rawDependencyScanGate)
            {
                Task scan = RunAfterAsync(rawDependencyScanTail);
                rawDependencyScanTail = scan;
                return scan;
            }
        }

        private async Task RunAfterAsync(Task previousScan)
        {
            // Leave the caller's lock (and thread) before touching the disk: the scan prefix loads
            // the raw cache and scans the vanilla raws, which must not happen while holding the
            // serialization gate.
            await Task.Yield();

            // A failed previous scan must not poison every later request; each caller observes its
            // own scan's exception instead.
            try
            {
                await previousScan.ConfigureAwait(false);
            }
            catch
            {
                // Already reported to the caller that started that scan.
            }

            await ScanModRawDependencyCacheAsync().ConfigureAwait(false);
        }

        private async Task ScanModRawDependencyCacheAsync()
        {
            List<ModReference> modsSnapshot = [.. modrefMap.Values];
            Dictionary<string, ModRawDependencyInfo> cache = ModRawDependencyCacheStore.Load();

            VanillaRawBaseline vanillaBaseline = GetVanillaBaseline();

            ConcurrentDictionary<string, ModRawDependencyInfo> resolved = new(StringComparer.OrdinalIgnoreCase);
            int cacheHitCount = 0;
            int scannedCount = 0;

            await Parallel.ForEachAsync(modsSnapshot, new ParallelOptions
            {
                MaxDegreeOfParallelism = Environment.ProcessorCount
            }, (modref, ct) =>
            {
                string modId = modref.ID?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(modId) || string.IsNullOrWhiteSpace(modref.Path))
                    return ValueTask.CompletedTask;

                string objectsPath = Path.Combine(modref.Path, "objects");
                long stamp = GetFolderStampTicks(objectsPath);
                string? infoPath = ConfigManager.ResolveInfoFilePath(modref.Path);
                long infoStamp = GetFileStampTicks(infoPath);
                string key = ModRawDependencyCacheStore.BuildKey(modId, modref.NumericVersion ?? string.Empty);

                if (cache.TryGetValue(key, out ModRawDependencyInfo? cached)
                    && cached.ObjectsFolderStampTicks == stamp
                    && cached.InfoFileStampTicks == infoStamp)
                {
                    resolved[modId] = cached;
                    _ = Interlocked.Increment(ref cacheHitCount);
                    return ValueTask.CompletedTask;
                }

                RawDatabase rawDatabase = ModRawObjectScanner.Scan(modref.Path, modId);
                ModRawDependencyInfo scanned = rawDatabase.ToDependencyInfo(
                    modId,
                    modref.NumericVersion ?? string.Empty,
                    stamp,
                    vanillaBaseline);

                scanned.InfoFileStampTicks = infoStamp;
                scanned.InfoIssues = ModInfoIssueAnalyzer.AnalyzeFile(infoPath);

                scanned.Capabilities = ModCapabilityScanner.Scan(
                    modref.Path,
                    hasRawDefinitions: rawDatabase.DefinedObjects.Count > 0,
                    hasGraphics: rawDatabase.HasGraphics);
                resolved[modId] = scanned;
                _ = Interlocked.Increment(ref scannedCount);
                return ValueTask.CompletedTask;
            });

            ModRawDependencyCacheStore.Save(resolved.Values);

            lock (rawDependencyGate)
                rawDependencyInfoByModId = new Dictionary<string, ModRawDependencyInfo>(resolved, StringComparer.OrdinalIgnoreCase);

            Loggers.Diag.Log($"Raw dependency scan complete: {resolved.Count} mod(s), {cacheHitCount} cache hit(s), {scannedCount} rescanned.");
        }

        private ModRawDependencyInfo? GetRawDependencyInfo(ModReference modref)
        {
            if (modref?.ID == null)
                return null;

            lock (rawDependencyGate)
            {
                return rawDependencyInfoByModId is { } map && map.TryGetValue(modref.ID, out ModRawDependencyInfo? info) ? info : null;
            }
        }

        // Errorlog attribution may run before the first scan in a session (or after the in-memory
        // map was cleared), so fall back to the persisted cache instead of silently losing all
        // object -> mod mappings.
        private IReadOnlyCollection<ModRawDependencyInfo> GetRawDependencyInfoSnapshot()
        {
            lock (rawDependencyGate)
            {
                if (rawDependencyInfoByModId is { Count: > 0 } map)
                    return [.. map.Values];
            }

            return [.. ModRawDependencyCacheStore.Load().Values];
        }

        private VanillaRawBaseline GetVanillaBaseline()
        {
            string? vanillaPath = ConfigManager.GetVanillaModsPath();

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

        private static long GetFolderStampTicks(string folderPath) => FolderTimestampHelper.GetLatestModifiedTimeUtc(folderPath)?.Ticks ?? 0;

        private static long GetFileStampTicks(string? filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                return 0;

            try
            {
                return File.Exists(filePath) ? File.GetLastWriteTimeUtc(filePath).Ticks : 0;
            }
            catch (Exception ex)
            {
                Loggers.Diag.Dev($"Could not stat mod info file '{filePath}': {ex.Message}");
                return 0;
            }
        }
    }
}
