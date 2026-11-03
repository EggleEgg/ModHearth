using Avalonia.Controls;
using ModHearth.Models;
using ModHearth.UI.Theme;

namespace ModHearth.UI
{
    public partial class SortRulesWindow : Window, IDisposable
    {
        public static double DefaultWidth => 980;
        public static double DefaultMinWidth => 640;
        public static double DefaultMaxWidth => 1100;
        public static double DefaultHeight => 760;
        public static double DefaultMinHeight => 350;
        public static double DefaultMaxHeight => 1100;

        public SortRulesWindow()
            : this(new Dictionary<string, ModRelationshipRule>(), [], string.Empty, null)
        {
        }

        public SortRulesWindow(
            IReadOnlyDictionary<string, ModRelationshipRule> existingRules,
            IEnumerable<ModReference> modRefs,
            string? rulesFilePath,
            Action<Dictionary<string, ModRelationshipRule>>? onRulesChanged = null,
            SortRulesControl? control = null)
        {
            InitializeComponent();
            WindowThemeManager.Register(this);
            Content = control ?? new SortRulesControl(existingRules, modRefs, rulesFilePath, onRulesChanged);

            // old docking/undocking cycles need to be cleared to avoid memory leaks
            if (Content is SortRulesControl rc)
            {
                void closeRequestedHandler(object? _1, EventArgs _2)
                {
                    Close();
                }

                rc.CloseRequested += closeRequestedHandler;
                Closed += (_, _) => rc.CloseRequested -= closeRequestedHandler;
            }
            Closed += (_, _) =>
            {
                if (Content is SortRulesControl rc2)
                    rc2.SaveSplitterRatio();
            };
        }



        private bool _disposed;

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
                return;

            if (disposing && Content is SortRulesControl control)
            {
                control.Dispose();

            }

            _disposed = true;
        }
    }
}
