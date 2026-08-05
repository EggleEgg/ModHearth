using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Threading;
using ModHearth.UI;
using Xunit;
using Xunit.Abstractions;

namespace ModHearth.App.Tests;

/// <summary>
/// Handles Avalonia headless app bootstrap once for the test class lifetime.
/// </summary>
public class AvaloniaAppFixture
{
    public AvaloniaAppFixture()
    {
        try
        {
            ConfigManager.AttemptLoadConfig(false);
            try { _ = ConfigManager.LoadStyle(false); } catch { /* Ignore missing style files in test environments */ }

            string testEnvDir = Path.Combine(AppContext.BaseDirectory, "test_env");
            _ = Directory.CreateDirectory(testEnvDir);
            _ = Directory.CreateDirectory(Path.Combine(testEnvDir, "data", "installed-mods"));
            _ = Directory.CreateDirectory(Path.Combine(testEnvDir, "hack"));
            string dfhackRunPath = Path.Combine(testEnvDir, "hack", OperatingSystem.IsWindows() ? "dfhack-run.exe" : "dfhack-run");
            if (!File.Exists(dfhackRunPath))
                File.WriteAllText(dfhackRunPath, string.Empty);

            ConfigManager.SetDwarfFortressFolderPath(testEnvDir);
            ConfigManager.SetDFHackFolderPath(Path.Combine(testEnvDir, "hack"));
            ConfigManager.SetInstalledModsPath(Path.Combine(testEnvDir, "data", "installed-mods"));
        }
        catch
        {
            // Fallback for headless test environments
        }
    }
}

/// <summary>
/// Smoke tests to verify that all Avalonia windows, dialogs, and custom controls can be instantiated
/// and initialized without crashing due to styling, theme, resource, binding, or real mod data mismatches.
/// </summary>
public class AvaloniaUiSmokeTests : IClassFixture<AvaloniaAppFixture>
{
    private readonly ITestOutputHelper _output;

    public AvaloniaUiSmokeTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact(Timeout = 20000)]
    public async Task Can_Instantiate_All_UI_Components_And_Pump_Events_Without_Crash()
    {
        try
        {
            if (Avalonia.Application.Current == null)
            {
                _output.WriteLine("AvaloniaUiSmokeTests: Initializing Avalonia application on the current test thread.");
                _ = Program.BuildAvaloniaApp().SetupWithoutStarting();
            }
        }
        catch (Exception ex)
        {
            _output.WriteLine($"AvaloniaUiSmokeTests: Failed to initialize application: {ex}");
        }

        _output.WriteLine("AvaloniaUiSmokeTests: Starting UI component instantiation test.");

        // 1. Resolve test mod data
        string exampleModDir = FindExampleModPath();
        _output.WriteLine($"AvaloniaUiSmokeTests: Example mod path resolved to: '{exampleModDir}'.");
        ModReference? exampleModRef = LoadModReferenceFromDirectory(exampleModDir);
        ModRefViewModel? exampleModVm = exampleModRef != null ? new(exampleModRef) : null;
        _output.WriteLine("AvaloniaUiSmokeTests: Mod reference and view model loaded.");

        // 2. Instantiate and verify UserControls & Views
        _output.WriteLine("AvaloniaUiSmokeTests: Instantiating UserControls & Views...");
        var controls = new Control[]
        {
            new ModColorPicker(),
            new ModRefControl { DataContext = exampleModVm },
            new ModSearchBar(),
            new ModUpdateLogControl(),
            new SortRulesControl(),
            new WorkshopDownloaderControl(),
            new ModDataEntryView { DataContext = exampleModRef },
            new ModDataPanelView { DataContext = exampleModRef },
            new ModDescriptionPanelView { DataContext = exampleModRef },
            new ModPreviewPanelView { DataContext = exampleModRef }
        };

        foreach (var control in controls)
        {
            Assert.NotNull(control);
        }
        _output.WriteLine("AvaloniaUiSmokeTests: UserControls & Views instantiated successfully.");

        // 3. Instantiate and verify Dialogs
        _output.WriteLine("AvaloniaUiSmokeTests: Instantiating Dialogs...");
        var dialogs = new Window[]
        {
            new CollectionChecklistDialog(),
            new InputDialog(),
            new MessageDialog(),
            new SteamCmdSetupDialog(),
            new UpdateDialog()
        };

        foreach (var dialog in dialogs)
        {
            Assert.NotNull(dialog);
        }
        _output.WriteLine("AvaloniaUiSmokeTests: Dialogs instantiated successfully.");

        // 4. Instantiate and verify Windows & Apply Themes
        _output.WriteLine("AvaloniaUiSmokeTests: Instantiating Windows...");
        var candidates = exampleModVm != null ? new List<ModRefViewModel> { exampleModVm } : [];
        var relationshipWindow = new RelationshipPickerWindow(
            "test_mod",
            ModRelationshipKind.Before,
            candidates,
            new HashSet<string>(),
            new Dictionary<string, ModRelationshipRule>()
        );

        var windows = new Window[]
        {
            new ModUpdateLogWindow(),
            new SortRulesWindow(),
            new WorkshopDownloaderWindow(),
            relationshipWindow,
            new MainWindow()
        };

        foreach (var window in windows)
        {
            Assert.NotNull(window);
            if (Style.instance != null)
            {
                WindowThemeManager.ApplyToWindow(window, Style.instance);
            }
        }
        _output.WriteLine("AvaloniaUiSmokeTests: Windows instantiated and themed successfully.");

        // 5. Pump UI event loop for 2 seconds to catch binding/layout failures or timer ticks
        _output.WriteLine("AvaloniaUiSmokeTests: Pumping UI event loop...");
        DateTime end = DateTime.UtcNow.AddSeconds(2);
        int iteration = 0;
        while (DateTime.UtcNow < end)
        {
            _output.WriteLine($"AvaloniaUiSmokeTests: Pumping iteration {iteration} - calling RunJobs()...");
            Dispatcher.UIThread.RunJobs();
            _output.WriteLine($"AvaloniaUiSmokeTests: Pumping iteration {iteration} - sleeping...");
            await Task.Delay(100);
            iteration++;
        }
        _output.WriteLine("AvaloniaUiSmokeTests: UI event loop completed successfully.");
    }

    private static string FindExampleModPath()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            foreach (string subPath in new[] { "ModHearth.App.Tests/example_mod", "example_mod" })
            {
                string candidate = Path.Combine(dir.FullName, subPath);
                if (Directory.Exists(candidate))
                    return candidate;
            }
        }

        return string.Empty;
    }

    private static ModReference? LoadModReferenceFromDirectory(string modDir)
    {
        if (string.IsNullOrEmpty(modDir) || !Directory.Exists(modDir))
            return null;

        string infoPath = Path.Combine(modDir, "info.txt");
        if (!File.Exists(infoPath))
            return null;

        string infoContent = File.ReadAllText(infoPath);

        string GetTag(string tagName)
        {
            Match m = Regex.Match(infoContent, $@"\[{tagName}:([^\]]+)\]", RegexOptions.IgnoreCase);
            return m.Success ? m.Groups[1].Value.Trim() : string.Empty;
        }

        string[] tags =
        [
            "ID", "NUMERIC_VERSION", "DISPLAYED_VERSION",
            "EARLIEST_COMPATIBLE_NUMERIC_VERSION", "EARLIEST_COMPATIBLE_DISPLAYED_VERSION",
            "AUTHOR", "NAME", "DESCRIPTION", "STEAM_FILE_ID", "STEAM_TITLE", "STEAM_DESCRIPTION"
        ];

        var memoryData = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["src_dir"] = modDir
        };

        foreach (string tag in tags)
        {
            memoryData[tag.ToLowerInvariant()] = GetTag(tag);
        }

        return new ModReference(memoryData);
    }
}