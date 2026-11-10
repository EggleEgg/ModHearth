using System.Collections.Concurrent;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace ModHearth.App.Tests
{
    internal sealed record UiActionRoot(Type ViewType, Func<Control> Factory);

    internal sealed record UiActionFailure(UiActionKey Key, string Detail, Exception Exception)
    {
        public override string ToString() =>
            $"{Key} ({Detail}): {Exception.GetType().Name}: {Exception.Message}";
    }

    internal sealed record UiActionRunResult(
        IReadOnlyList<UiActionKey> Invoked,
        IReadOnlyList<UiActionKey> Skipped,
        IReadOnlyList<UiActionKey> Unclassified,
        IReadOnlyList<UiActionFailure> Failures,
        int WindowsShown);

    /// <summary>
    /// Executes the classified UI actions and reports both synchronous exceptions (thrown by the
    /// handler) and asynchronous ones (captured from the dispatcher, e.g. from an <c>async void</c>
    /// click handler).
    /// </summary>
    /// <remarks>
    /// Windows are shown before their actions run so <c>Opened</c>/<c>Loaded</c> handlers, templates,
    /// and layout execute as they do in the real application. The runner awaits instead of sleeping
    /// so the Avalonia dispatcher loop keeps processing timers, bindings, and continuations.
    /// </remarks>
    internal static class UiActionRunner
    {
        private static readonly TimeSpan PumpInterval = TimeSpan.FromMilliseconds(60);
        private static readonly TimeSpan WindowShowSettleDelay = TimeSpan.FromMilliseconds(50);
        private static readonly TimeSpan BackgroundSettleDelay = TimeSpan.FromSeconds(2);

        private static readonly UiActionKey BackgroundKey = new("Background", "Background", "Background");

        public static async Task<UiActionRunResult> RunAsync(
            IReadOnlyList<UiActionRoot> roots,
            CancellationToken cancellationToken)
        {
            List<UiActionKey> invoked = [];
            List<UiActionKey> skipped = [];
            List<UiActionKey> unclassified = [];
            List<UiActionFailure> failures = [];
            int windowsShown = 0;

            // Dispatcher exceptions surface on the UI thread, but unobserved task exceptions can be
            // reported from finalizer threads, so the queues have to be thread safe.
            ConcurrentQueue<Exception> pendingDispatcherExceptions = new();
            ConcurrentQueue<Exception> pendingTaskExceptions = new();

            void OnDispatcherException(object? sender, DispatcherUnhandledExceptionEventArgs args)
            {
                pendingDispatcherExceptions.Enqueue(args.Exception);
                args.Handled = true;
            }

            void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs args)
            {
                pendingTaskExceptions.Enqueue(args.Exception);
                args.SetObserved();
            }

            Dispatcher.UIThread.UnhandledException += OnDispatcherException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
            try
            {
                foreach (UiActionRoot root in roots)
                {
                    Control instance = root.Factory();
                    try
                    {
                        if (await ShowWindowAsync(
                                root,
                                instance,
                                failures,
                                pendingDispatcherExceptions,
                                pendingTaskExceptions,
                                cancellationToken))
                        {
                            windowsShown++;
                        }

                        foreach (IGrouping<UiActionKey, DiscoveredUiAction> keyGroup
                                 in UiActionDiscovery.Discover(instance).GroupBy(action => action.Key))
                        {
                            // The caller validates the discovered inventory first. Re-check here
                            // because a fresh instance could realize additional templates.
                            UiActionPlan? plan = UiActionPolicy.Classify(keyGroup.Key);
                            if (plan == null)
                            {
                                unclassified.Add(keyGroup.Key);
                                continue;
                            }

                            if (plan.Mode == UiActionMode.Skip)
                            {
                                skipped.Add(keyGroup.Key);
                                continue;
                            }

                            // Raising the routed event once per (control, event) runs every
                            // subscription with that key, including several handlers on one button.
                            foreach (IGrouping<(Interactive Source, RoutedEvent Event), DiscoveredUiAction> controlGroup
                                     in keyGroup.GroupBy(action => (action.Source, action.RoutedEvent)).OfType<IGrouping<(Interactive Source, RoutedEvent Event), DiscoveredUiAction>>())
                            {
                                Raise(keyGroup.Key, controlGroup.Key.Source, controlGroup.Key.Event, failures);
                                await Task.Delay(PumpInterval, cancellationToken);
                                FlushPending(keyGroup.Key, pendingDispatcherExceptions, pendingTaskExceptions, failures);
                            }

                            invoked.Add(keyGroup.Key);
                        }
                    }
                    finally
                    {
                        DisposeRoot(instance);
                    }
                }

                // Give delayed async continuations a chance to surface before the final flush.
                await Task.Delay(BackgroundSettleDelay, cancellationToken);
                FlushPending(BackgroundKey, pendingDispatcherExceptions, pendingTaskExceptions, failures);
            }
            finally
            {
                Dispatcher.UIThread.UnhandledException -= OnDispatcherException;
                TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;
            }

            return new UiActionRunResult(invoked, skipped, unclassified, failures, windowsShown);
        }

        private static async Task<bool> ShowWindowAsync(
            UiActionRoot root,
            Control instance,
            List<UiActionFailure> failures,
            ConcurrentQueue<Exception> dispatcherExceptions,
            ConcurrentQueue<Exception> taskExceptions,
            CancellationToken cancellationToken)
        {
            if (instance is not Window window)
                return false;

            UiActionKey key = new(root.ViewType.Name, "(window)", "Show");
            try
            {
                window.Show();
            }
            catch (Exception ex)
            {
                failures.Add(new UiActionFailure(key, "Synchronous exception from Window.Show().", ex));
                return false;
            }

            await Task.Delay(WindowShowSettleDelay, cancellationToken);
            FlushPending(key, dispatcherExceptions, taskExceptions, failures);
            return true;
        }

        private static void Raise(
            UiActionKey key,
            Interactive source,
            RoutedEvent routedEvent,
            List<UiActionFailure> failures)
        {
            if (routedEvent != Button.ClickEvent || source is not Button button)
            {
                failures.Add(new UiActionFailure(
                    key,
                    "Policy asked to invoke an unsupported event shape.",
                    new NotSupportedException(
                        $"Only {nameof(Button)}.{nameof(Button.ClickEvent)} can be raised, " +
                        $"but the source is {source.GetType().Name} and the event is {routedEvent.Name}.")));
                return;
            }

            try
            {
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, button));
            }
            catch (Exception ex)
            {
                failures.Add(new UiActionFailure(key, "Synchronous handler exception.", ex));
            }
        }

        private static void FlushPending(
            UiActionKey actionKey,
            ConcurrentQueue<Exception> dispatcherExceptions,
            ConcurrentQueue<Exception> taskExceptions,
            List<UiActionFailure> failures)
        {
            while (dispatcherExceptions.TryDequeue(out Exception? ex))
                failures.Add(new UiActionFailure(actionKey, "Unhandled exception on the UI dispatcher.", ex));

            while (taskExceptions.TryDequeue(out Exception? ex))
                failures.Add(new UiActionFailure(actionKey, "Unobserved task exception.", ex));
        }

        internal static void DisposeRoot(Control root)
        {
            try
            {
                if (root is Window window)
                    window.Close();
            }
            catch (Exception)
            {
                // Closing a never-shown window is best-effort cleanup; the assertion result matters.
            }

            if (root is IDisposable disposable)
            {
                try
                {
                    disposable.Dispose();
                }
                catch (Exception)
                {
                    // Disposal failures are independent of whether the UI action crashed.
                }
            }
        }
    }
}
