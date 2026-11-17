using Avalonia.Controls;
using ModHearth.Models;
using ModHearth.UI.Theme;

namespace ModHearth.UI
{
    public partial class SortRulesWindow : Window, IDisposable
    {
        private readonly SortRulesControl rulesControl;

        public static double DefaultWidth => 980;
        public static double DefaultMinWidth => 640;
        public static double DefaultMaxWidth => 1100;
        public static double DefaultHeight => 760;
        public static double DefaultMinHeight => 350;
        public static double DefaultMaxHeight => 1100;

        public SortRulesWindow()
            : this(new Dictionary<string, ModRelationshipRule>(), [], null)
        {
        }

        public SortRulesWindow(
            IReadOnlyDictionary<string, ModRelationshipRule> existingRules,
            IEnumerable<ModReference> modRefs,
            Action<Dictionary<string, ModRelationshipRule>>? onRulesChanged = null,
            SortRulesControl? control = null)
        {
            InitializeComponent();
            WindowThemeManager.Register(this);
            rulesControl = control ?? new SortRulesControl(existingRules, modRefs, onRulesChanged);
            Content = rulesControl;

            // old docking/undocking cycles need to be cleared to avoid memory leaks
            void closeRequestedHandler(object? _1, EventArgs _2)
            {
                Close();
            }

            rulesControl.CloseRequested += closeRequestedHandler;
            Closed += (_, _) =>
            {
                rulesControl.CloseRequested -= closeRequestedHandler;
                rulesControl.SaveSplitterRatio();
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

            if (disposing)
                rulesControl.Dispose();

            _disposed = true;
        }
    }
}
