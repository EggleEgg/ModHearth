using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using ModHearth.Models;
using ModHearth.UI;
using ModHearth.UI.Theme;
using ModHearth.UI.ViewModels;
using ModHearth.UI.Windows;
using Xunit;

namespace ModHearth.App.Tests
{
    /// <summary>
    /// Smoke tests to verify that all Avalonia windows, dialogs, and custom controls can be
    /// instantiated, themed, and have their UI actions exercised without crashing.
    /// </summary>
    /// <remarks>
    /// Views are discovered through reflection, so adding a new <see cref="Window"/> or
    /// <see cref="UserControl"/> automatically adds it to this test. Event subscriptions are
    /// discovered from the realized logical tree and every click must be classified in
    /// <see cref="UiActionPolicy"/>; otherwise the test fails and asks for a review.
    /// <see cref="UiActionRunner"/> shows windows while their actions run, so <c>Opened</c>/<c>Loaded</c>
    /// handlers, templates, and layout are covered as well.
    /// </remarks>
    [Collection(AvaloniaUiTestCollection.Name)]
    public class AvaloniaUiSmokeTests(ITestOutputHelper output)
    {
        private static readonly TimeSpan EventLoopPumpDuration = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan EventLoopPumpInterval = TimeSpan.FromMilliseconds(50);

        private readonly ITestOutputHelper _output = output;

        [AvaloniaFact(Timeout = 120_000)]
        public async Task CanInstantiateAllUIComponentsAndPumpEventsWithoutCrash()
        {
            AvaloniaTestEnvironment.EnsureInitialized(_output.WriteLine);

            _output.WriteLine("AvaloniaUiSmokeTests: Starting dynamic UI component instantiation test.");

            // 1. Resolve test mod data
            string exampleModDir = ExampleModTestData.FindPath();
            _output.WriteLine($"AvaloniaUiSmokeTests: Example mod path resolved to: '{exampleModDir}'.");
            ModReference? exampleModRef = ExampleModTestData.LoadModReference(exampleModDir);
            ModRefViewModel? exampleModVm = exampleModRef != null ? new(exampleModRef) : null;
            _output.WriteLine("AvaloniaUiSmokeTests: Mod reference and view model loaded.");

            // 2. Views with constructor arguments or required data context need an explicit factory.
            //    Everything else is created through its public parameterless constructor.
            List<ModRefViewModel> candidates = exampleModVm != null ? [exampleModVm] : [];
            Dictionary<Type, Func<Control>> factories = new()
            {
                [typeof(ModRefControl)] = () => new ModRefControl { DataContext = exampleModVm },
                [typeof(ModDataEntryView)] = () => new ModDataEntryView { DataContext = exampleModRef },
                [typeof(ModDataPanelView)] = () => new ModDataPanelView { DataContext = exampleModRef },
                [typeof(ModDescriptionPanelView)] = () => new ModDescriptionPanelView { DataContext = exampleModRef },
                [typeof(ModPreviewPanelView)] = () => new ModPreviewPanelView { DataContext = exampleModRef },
                [typeof(RelationshipPickerWindow)] = () => new RelationshipPickerWindow(
                    "test_mod",
                    ModRelationshipKind.Before,
                    candidates,
                    new HashSet<string>())
            };

            Type[] viewTypes = [.. UiActionDiscovery.GetViewTypes(typeof(MainWindow).Assembly)];
            _output.WriteLine($"AvaloniaUiSmokeTests: Discovered {viewTypes.Length} view types.");

            List<UiActionRoot> roots = [];
            List<Type> missingFactories = [];
            List<DiscoveredUiAction> discoveredActions = [];

            foreach (Type viewType in viewTypes)
            {
                Func<Control>? factory;
                if (factories.TryGetValue(viewType, out Func<Control>? explicitFactory))
                    factory = (Func<Control>?)explicitFactory;
                else
                {
                    factory = UiActionDiscovery.HasPublicParameterlessConstructor(viewType)
                        ? () => (Control)Activator.CreateInstance(viewType)!
                        : null;
                }

                if (factory == null)
                {
                    missingFactories.Add(viewType);
                    continue;
                }

                roots.Add(new UiActionRoot(viewType, factory));

                // Instantiate, theme, and inspect the view once. The action runner creates its own
                // instances so one action cannot leave state that changes the next action's result.
                Control control = factory();
                try
                {
                    if (control is Window window && Style.instance != null)
                        WindowThemeManager.ApplyToWindow(window, Style.instance);

                    discoveredActions.AddRange(UiActionDiscovery.Discover(control));
                }
                finally
                {
                    UiActionRunner.DisposeRoot(control);
                }
            }

            Assert.True(
                missingFactories.Count == 0,
                "Every Window/UserControl must be constructible. Add an explicit factory in " +
                "AvaloniaUiSmokeTests for these types:\n  " +
                string.Join("\n  ", missingFactories.Select(type => type.FullName)));

            _output.WriteLine($"AvaloniaUiSmokeTests: Instantiated and themed {roots.Count} views.");
            _output.WriteLine($"AvaloniaUiSmokeTests: Discovered {discoveredActions.Count} app-owned event subscriptions.");

            // 3. Every discovered click must have a policy entry. This is what makes new UI actions
            //    fail loudly instead of quietly escaping the smoke test.
            List<UiActionKey> distinctKeys = [.. discoveredActions
                .Select(action => action.Key)
                .Distinct()
                .OrderBy(key => key.RootType, StringComparer.Ordinal)
                .ThenBy(key => key.ControlName, StringComparer.Ordinal)
                .ThenBy(key => key.EventName, StringComparer.Ordinal)];

            Assert.NotEmpty(distinctKeys);

            UiActionKey[] unclassified = [.. distinctKeys.Where(key => UiActionPolicy.Classify(key) == null)];
            Assert.True(
                unclassified.Length == 0,
                "New UI actions were discovered without an explicit classification. Add an " +
                "UiActionPolicy.Invoke(...) or UiActionPolicy.Skip(...) entry for each of:\n  " +
                string.Join("\n  ", unclassified));

            // 4. Run the approved actions on fresh view instances.
            _output.WriteLine("AvaloniaUiSmokeTests: Executing classified UI actions...");
            UiActionRunResult run = await UiActionRunner.RunAsync(roots, TestContext.Current.CancellationToken);

            _output.WriteLine(
                $"AvaloniaUiSmokeTests: Invoked {run.Invoked.Count} action keys, skipped {run.Skipped.Count}, " +
                $"showed {run.WindowsShown} windows, failures {run.Failures.Count}.");

            Assert.Multiple(
                () => Assert.Empty(run.Unclassified),
                () => Assert.Empty(run.Failures));

            // 5. Let the dispatcher loop run for a while to catch binding/layout failures or timer
            //    ticks. Awaiting keeps the continuation on the Avalonia dispatcher thread, so the
            //    real dispatcher loop keeps processing timers, bindings, and layout.
            _output.WriteLine("AvaloniaUiSmokeTests: Pumping UI event loop...");
            DateTime end = DateTime.UtcNow + EventLoopPumpDuration;
            int iteration = 0;
            while (DateTime.UtcNow < end)
            {
                await Task.Delay(EventLoopPumpInterval, TestContext.Current.CancellationToken);
                iteration++;
            }

            _output.WriteLine($"AvaloniaUiSmokeTests: UI event loop completed successfully after {iteration} iterations.");
        }
    }
}