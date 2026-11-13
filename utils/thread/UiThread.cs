using Avalonia.Threading;

namespace ModHearth.Utils.Thread
{
    /// <summary>
    /// Runs UI work on the Avalonia dispatcher. When the caller is already on the UI thread the
    /// action runs inline, so call sites that need an immediate refresh keep that behaviour;
    /// otherwise it is posted to the UI thread.
    /// </summary>
    public static class UiThread
    {
        public static void Run(Action action)
        {
            ArgumentNullException.ThrowIfNull(action);

            if (Dispatcher.UIThread.CheckAccess())
            {
                action();
                return;
            }

            Dispatcher.UIThread.Post(action);
        }
    }
}
