using ModHearth.Models;
using ModHearth.Utils.IO;
using ModHearth.Utils.Mods;

using static ModHearth.Config.ConfigManager;

namespace ModHearth.Manager
{
    public partial class ModHearthManager
    {
        public static bool CanDeleteModFromModsFolder(ModReference modref)
            => TryGetDeletablePath(modref, GetModsPath(), out _);

        /// <summary>
        /// True when <paramref name="modref"/> points at a real folder inside Dwarf Fortress's
        /// installed_mods cache. The cache is disposable, so these entries can be deleted even
        /// though they are never actionable as a normal local mod.
        /// </summary>
        public static bool CanDeleteCacheOnlyMod(ModReference modref)
            => ModSourceClassifier.IsCacheOnly(modref) && TryGetDeletablePath(modref, GetInstalledModsPath(), out _);

        /// <summary>
        /// Resolves <paramref name="modref"/> against <paramref name="root"/> and reports whether it
        /// is a real folder inside it. Shared by both delete guards so containment rules cannot drift.
        /// </summary>
        private static bool TryGetDeletablePath(ModReference? modref, string? root, out string modPath)
        {
            modPath = string.Empty;
            if (modref == null || string.IsNullOrWhiteSpace(modref.Path) || Config == null)
                return false;

            string canonicalRoot = ResolveCanonicalPath(root ?? string.Empty);
            if (string.IsNullOrWhiteSpace(canonicalRoot))
                return false;

            modPath = ResolveCanonicalPath(modref.Path);
            if (string.IsNullOrWhiteSpace(modPath))
                return false;

            return PathHelper.IsUnderRoot(modPath, canonicalRoot) && Directory.Exists(modPath);
        }

        public ModDeletionBatchScope BeginDeletionBatch() => new(ShowNotification);

        public bool DeleteModFromModsFolder(ModReference modref, out string message, bool requestUIReload = true)
            => TryDeleteAfterGuard(
                modref,
                CanDeleteModFromModsFolder,
                "Mod is not in the Mods folder or was already removed.",
                requestUIReload,
                out message);

        /// <summary>
        /// Deletes a cache-only mod's copy from Dwarf Fortress's disposable installed_mods folder.
        /// The real source (if any) is untouched; Dwarf Fortress recreates the cache entry the next
        /// time the mod is enabled.
        /// </summary>
        public bool DeleteCacheOnlyMod(ModReference modref, out string message, bool requestUIReload = true)
            => TryDeleteAfterGuard(
                modref,
                CanDeleteCacheOnlyMod,
                "Mod is not a deletable installed_mods cache entry or was already removed.",
                requestUIReload,
                out message);

        /// <summary>
        /// Shared guard-and-delete flow: reject a null selection, reject targets the caller's guard
        /// refuses, then delete the folder.
        /// </summary>
        private bool TryDeleteAfterGuard(
            ModReference modref,
            Func<ModReference, bool> canDelete,
            string notDeletableMessage,
            bool requestUIReload,
            out string message)
        {
            if (modref == null)
            {
                message = "No mod selected.";
                return false;
            }

            if (!canDelete(modref))
            {
                message = notDeletableMessage;
                return false;
            }

            return TryDeleteModFolder(modref, requestUIReload, out message);
        }

        /// <summary>
        /// Deletes the mod's folder, then drops the reference from the catalog and reports the change.
        /// Callers are responsible for validating that the path is deletable.
        /// </summary>
        private bool TryDeleteModFolder(ModReference modref, bool requestUIReload, out string message)
        {
            string modPath = NormalizeFileSystemPath(modref.Path);
            if (string.IsNullOrWhiteSpace(modPath))
            {
                message = "Mod path is invalid.";
                return false;
            }

            try
            {
                Directory.Delete(modPath, true);
                string sourceFolder = Path.GetDirectoryName(modPath) ?? string.Empty;
                ModDeletionNotifier.NotifyDeleted(ShowNotification, modPath, sourceFolder);
            }
            catch (Exception ex)
            {
                message = $"Failed to delete mod folder: {ex.Message}";
                return false;
            }

            ForgetDeletedMod(modref, requestUIReload);
            message = $"Deleted {modPath}";
            return true;
        }

        /// <summary>
        /// Removes a deleted mod from the in-memory catalog after its folder is gone, so the UI stops
        /// showing it without waiting for a full disk rescan.
        /// </summary>
        private void ForgetDeletedMod(ModReference modref, bool requestUIReload)
        {
            DfhMod dfm = modref.ToDFHMod();
            string modrefKey = modref.DFHackCompatibleString();

            HashSet<DfhMod> newModPool = [.. modPool];
            _ = newModPool.Remove(dfm);
            List<DfhMod> newEnabledMods = [.. enabledMods.Where(m => m != dfm)];
            HashSet<DfhMod> newDisabledMods = [.. disabledMods];
            _ = newDisabledMods.Remove(dfm);
            Dictionary<string, ModReference> newModrefMap = new(modrefMap, StringComparer.OrdinalIgnoreCase);
            _ = newModrefMap.Remove(modrefKey);

            lock (stateGate)
            {
                modPool = newModPool;
                enabledMods = newEnabledMods;
                disabledMods = newDisabledMods;
                modrefMap = newModrefMap;
            }

            RefreshInstalledCacheModIds();
            FindModlistProblems();
            _ = TryRequestModManagerReload(out _, out _);
            if (requestUIReload)
            {
                TriggerUIReload();
            }
        }

        public bool ClearInstalledModsFolder(out string message)
        {
            string installedModsPath = GetInstalledModsPath();
            if (string.IsNullOrWhiteSpace(installedModsPath))
            {
                message = "installed_mods path is not set.";
                return false;
            }

            if (!Directory.Exists(installedModsPath))
            {
                message = $"installed_mods folder not found:\n{installedModsPath}";
                return false;
            }

            int deleted = 0;
            List<string> failures = [];
            using (BeginDeletionBatch())
            {
                foreach (string entry in Directory.EnumerateFileSystemEntries(installedModsPath))
                {
                    try
                    {
                        if (Directory.Exists(entry))
                        {
                            Directory.Delete(entry, true);
                            ModDeletionNotifier.NotifyDeleted(ShowNotification, entry, installedModsPath);
                        }
                        else if (File.Exists(entry))
                        {
                            File.Delete(entry);
                        }

                        deleted++;
                    }
                    catch
                    {
                        failures.Add(Path.GetFileName(entry));
                    }
                }
            }

            if (failures.Count > 0)
            {
                message = "Failed to delete: " + string.Join(", ", failures);
                return false;
            }

            message = $"Cleared {deleted} item(s).";
            RefreshInstalledCacheModIds();
            return true;
        }

        public bool ClearErrorLog(out string message)
        {
            string errorLogPath = GetErrorLogPath();
            if (string.IsNullOrWhiteSpace(errorLogPath))
            {
                message = "Error log path is not set.";
                return false;
            }

            if (!File.Exists(errorLogPath))
            {
                message = $"Error log file not found:\n{errorLogPath}";
                return false;
            }

            try
            {
                File.Delete(errorLogPath);
                ClearErrorLogProblemCache();
                message = $"Deleted {errorLogPath}";
                return true;
            }
            catch (Exception ex)
            {
                message = $"Failed to delete error log:\n{ex.Message}";
                return false;
            }
        }
    }
}
