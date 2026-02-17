using ModHearth.UI;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Reflection;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;

namespace ModHearth
{
    /// <summary>
    /// Config class, to store folder information.
    /// </summary>
    [Serializable]
    public class ModHearthConfig
    {
        // Path to DF.exe
        public string DFEXEPath { get; set; }
        public string DFFolderPath => Path.GetDirectoryName(DFEXEPath);
        public string ModsPath => Path.Combine(DFFolderPath, "Mods");

        // Path to installed mods cache.
        public string InstalledModsPath { get; set; }

        // Should this be in lightmode?
        public int theme { get; set; }

    }

    [Serializable]
    public struct ModProblem
    {
        public string problemThrowerID;
        public string problemID;

        public enum ProblemType
        {
            MissingBefore,
            MissingAfter,
            ConflictPresent
        };

        public ProblemType problemType;

        public ModProblem(string problemThrowerID, string problemID, ProblemType problemType)
        {
            this.problemThrowerID = problemThrowerID;
            this.problemID = problemID;
            this.problemType = problemType;
        }

        public override string ToString()
        {
            switch(problemType)
            {
                case ProblemType.MissingBefore:
                    return $"Mod '{problemThrowerID}' requires mod '{problemID}' to be loaded before it.";
                case ProblemType.MissingAfter:
                    return $"Mod '{problemThrowerID}' requires mod '{problemID}' to be loaded after it.";
                case ProblemType.ConflictPresent:
                    return $"Mod '{problemThrowerID}' is incompatible with mod '{problemID}'.";
            }
            return "";
        }
    }

    public class ModHearthManager
    {
        public static string GetBuildVersionString()
        {
            string runNumber = Environment.GetEnvironmentVariable("GITHUB_RUN_NUMBER");
            if (!string.IsNullOrWhiteSpace(runNumber))
                return runNumber;

            string infoVersion = Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (!string.IsNullOrWhiteSpace(infoVersion))
            {
                int plusIndex = infoVersion.IndexOf('+');
                if (plusIndex > 0)
                    infoVersion = infoVersion.Substring(0, plusIndex);
                if (!string.IsNullOrWhiteSpace(infoVersion))
                    return infoVersion;
            }

            return " none";
        }

        // Maps strings to ModReferences. The keys match DFHMods.ToString() perfectly. Given a value V, V.ToDFHMod.ToString() returns it's key.
        private Dictionary<string, ModReference> modrefMap;

        // Get a ModReference given a string key.
        public ModReference GetModRef(string key) => modrefMap[key];

        // Get a DFHMod given a string key.
        public DFHMod GetDFHackMod(string key) => modrefMap[key].ToDFHMod();

        // Get a ModReference given a DFHMod key.
        public ModReference GetRefFromDFHMod(DFHMod dfmod) => modrefMap[dfmod.ToString()];

        // The sorted list of enabled DFHmods. This list is modified by the form, and when saved it overwrites the list of a ModPack.
        public List<DFHMod> enabledMods;

        // The unsorted list of disabled DFHmods
        public HashSet<DFHMod> disabledMods;

        // The unsorted list of all available DFHmods
        public HashSet<DFHMod> modPool;

        // Get the currently selected modpack
        public DFHModpack SelectedModlist => modpacks[selectedModlistIndex];

        // List of all modpacks. After a modpack in this list is modified the list is saved to file.
        public List<DFHModpack> modpacks;

        // The index of the currently selected modpack.
        public int selectedModlistIndex;

        // The file config for this class.
        private ModHearthConfig config;

        // Paths.
        private static readonly string configPath = "config.json";
        private static readonly string styleLightPath = "style.light.json";
        private static readonly string styleDarkPath = "style.dark.json";
        private static readonly string styleLegacyPath = "style.json";

        // Mod problem tracker.
        public List<ModProblem> modproblems;

        public ModHearthManager() 
        {
            Console.WriteLine($"Crafting Hearth v{GetBuildVersionString()}");

            // Get and load config file, fix if needed.
            AttemptLoadConfig();
            FixConfig();

            // Find all mods and add to the lists.
            FindAllModsDFHackLua();

            // Find DFHModpacks, and fix them if needed.
            FindModpacks();

            // Write some info on found things.
            Console.WriteLine();
            Console.WriteLine($"Found {modrefMap.Count} mods and {modpacks.Count} modlists");
            Console.WriteLine();
        }


        //??
        public ModHearthConfig GetConfig()
        {
            return config;
        }

        public string GetInstalledModsPath()
        {
            if (config == null || string.IsNullOrWhiteSpace(config.InstalledModsPath))
                return GetDefaultInstalledModsPath();
            return config.InstalledModsPath;
        }

        public bool CanDeleteModFromModsFolder(ModReference modref)
        {
            if (modref == null || string.IsNullOrWhiteSpace(modref.path) || config == null)
                return false;
            if (string.IsNullOrWhiteSpace(config.ModsPath))
                return false;
            string modPath = Path.GetFullPath(modref.path);
            string modsPath = Path.GetFullPath(config.ModsPath);
            if (!IsPathUnderRoot(modPath, modsPath))
                return false;
            return Directory.Exists(modPath);
        }

        public bool DeleteModFromModsFolder(ModReference modref, out string message)
        {
            if (modref == null)
            {
                message = "No mod selected.";
                return false;
            }

            if (!CanDeleteModFromModsFolder(modref))
            {
                message = "Mod is not in the Mods folder or was already removed.";
                return false;
            }

            string modPath = Path.GetFullPath(modref.path);
            try
            {
                Directory.Delete(modPath, true);
            }
            catch (Exception ex)
            {
                message = $"Failed to delete mod folder: {ex.Message}";
                return false;
            }

            DFHMod dfm = modref.ToDFHMod();
            modPool.Remove(dfm);
            enabledMods.Remove(dfm);
            disabledMods.Remove(dfm);
            modrefMap.Remove(modref.DFHackCompatibleString());
            FindModlistProblems();
            ReloadDFHackModManagerScreen();

            message = $"Deleted {modPath}";
            return true;
        }

        private static bool IsPathUnderRoot(string path, string root)
        {
            string normalizedPath = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string normalizedRoot = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            normalizedRoot += Path.DirectorySeparatorChar;
            return normalizedPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
        }

        private string GetDefaultInstalledModsPath()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Bay 12 Games",
                "Dwarf Fortress",
                "data",
                "installed_mods");
        }

        public bool ClearInstalledModsFolder(out string message)
        {
            string installedModsPath = GetInstalledModsPath();
            if (string.IsNullOrWhiteSpace(installedModsPath))
            {
                message = "Installed mods path is not set.";
                return false;
            }

            if (!Directory.Exists(installedModsPath))
            {
                message = $"Installed mods folder not found:\n{installedModsPath}";
                return false;
            }

            int deleted = 0;
            List<string> failures = new List<string>();
            foreach (string entry in Directory.EnumerateFileSystemEntries(installedModsPath))
            {
                try
                {
                    if (Directory.Exists(entry))
                        Directory.Delete(entry, true);
                    else if (File.Exists(entry))
                        File.Delete(entry);
                    deleted++;
                }
                catch
                {
                    failures.Add(Path.GetFileName(entry));
                }
            }

            if (failures.Count > 0)
            {
                message = "Failed to delete: " + string.Join(", ", failures);
                return false;
            }

            message = $"Cleared {deleted} item(s).";
            return true;
        }

        private void FindAllModsDFHackLua()
        {
            // If game not running, prompt user to run it and force restart.
            if (!DwarfFortressRunning())
            {
                // Only restart if user hits OK.
                Console.WriteLine("DF not running");
                DialogResult result = MessageBox.Show("Please launch dwarf fortress and navigate to the world creation screen. Application will restart when done.", "DF not running", MessageBoxButtons.OKCancel);

                if (result == DialogResult.OK)
                    Process.Start(Application.ExecutablePath);

                // More forceful shutdown.
                MainForm.instance.selfClosing = true;
                Application.Exit();
                Environment.Exit(0);

                // Needed?
                return;
            }

            // Initialize relevant variables.
            modrefMap = new Dictionary<string, ModReference>();
            modPool = new HashSet<DFHMod>();

            // Get all mod folders.
            Console.WriteLine("Finding all mods... ");

            HashSet<Dictionary<string, string>> modData = GetModMemoryData();
            Dictionary<string, string> modIdPathMap = BuildModIdPathMap();

            foreach (Dictionary<string, string> modDataEntry in modData)
            {
                // Directory correction.
                modDataEntry["src_dir"] = ResolveModPath(modDataEntry, modIdPathMap);

                // Mod setup and registry.
                ModReference modRef = new ModReference(modDataEntry);
                string key = modRef.DFHackCompatibleString();
                Console.WriteLine($"   Mod registered: {modRef.name}.");
                modrefMap.Add(key, modRef);
                modPool.Add(modRef.ToDFHMod());
            }
        }

        private Dictionary<string, string> BuildModIdPathMap()
        {
            Dictionary<string, string> map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            string installedModsPath = Path.Combine(config.DFFolderPath, "data", "installed_mods");
            List<string> roots = new List<string> { config.ModsPath, installedModsPath };

            foreach (string root in roots)
            {
                if (!Directory.Exists(root))
                    continue;

                foreach (string dir in Directory.EnumerateDirectories(root))
                {
                    string infoPath = Path.Combine(dir, "info.txt");
                    if (!File.Exists(infoPath))
                        continue;

                    try
                    {
                        string info = File.ReadAllText(infoPath);
                        Match idMatch = Regex.Match(info, @"\[ID:([^\]]+)\]", RegexOptions.IgnoreCase);
                        if (idMatch.Success)
                        {
                            string id = idMatch.Groups[1].Value.Trim();
                            if (!string.IsNullOrEmpty(id))
                                map[id] = dir;
                        }
                    }
                    catch
                    {
                        // Ignore unreadable info files.
                    }
                }
            }

            return map;
        }

        private string ResolveModPath(Dictionary<string, string> modDataEntry, Dictionary<string, string> modIdPathMap)
        {
            string rawSrcDir = modDataEntry["src_dir"];
            string fullPath = Path.Combine(config.DFFolderPath, rawSrcDir);

            if (Directory.Exists(fullPath))
                return fullPath;

            // Try matching the folder name in known roots.
            string rawFolderName = Path.GetFileName(rawSrcDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (!string.IsNullOrEmpty(rawFolderName))
            {
                string candidate = Path.Combine(config.ModsPath, rawFolderName);
                if (Directory.Exists(candidate))
                    return candidate;

                string installedModsPath = Path.Combine(config.DFFolderPath, "data", "installed_mods");
                candidate = Path.Combine(installedModsPath, rawFolderName);
                if (Directory.Exists(candidate))
                    return candidate;
            }

            // Fall back to ID-based lookup.
            if (modDataEntry.TryGetValue("id", out string id) &&
                !string.IsNullOrWhiteSpace(id) &&
                modIdPathMap.TryGetValue(id, out string mappedPath))
            {
                return mappedPath;
            }

            return fullPath;
        }

        // Output a dictionary, that given a modID gets the true version.
        private HashSet<Dictionary<string, string>> GetModMemoryData()
        {
            HashSet<Dictionary<string, string>> modData = new HashSet<Dictionary<string, string>>();

            // Load raw memory data string, and parse it with regex
            string RawModData = LoadModMemoryData();

            if (RawModData.StartsWith('0'))
            {
                // Only restart if user hits OK.        
                DialogResult result = MessageBox.Show("Please navigate to the world creation screen. Application will restart when done.", "DF works creation screen not open.", MessageBoxButtons.OKCancel);

                if (result == DialogResult.OK)
                    Process.Start(Application.ExecutablePath);

                // More forceful shutdown.
                MainForm.instance.selfClosing = true;
                Application.Exit();
                Environment.Exit(0);

                // Needed?
                return null;
            }

            // Split into mods, then loop through and extract headers.
            string[] singleModDataPairs = RawModData.Split("___");
            Console.WriteLine("Mods found: " + singleModDataPairs.Length);
            foreach (string simpleModDataPair in singleModDataPairs)
            {
                // Split into headers and non headers. Deserialize headers into dict.
                string[] pairArr = simpleModDataPair.Split("===");
                string[] nonHeaders = pairArr[0].Split('|');
                Dictionary<string, string> headers = JsonSerializer.Deserialize<Dictionary<string, string>>(pairArr[1]);
                modData.Add(headers);
                Console.WriteLine("   Mod Found: " + headers["name"]);

                // To see which headers there are to choose from.
                //foreach (string k in headers.Keys)
                    //Console.WriteLine($"header found. k: {k}, v: {headers[k]}");

            }

            return modData;
        }

        // Use dfhack-run.exe and lua to get raw mod data.
        private string LoadModMemoryData()
        {
            // Get path to lua script.
            string luaPath = Path.Combine(Environment.CurrentDirectory, "GetModMemoryData.lua");

            // Set up dfhack process.
            ProcessStartInfo processStartInfo = new ProcessStartInfo
            {
                FileName = Path.Combine(config.DFFolderPath, "dfhack-run.exe"),
                WorkingDirectory = config.DFFolderPath,
                Arguments = $"lua -f \"{luaPath}\"",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            // Start dfhack process.
            Process process = new Process
            {
                StartInfo = processStartInfo
            };
            process.Start();

            // Get output string.
            string output = process.StandardOutput.ReadToEnd();

            // Wait for the process to exit.
            process.WaitForExit();

            //Console.WriteLine("output:\n" + output);

            return output;
        }

        // Check if DF is running.
        public bool DwarfFortressRunning()
        {
            foreach(Process process in Process.GetProcesses())
                if (process.ProcessName.Equals("Dwarf Fortress"))
                    return true;
            return false;
        }

        // Alter the current modpack with enabledMods and save modpack list to dfhack file.
        public void SaveCurrentModpack()
        {
            SelectedModlist.modlist = new List<DFHMod>(enabledMods);

            SaveAllModpacks();
        }

        // Save the DFHModpack list to file.
        public void SaveAllModpacks()
        {
            Console.WriteLine("Modlists saved.");

            // Get the path, serialize with right options, and write to file.
            string dfHackModlistPath = Path.Combine(config.DFFolderPath, @"dfhack-config\mod-manager.json");
            JsonSerializerOptions options = new JsonSerializerOptions
            {
                WriteIndented = true // Enable pretty formatting
            };
            string modlistJson = JsonSerializer.Serialize(modpacks, options);
            File.WriteAllText(dfHackModlistPath, modlistJson);

            ReloadDFHackModManagerScreen();
        }

        private void ReloadDFHackModManagerScreen()
        {
            if (!DwarfFortressRunning())
                return;

            string dfhackRunPath = Path.Combine(config.DFFolderPath, "dfhack-run.exe");
            if (!File.Exists(dfhackRunPath))
                return;

            string luaPath = Path.Combine(Environment.CurrentDirectory, "ReloadModManager.lua");
            if (!File.Exists(luaPath))
                return;

            ProcessStartInfo processStartInfo = new ProcessStartInfo
            {
                FileName = dfhackRunPath,
                WorkingDirectory = config.DFFolderPath,
                Arguments = $"lua -f \"{luaPath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            try
            {
                using Process process = new Process { StartInfo = processStartInfo };
                process.Start();
                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();
                process.WaitForExit();
                if (!string.IsNullOrWhiteSpace(output))
                    Console.WriteLine(output.TrimEnd());
                if (!string.IsNullOrWhiteSpace(error))
                    Console.WriteLine(error.TrimEnd());
            }
            catch
            {
                // Ignore reload failures to avoid disrupting saving.
            }
        }

        public void SetSelectedModpack(int index)
        {
            // Regenerate enabled and disabled lists to match newly selected modpack.
            selectedModlistIndex = index;
            SetActiveMods(SelectedModlist.modlist);

            // Find problems with newly selected modpack.
            FindModlistProblems();
        }

        // Changes currently enabled and disabled mods based on the given list.
        // The only time this is called (other than SetSelectedModpack) is when overwriting a modpack due to importing.
        public void SetActiveMods(List<DFHMod> mods)
        {
            enabledMods = new List<DFHMod>();
            disabledMods = new HashSet<DFHMod>(modPool);
            for (int i = 0; i < mods.Count; i++)
            {
                enabledMods.Add(mods[i]);
                disabledMods.Remove(mods[i]);
            }
        }

        // Move a mod from one place to another. Has four cases, depending on source and destination.
        public void MoveMod(ModReference mod, int newIndex, bool sourceLeft, bool destinationLeft)
        {
            // Convert mod to DFHMod.
            DFHMod dfm = mod.ToDFHMod();
            Console.WriteLine($"Mod '{dfm.id}' moved from " + (sourceLeft ? "dis" : "en") + "abled to " + (destinationLeft ? "dis" : "en") + "abled.");

            if (sourceLeft && destinationLeft)
            {
                // Do nothing since the order of disabled mods doesn't matter.
                return;
            }
            else if (!sourceLeft && !destinationLeft)
            {
                // Get the old index of the mod
                int oldIndex = enabledMods.IndexOf(dfm);

                // If the mod is removed from the old index, and it would shift the whole list down, account for that.
                if (oldIndex < newIndex)
                    newIndex--;

                if (oldIndex < 0)
                {
                    Console.WriteLine("Clicking too fast, attempted to disable same mod twice.");
                    return;
                }

                // Remove from old index and insert at the new index (or add if at end of list).
                enabledMods.RemoveAt(oldIndex);
                if (newIndex == enabledMods.Count)
                    enabledMods.Add(dfm);
                else
                    enabledMods.Insert(newIndex, dfm);
            }
            else if (!sourceLeft && destinationLeft)
            {
                // Remove the mod from enabled list, and toss into disabled list (order doesn't matter).
                enabledMods.Remove(dfm);
                disabledMods.Add(dfm);
            }
            else if (sourceLeft && !destinationLeft)
            {
                // Insert/add from disabled to enabled.
                disabledMods.Remove(dfm);
                if (newIndex == enabledMods.Count)
                    enabledMods.Add(dfm);
                else
                    enabledMods.Insert(newIndex, dfm);
            }

            // Refind mod problems since mod order changed.
            FindModlistProblems();
        }

        public void MoveMods(List<DFHMod> mods, int newIndex, bool sourceLeft, bool destinationLeft)
        {
            if (mods == null || mods.Count == 0)
                return;

            List<DFHMod> uniqueMods = new List<DFHMod>();
            HashSet<DFHMod> seen = new HashSet<DFHMod>();
            foreach (DFHMod mod in mods)
            {
                if (seen.Add(mod))
                    uniqueMods.Add(mod);
            }

            if (uniqueMods.Count == 0)
                return;

            bool changed = false;

            if (sourceLeft && destinationLeft)
            {
                return;
            }
            else if (!sourceLeft && !destinationLeft)
            {
                HashSet<DFHMod> selectedSet = new HashSet<DFHMod>(uniqueMods);
                List<DFHMod> selectedInOrder = enabledMods.Where(m => selectedSet.Contains(m)).ToList();
                if (selectedInOrder.Count == 0)
                    return;

                int clampedIndex = Math.Max(0, Math.Min(newIndex, enabledMods.Count));
                int selectedBefore = enabledMods.Take(clampedIndex).Count(m => selectedSet.Contains(m));
                int targetIndex = clampedIndex - selectedBefore;

                List<DFHMod> remaining = enabledMods.Where(m => !selectedSet.Contains(m)).ToList();
                targetIndex = Math.Max(0, Math.Min(targetIndex, remaining.Count));

                List<DFHMod> newList = new List<DFHMod>();
                newList.AddRange(remaining.Take(targetIndex));
                newList.AddRange(selectedInOrder);
                newList.AddRange(remaining.Skip(targetIndex));

                if (!enabledMods.SequenceEqual(newList))
                {
                    enabledMods = newList;
                    changed = true;
                }
            }
            else if (!sourceLeft && destinationLeft)
            {
                HashSet<DFHMod> selectedSet = new HashSet<DFHMod>(uniqueMods);
                int beforeCount = enabledMods.Count;
                enabledMods = enabledMods.Where(m => !selectedSet.Contains(m)).ToList();
                foreach (DFHMod mod in uniqueMods)
                    disabledMods.Add(mod);
                changed = enabledMods.Count != beforeCount;
            }
            else if (sourceLeft && !destinationLeft)
            {
                foreach (DFHMod mod in uniqueMods)
                    disabledMods.Remove(mod);

                int insertIndex = Math.Max(0, Math.Min(newIndex, enabledMods.Count));
                enabledMods.InsertRange(insertIndex, uniqueMods);
                changed = true;
            }

            if (changed)
                FindModlistProblems();
        }

        // Go through modlist and scan for problems.
        // Tuple representing problem has problem mod, int problemType (missing before, missing after, conflict present), and string modID.
        public void FindModlistProblems()
        {
            // Set up list of problems to return.
            modproblems = new List<ModProblem>();

            // Set up a hashset of scanned mods and unscanned mods, for determining load order.
            HashSet<string> scannedModIDs = new HashSet<string>();
            HashSet<string> unscannedModIDs = new HashSet<string>();

            // Add all enabled mod IDs to unscanned.
            foreach (DFHMod dfm in enabledMods)
            {
                unscannedModIDs.Add(dfm.id.ToLower());
            }

            // Loop through enabled mods, doing a mock load.
            for (int i = 0; i < enabledMods.Count; i++)
            {
                DFHMod currentDFM = enabledMods[i];
                ModReference currentMod = GetRefFromDFHMod(currentDFM);

                // Check for problems.
                if (currentMod.problematic)
                {
                    foreach (string beforeID in currentMod.require_before_me)
                        if (!scannedModIDs.Contains(beforeID.ToLower()))
                        {
                            modproblems.Add(new ModProblem(currentDFM.id, beforeID, ModProblem.ProblemType.MissingBefore));
                            //Console.WriteLine("Problem found: missing before mod with ID: " + beforeID + " mod needing is: " + currentDFM.id);
                        }
                    foreach (string afterID in currentMod.require_after_me)
                        if (!unscannedModIDs.Contains(afterID.ToLower()))
                        {
                            modproblems.Add(new ModProblem(currentDFM.id, afterID, ModProblem.ProblemType.MissingAfter));
                            //Console.WriteLine("Problem found: missing after mod with ID: " + afterID + " mod needing is: " + currentDFM.id);
                        }
                    foreach (string conflictID in currentMod.conflicts_with)
                        if (scannedModIDs.Contains(conflictID.ToLower()) || unscannedModIDs.Contains(conflictID.ToLower()) )
                        {
                            modproblems.Add(new ModProblem(currentDFM.id, conflictID, ModProblem.ProblemType.ConflictPresent));
                            //Console.WriteLine("Problem found: conflict present mod with ID: " + conflictID + " mod needing is: " + currentDFM.id);
                        }
                }

                // Move to scanned.
                scannedModIDs.Add(currentDFM.id.ToLower());
                unscannedModIDs.Remove(currentDFM.id.ToLower());
            }
        }

        public bool AutoSortEnabledMods()
        {
            Dictionary<string, ModReference> idMap = new Dictionary<string, ModReference>(StringComparer.OrdinalIgnoreCase);
            foreach (ModReference modref in modrefMap.Values)
                if (!idMap.ContainsKey(modref.ID))
                    idMap.Add(modref.ID, modref);

            Dictionary<string, int> originalIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            List<ModReference> enabledRefs = new List<ModReference>();
            for (int i = 0; i < enabledMods.Count; i++)
            {
                if (modrefMap.TryGetValue(enabledMods[i].ToString(), out ModReference modref))
                {
                    enabledRefs.Add(modref);
                    if (!originalIndex.ContainsKey(modref.ID))
                        originalIndex[modref.ID] = i;
                }
            }

            HashSet<string> enabledIds = new HashSet<string>(enabledRefs.Select(m => m.ID), StringComparer.OrdinalIgnoreCase);
            Queue<ModReference> queue = new Queue<ModReference>(enabledRefs);
            while (queue.Count > 0)
            {
                ModReference current = queue.Dequeue();
                foreach (string dep in current.require_before_me.Concat(current.require_after_me))
                {
                    string depId = dep?.Trim();
                    if (string.IsNullOrEmpty(depId))
                        continue;
                    if (enabledIds.Contains(depId))
                        continue;
                    if (idMap.TryGetValue(depId, out ModReference depRef))
                    {
                        enabledIds.Add(depRef.ID);
                        queue.Enqueue(depRef);
                    }
                }
            }

            List<ModReference> allEnabled = new List<ModReference>();
            foreach (string id in enabledIds)
                if (idMap.TryGetValue(id, out ModReference modref))
                    allEnabled.Add(modref);

            Dictionary<string, (bool vanillaEntity, bool newEntity, bool reaction, bool creature, bool newStuff, bool graphics, bool beforeVanilla)> traitCache =
                new Dictionary<string, (bool vanillaEntity, bool newEntity, bool reaction, bool creature, bool newStuff, bool graphics, bool beforeVanilla)>(StringComparer.OrdinalIgnoreCase);

            List<ModReference> baseOrder = allEnabled
                .OrderBy(m => GetAutoSortGroup(m, traitCache))
                .ThenBy(m => GetReactionPriority(m))
                .ThenBy(m => originalIndex.TryGetValue(m.ID, out int idx) ? idx : int.MaxValue)
                .ThenBy(m => m.name ?? m.ID)
                .ToList();

            Dictionary<string, int> baseIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < baseOrder.Count; i++)
                baseIndex[baseOrder[i].ID] = i;

            Dictionary<string, List<string>> edges = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, int> indegree = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (ModReference modref in allEnabled)
            {
                edges[modref.ID] = new List<string>();
                indegree[modref.ID] = 0;
            }

            foreach (ModReference modref in allEnabled)
            {
                foreach (string dep in modref.require_before_me)
                {
                    string depId = dep?.Trim();
                    if (string.IsNullOrEmpty(depId) || !enabledIds.Contains(depId))
                        continue;
                    edges[depId].Add(modref.ID);
                    indegree[modref.ID]++;
                }
                foreach (string dep in modref.require_after_me)
                {
                    string depId = dep?.Trim();
                    if (string.IsNullOrEmpty(depId) || !enabledIds.Contains(depId))
                        continue;
                    edges[modref.ID].Add(depId);
                    indegree[depId]++;
                }
            }

            List<string> available = new List<string>();
            foreach (KeyValuePair<string, int> kv in indegree)
                if (kv.Value == 0)
                    available.Add(kv.Key);

            List<string> sortedIds = new List<string>();
            while (available.Count > 0)
            {
                string next = available.OrderBy(id => baseIndex.TryGetValue(id, out int idx) ? idx : int.MaxValue).First();
                available.Remove(next);
                sortedIds.Add(next);
                foreach (string dest in edges[next])
                {
                    indegree[dest]--;
                    if (indegree[dest] == 0)
                        available.Add(dest);
                }
            }

            if (sortedIds.Count != enabledIds.Count)
                sortedIds = baseOrder.Select(m => m.ID).ToList();

            List<DFHMod> sortedMods = new List<DFHMod>();
            foreach (string id in sortedIds)
                if (idMap.TryGetValue(id, out ModReference modref))
                    sortedMods.Add(modref.ToDFHMod());

            bool changed = sortedMods.Count != enabledMods.Count;
            if (!changed)
                for (int i = 0; i < sortedMods.Count; i++)
                    if (sortedMods[i] != enabledMods[i])
                    {
                        changed = true;
                        break;
                    }

            if (changed)
            {
                SetActiveMods(sortedMods);
                FindModlistProblems();
            }

            return changed;
        }

        private int GetAutoSortGroup(ModReference modref, Dictionary<string, (bool vanillaEntity, bool newEntity, bool reaction, bool creature, bool newStuff, bool graphics, bool beforeVanilla)> traitCache)
        {
            var traits = GetModTraits(modref, traitCache);
            if (traits.beforeVanilla || (modref.name ?? "").IndexOf("better instruments", StringComparison.OrdinalIgnoreCase) >= 0)
                return 0;
            if (traits.vanillaEntity)
                return 0;
            if (traits.newEntity)
                return 1;
            if (traits.reaction)
                return 4;
            if (traits.graphics)
                return 2;
            if (traits.newStuff)
                return 5;
            return 3;
        }

        private int GetReactionPriority(ModReference modref)
        {
            string label = ((modref.name ?? "") + " " + modref.ID).ToLowerInvariant();
            if (label.Contains("set production"))
                return 0;
            if (label.Contains("smelt ore by product"))
                return 1;
            if (label.Contains("stone beds") || label.Contains("stoneworking expanded"))
                return 2;
            if (label.Contains("specific decoration"))
                return 3;
            if (label.Contains("fermented milk"))
                return 4;
            return 100;
        }

        private (bool vanillaEntity, bool newEntity, bool reaction, bool creature, bool newStuff, bool graphics, bool beforeVanilla) GetModTraits(
            ModReference modref,
            Dictionary<string, (bool vanillaEntity, bool newEntity, bool reaction, bool creature, bool newStuff, bool graphics, bool beforeVanilla)> traitCache)
        {
            if (traitCache.TryGetValue(modref.ID, out var cached))
                return cached;

            bool vanillaEntity = false;
            bool newEntity = false;
            bool reaction = false;
            bool creature = false;
            bool newStuff = false;
            bool graphics = false;
            bool beforeVanilla = false;

            string infoPath = Path.Combine(modref.path, "info.txt");
            if (File.Exists(infoPath))
            {
                string info = File.ReadAllText(infoPath);
                if (info.IndexOf("before vanilla", StringComparison.OrdinalIgnoreCase) >= 0)
                    beforeVanilla = true;
                if (info.IndexOf("graphics", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    info.IndexOf("tileset", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    info.IndexOf("tile set", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    info.IndexOf("portrait", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    info.IndexOf("sprite", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    info.IndexOf("landscape", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    info.IndexOf("stone variation", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    info.IndexOf("rounded hills", StringComparison.OrdinalIgnoreCase) >= 0)
                    graphics = true;
            }

            if (Directory.Exists(Path.Combine(modref.path, "graphics")) ||
                Directory.Exists(Path.Combine(modref.path, "raw", "graphics")))
                graphics = true;

            if (Directory.Exists(modref.path))
            {
                foreach (string file in Directory.EnumerateFiles(modref.path, "*.txt", SearchOption.AllDirectories))
                {
                    string lowerPath = file.ToLowerInvariant();
                    if (lowerPath.Contains("\\graphics\\") || lowerPath.Contains("/graphics/"))
                        graphics = true;
                    if (!lowerPath.Contains("\\raw\\") && !lowerPath.Contains("/raw/"))
                        continue;

                    string text;
                    try
                    {
                        text = File.ReadAllText(file);
                    }
                    catch
                    {
                        continue;
                    }

                    if (!reaction && text.IndexOf("[REACTION:", StringComparison.OrdinalIgnoreCase) >= 0)
                        reaction = true;
                    if (!creature && text.IndexOf("[CREATURE:", StringComparison.OrdinalIgnoreCase) >= 0)
                        creature = true;
                    if (!newStuff &&
                        (text.IndexOf("[INORGANIC:", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         text.IndexOf("[PLANT:", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         text.IndexOf("[ITEM_", StringComparison.OrdinalIgnoreCase) >= 0))
                        newStuff = true;

                    if (!vanillaEntity || !newEntity)
                    {
                        MatchCollection entityMatches = Regex.Matches(text, @"\[ENTITY:([^\]]+)\]", RegexOptions.IgnoreCase);
                        foreach (Match match in entityMatches)
                        {
                            string ent = match.Groups[1].Value.Trim();
                            if (IsVanillaEntity(ent))
                                vanillaEntity = true;
                            else if (!string.IsNullOrEmpty(ent))
                                newEntity = true;
                        }
                    }

                    if (reaction && creature && newStuff && graphics && (vanillaEntity || newEntity))
                        break;
                }
            }

            if (creature)
                newStuff = true;

            var result = (vanillaEntity, newEntity, reaction, creature, newStuff, graphics, beforeVanilla);
            traitCache[modref.ID] = result;
            return result;
        }

        private bool IsVanillaEntity(string id)
        {
            switch (id.ToUpperInvariant())
            {
                case "DWARF":
                case "ELF":
                case "HUMAN":
                case "GOBLIN":
                case "KOBOLD":
                    return true;
            }
            return false;
        }

        #region initialization file stuff

        // Find modpacks from dfhack mod-manager config file.
        private void FindModpacks()
        {
            // Get paths and read file. #TODO: handling the file not existing.
            string dfHackModpackPath = Path.Combine(config.DFFolderPath, @"dfhack-config\mod-manager.json");
            string dfHackModpackJson = File.ReadAllText(dfHackModpackPath);
            modpacks = new List<DFHModpack>(JsonSerializer.Deserialize<List<DFHModpack>>(dfHackModpackJson));

            Console.WriteLine();
            Console.WriteLine("Found modlists: ");

            // Handle mods missing.
            bool modMissing = false;
            string missingMessage = $"Some mods missing. \nModlists will be modified to not require lost mods. \nMissing mods: ";
            HashSet<DFHMod> notFound = new HashSet<DFHMod>();

            // If a default modpack exists.
            bool defaultFound = false;

            // Go through modpacks, and go through their modlists, looking for mods that we don't have.
            for (int i = 0; i < modpacks.Count; i++)
            {
                DFHModpack modlist = modpacks[i];

                HashSet<DFHMod> thisListMissingMods = new HashSet<DFHMod>();
                foreach(DFHMod mod in modlist.modlist)
                {
                    if(!modPool.Contains(mod))
                    {
                        modMissing = true;
                        notFound.Add(mod);
                        thisListMissingMods.Add(mod);
                        missingMessage += $"\n{mod}";
                    }
                }
                
                // Remove the missing mods from the modlist.
                foreach(DFHMod m in thisListMissingMods)
                {
                    modlist.modlist.Remove(m);
                }

                // Write out some info on the modpack
                Console.WriteLine("   Name: " + modlist.name);
                Console.WriteLine("   Default: " + modlist.@default);
                Console.WriteLine("   Mods count: " + modlist.modlist.Count);
                Console.WriteLine();

                // If  this is the default modpack, set index to that. 
                if (modlist.@default)
                {
                    SetSelectedModpack(i);
                    defaultFound = true;
                }

                // Set modpacks[i] back to this modpack. #FIXME: why is this necessary? Isn't modpack a reference type?
                modpacks[i] = modlist;
            }

            // Set default as backup.
            if (!defaultFound)
            {
                SetSelectedModpack(0);
                modpacks[0].@default = true;
                SaveAllModpacks();
            }

            // Create default modpack if none present.
            if(modpacks.Count == 0)
            {
                DFHModpack newPack = new DFHModpack(true, new List<DFHMod>(), "Default");
                // FIXME: generate vanilla modpack in a better way than this
                newPack.modlist = GenerateVanillaModlist();

            }

            // Pop up a message notifying the user that the missing mods have been removed.
            if(modMissing)
            {
                MessageBox.Show(missingMessage, "Missing Mods", MessageBoxButtons.OK);
            }
        }

        // Generated a vanilla modlist using manually generated mod ID list.
        public List<DFHMod> GenerateVanillaModlist()
        {
            // Manually made vanilla modlist.
            List<string> vanillaModIDList = new List<string>()
                {
                    "vanilla_text",
                    "vanilla_languages",
                    "vanilla_descriptors",
                    "vanilla_materials",
                    "vanilla_environment",
                    "vanilla_plants",
                    "vanilla_items",
                    "vanilla_buildings",
                    "vanilla_bodies",
                    "vanilla_creatures",
                    "vanilla_entities",
                    "vanilla_reactions",
                    "vanilla_interactions",
                    "vanilla_descriptors_graphics",
                    "vanilla_plants_graphics",
                    "vanilla_items_graphics",
                    "vanilla_buildings_graphics",
                    "vanilla_creatures_graphics",
                    "vanilla_world_map",
                    "vanilla_interface",
                    "vanilla_music",
                };

            // Get all mods that are probably vanilla.
            Dictionary<string, DFHMod> vanillaPool = new Dictionary<string, DFHMod>();
            foreach (DFHMod dfm in modPool)
            {
                if (dfm.id.ToLower().Contains("vanilla"))
                {
                    vanillaPool.Add(dfm.id, dfm);
                    Console.WriteLine("added mod with id " +  dfm.id);
                }
            }

            // Load vanilla mods into pack.
            List<DFHMod> vanillaList = new List<DFHMod>();
            for (int i = 0; i < vanillaModIDList.Count; i++)
            {
                Console.WriteLine("looking for mod with id " + vanillaModIDList[i]);
                vanillaList.Add(vanillaPool[vanillaModIDList[i]]);
            }

            return vanillaList;
        }

        // Get the theme from config.
        public int GetTheme()
        {
            return config.theme;
        }
        
        // Save the theme to config file.
        public void SetTheme(int theme)
        {
            config.theme = theme;
            SaveConfigFile();
        }


        // Fix the config file if it's broken or missing.
        public void FixConfig()
        {
            // If it's missing create it.
            if (config == null)
                config = new ModHearthConfig();

            // If it's missing the path to dwarf fortress executable, get the path.
            if (String.IsNullOrEmpty(config.DFEXEPath))
            {
                Console.WriteLine("Config file missing DF path.");
                string newPath = "";
                while (string.IsNullOrEmpty(newPath))
                {
                    newPath = GetDFPath();
                }
                config.DFEXEPath = newPath;
            }

            if (string.IsNullOrWhiteSpace(config.InstalledModsPath) || !Directory.Exists(config.InstalledModsPath))
            {
                if (string.IsNullOrWhiteSpace(config.InstalledModsPath))
                    Console.WriteLine("Config file missing installed mods path.");
                else
                    Console.WriteLine("Installed mods path not found.");

                string defaultPath = GetDefaultInstalledModsPath();
                if (Directory.Exists(defaultPath))
                {
                    config.InstalledModsPath = defaultPath;
                }
                else
                {
                    string newPath = "";
                    while (string.IsNullOrWhiteSpace(newPath) || !Directory.Exists(newPath))
                    {
                        newPath = GetInstalledModsPathFromUser();
                    }
                    config.InstalledModsPath = newPath;
                }
            }

            // Save the fixed config file.
            SaveConfigFile();
        }

        // Destroy the config file, to be remade.
        public void DestroyConfig()
        {
            if (File.Exists(configPath))
            {
                File.Delete(configPath);
            }
        }

        // Get the path to the dwarf fortress executable from the user.
        private string GetDFPath()
        {
            LocationMessageBox.Show("Please find the path to your Dwarf Fortress.exe.", "DF.exe location", MessageBoxButtons.OK);
            OpenFileDialog dfFileDialog = new OpenFileDialog();
            dfFileDialog.Filter = "Executable files (*.exe)|Dwarf Fortress.exe";
            DialogResult result = dfFileDialog.ShowDialog();
            if (result == DialogResult.OK)
            {
                string selectedFilePath = dfFileDialog.FileName;
                Console.WriteLine("DF path set to: " + selectedFilePath);
                return selectedFilePath;
            }
            return "";
        }

        private string GetInstalledModsPathFromUser()
        {
            string defaultPath = GetDefaultInstalledModsPath();
            LocationMessageBox.Show("Please select your Dwarf Fortress installed_mods folder.", "installed_mods location", MessageBoxButtons.OK);
            using FolderBrowserDialog folderDialog = new FolderBrowserDialog();
            folderDialog.Description = "Select the installed_mods folder";
            if (Directory.Exists(defaultPath))
                folderDialog.SelectedPath = defaultPath;
            DialogResult result = folderDialog.ShowDialog();
            if (result == DialogResult.OK && Directory.Exists(folderDialog.SelectedPath))
            {
                Console.WriteLine("Installed mods path set to: " + folderDialog.SelectedPath);
                return folderDialog.SelectedPath;
            }
            return "";
        }

        // Attmempt loading config. If broken or failed, run FixConfig.
        public void AttemptLoadConfig()
        {
            Console.WriteLine("Attempting config file load.");
            try
            {
                if (File.Exists(configPath))
                {
                    Console.WriteLine("Config file found.");
                    string jsonContent = File.ReadAllText(configPath);

                    // Deserialize the JSON content into an object
                    config = JsonSerializer.Deserialize<ModHearthConfig>(jsonContent);

                    if (config == null)
                    {
                        Console.WriteLine("Config file borked.");
                        FixConfig();
                    }
                }
                else
                {
                    Console.WriteLine("Config file missing.");
                    FixConfig();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred: {ex.Message}");
                config = null;
                FixConfig();

            }
        }

        // Save the config to file.
        public void SaveConfigFile()
        {
            Console.WriteLine("Config saved.");
            JsonSerializerOptions options = new JsonSerializerOptions
            {
                WriteIndented = true // Enable pretty formatting
            };
            string jsonContent = JsonSerializer.Serialize(config);
            File.WriteAllText(configPath, jsonContent);
        }

        // Try to load style file.
        public Style LoadStyle()
        {
            Style style = new Style();
            int theme = GetTheme();
            string stylePath = GetStylePathForTheme(theme);

            try
            {
                if (File.Exists(stylePath))
                {
                    Console.WriteLine("Style file found.");
                    if (!TryLoadStyleFromPath(stylePath, out style))
                    {
                        Console.WriteLine("Style file borked. Style regenerated.");
                        style = GetDefaultStyleForTheme(theme);
                        SaveStyle(style, stylePath);
                    }
                }
                else if (File.Exists(styleLegacyPath))
                {
                    Console.WriteLine("Legacy style file found. Migrating.");
                    if (!TryLoadStyleFromPath(styleLegacyPath, out style))
                    {
                        Console.WriteLine("Legacy style file borked. Style regenerated.");
                        style = GetDefaultStyleForTheme(theme);
                        SaveStyle(style, stylePath);
                    }
                    else
                    {
                        SaveStyle(style, stylePath);
                    }
                }
                else
                {
                    Console.WriteLine("Style file missing. New style reated.");
                    style = GetDefaultStyleForTheme(theme);
                    SaveStyle(style, stylePath);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred: {ex.Message}");
            }


            // Set global instance and return.
            Style.instance = style;
            return style;
        } 

        // Save the style to file.
        private void SaveStyle(Style style, string stylePath)
        {
            Console.WriteLine("Style saved.");
            JsonSerializerOptions options = new JsonSerializerOptions
            {
                WriteIndented = true // Enable pretty formatting
            };
            string jsonContent = JsonSerializer.Serialize(style, options);
            File.WriteAllText(stylePath, jsonContent);
        }

        private string GetStylePathForTheme(int theme)
        {
            return theme == 0 ? styleLightPath : styleDarkPath;
        }

        private Style GetDefaultStyleForTheme(int theme)
        {
            string stylePath = GetStylePathForTheme(theme);
            if (TryLoadStyleFromPath(stylePath, out Style style))
                return style;

            if (TryLoadStyleFromPath(styleLegacyPath, out style))
                return style;

            return GetFallbackStyle();
        }

        private bool TryLoadStyleFromPath(string stylePath, out Style style)
        {
            style = null;
            if (!File.Exists(stylePath))
                return false;

            try
            {
                string jsonContent = File.ReadAllText(stylePath);
                Style foundStyle = JsonSerializer.Deserialize<Style>(jsonContent);
                if (foundStyle == null)
                    return false;
                style = EnsureStyleDefaults(foundStyle);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private Style EnsureStyleDefaults(Style style)
        {
            Style fallback = GetFallbackStyle();
            style.modRefColor ??= fallback.modRefColor;
            style.modRefHighlightColor ??= fallback.modRefHighlightColor;
            style.modRefJumpHighlightColor ??= fallback.modRefJumpHighlightColor;
            style.modRefPanelColor ??= fallback.modRefPanelColor;
            style.modRefTextColor ??= fallback.modRefTextColor;
            style.modRefTextBadColor ??= fallback.modRefTextBadColor;
            style.modRefTextFilteredColor ??= fallback.modRefTextFilteredColor;
            style.formColor ??= fallback.formColor;
            style.textColor ??= fallback.textColor;
            return style;
        }

        private Style GetFallbackStyle()
        {
            return new Style
            {
                modRefColor = SystemColors.ControlLight,
                modRefHighlightColor = SystemColors.Highlight,
                modRefJumpHighlightColor = SystemColors.Highlight,
                modRefPanelColor = SystemColors.Window,
                modRefTextColor = SystemColors.WindowText,
                modRefTextBadColor = Color.Red,
                modRefTextFilteredColor = SystemColors.GrayText,
                formColor = SystemColors.Window,
                textColor = SystemColors.WindowText
            };
        }
        #endregion
    }
}
