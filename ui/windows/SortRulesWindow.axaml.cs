using System.Text.RegularExpressions;
using Avalonia.Controls;
using System.Globalization;

namespace ModHearth.UI;

public partial class SortRulesWindow : Window, IDisposable
{
    public static double DefaultWidth => LoadDimension("Width", 760);
    public static double DefaultMinWidth => LoadDimension("MinWidth", 480);
    public static double DefaultMaxWidth => LoadDimension("MaxWidth", 1100);
    public static double DefaultHeight => LoadDimension("Height", 760);
    public static double DefaultMinHeight => LoadDimension("MinHeight", 350);
    public static double DefaultMaxHeight => LoadDimension("MaxHeight", 1100);

    private static double LoadDimension(string attributeName, double fallback) =>
        WindowDimensionLoader.Load("SortRulesWindow.axaml", attributeName, fallback);

    public SortRulesWindow()
        : this(new Dictionary<string, ModRelationshipRule>(), Array.Empty<ModReference>(), string.Empty, null)
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
            EventHandler closeRequestedHandler = (_, _) => Close();
            rc.CloseRequested += closeRequestedHandler;
            Closed += (_, _) => rc.CloseRequested -= closeRequestedHandler;
        }
        Closed += (_, _) =>
        {
            if (Content is SortRulesControl rc2)
                rc2.SaveSplitterRatio();
        };
    }



    public void Dispose()
    {
        if (Content is SortRulesControl control)
        {
            control.Dispose();
        }
    }
}
