namespace ModHearth.UI.Theme
{
    public interface IThemedViewModel
    {
        void RefreshStyle(Style style);
    }

    internal static class ThemedViewModelRegistry
    {
        private static readonly List<WeakReference<IThemedViewModel>> instances = [];
        private static readonly object gate = new();
        private static int registerCounter;

        public static void Register(IThemedViewModel vm)
        {
            lock (gate)
            {
                if (++registerCounter >= 64)
                {
                    registerCounter = 0;
                    _ = instances.RemoveAll(w => !w.TryGetTarget(out _));
                }
                instances.Add(new WeakReference<IThemedViewModel>(vm));
            }
        }

        public static void RefreshAll(Style style)
        {
            List<IThemedViewModel> targets;
            lock (gate)
            {
                registerCounter = 0;
                _ = instances.RemoveAll(w => !w.TryGetTarget(out _));
                targets = [.. instances.Select(w => { _ = w.TryGetTarget(out IThemedViewModel? t); return t!; })];
            }
            foreach (IThemedViewModel vm in targets)
                vm.RefreshStyle(style);
        }
    }
}
