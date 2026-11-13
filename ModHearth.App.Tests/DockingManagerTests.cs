using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using ModHearth.UI.Docking;
using Xunit;

namespace ModHearth.App.Tests
{
    /// <summary>
    /// Regression tests for the dock/floating-window teardown paths. These drive the real
    /// <see cref="DockingManager{TControl,TWindow}"/> state machine (open, user-close the floating
    /// window, dock, re-open, dispose) because the shared detach helpers it uses must stay
    /// re-entrant and must always leave the manager in a usable state.
    /// </summary>
    [Collection(AvaloniaUiTestCollection.Name)]
    public class DockingManagerTests
    {
        [AvaloniaFact]
        public void ClosingFloatingWindowDetachesAndAllowsReopen()
        {
            (Window parent, DockingTarget target) = CreateParentWindow();
            DockingManager<TestDockControl, TestDockWindow> manager = CreateManager(parent, target, initialDocked: false);
            using (manager)
            {
                parent.Show();

                manager.Open();
                Assert.False(manager.IsDocked);
                Assert.True(manager.IsOpen);
                TestDockWindow floating = Assert.IsType<TestDockWindow>(manager.FloatingWindow);

                // User closes the floating window: the Closed handler must detach the tracker and
                // window without leaving a dangling reference.
                floating.Close();

                Assert.Null(manager.FloatingWindow);
                Assert.False(manager.IsOpen);

                // Closing again after the window already went away must be a no-op, not a double-close.
                manager.Close();
                Assert.Null(manager.FloatingWindow);

                // The manager must still be usable after an externally closed window.
                manager.Open();
                Assert.NotNull(manager.FloatingWindow);
                Assert.NotSame(floating, manager.FloatingWindow);
                manager.Close();
            }

            parent.Close();
        }

        [AvaloniaFact]
        public void DockingDetachesFloatingWindowAndClosingRestoresDockState()
        {
            (Window parent, DockingTarget target) = CreateParentWindow();
            DockingManager<TestDockControl, TestDockWindow> manager = CreateManager(parent, target, initialDocked: false);
            using (manager)
            {
                parent.Show();

                manager.Open();
                Assert.NotNull(manager.FloatingWindow);

                manager.SetDocked(true);

                // Headless screen metrics can reject docking; when they do, the manager must keep the
                // floating window rather than end up in a half-torn-down state.
                if (manager.IsDocked)
                {
                    Assert.Null(manager.FloatingWindow);
                    Assert.Same(manager.SharedControl, target.DockHostControl.Content);

                    // Closing the docked panel clears the host and contracts the parent again.
                    manager.Close();
                    Assert.Null(target.DockHostControl.Content);
                    Assert.False(manager.IsOpen);
                    Assert.False(target.DockHostControl.IsVisible);

                    manager.Open();
                    Assert.True(manager.IsDocked);
                    Assert.Same(manager.SharedControl, target.DockHostControl.Content);
                    manager.Close();
                }
                else
                {
                    Assert.NotNull(manager.FloatingWindow);
                    Assert.Null(target.DockHostControl.Content);
                    manager.Close();
                }
            }

            parent.Close();
        }

        [AvaloniaFact]
        public void DisposeIsIdempotentAndClosesOpenWindows()
        {
            (Window parent, DockingTarget target) = CreateParentWindow();
            DockingManager<TestDockControl, TestDockWindow> manager = CreateManager(parent, target, initialDocked: false);

            parent.Show();
            manager.Open();
            TestDockWindow floating = Assert.IsType<TestDockWindow>(manager.FloatingWindow);

            manager.Dispose();

            Assert.False(floating.IsVisible);
            Assert.Null(manager.FloatingWindow);
            Assert.False(manager.IsOpen);

            // A second Dispose must not throw or re-close an already detached window.
            manager.Dispose();

            parent.Close();
        }

        private static (Window Parent, DockingTarget Target) CreateParentWindow()
        {
            Grid grid = new()
            {
                ColumnDefinitions = new ColumnDefinitions("0,0")
            };
            ContentControl dockHost = new() { IsVisible = false };
            Border splitter = new();
            grid.Children.Add(splitter);
            grid.Children.Add(dockHost);

            DockingTarget target = new()
            {
                MainGrid = grid,
                Side = DockSide.Right,
                SplitterIndex = 0,
                ContentIndex = 1,
                SplitterControl = splitter,
                DockHostControl = dockHost
            };

            Window parent = new()
            {
                Width = 800,
                Height = 600,
                MinWidth = 200,
                MinHeight = 200,
                Content = grid
            };

            return (parent, target);
        }

        private static DockingManager<TestDockControl, TestDockWindow> CreateManager(
            Window parent,
            DockingTarget target,
            bool initialDocked)
            => new(
                parent,
                new DockingManagerOptions<TestDockControl, TestDockWindow>
                {
                    SideTargets = new Dictionary<DockSide, DockingTarget> { [DockSide.Right] = target },
                    DefaultSide = DockSide.Right,
                    ControlCreator = static () => new TestDockControl(),
                    WindowCreator = static control => new TestDockWindow { Content = control, Width = 320, Height = 480 },
                    DefaultSize = 320,
                    MinSize = 240,
                    MaxSize = 480,
                    SplitterSize = 7,
                    InitialDocked = initialDocked
                });

        private sealed class TestDockControl : UserControl
        {
        }

        private sealed class TestDockWindow : Window
        {
        }
    }
}
