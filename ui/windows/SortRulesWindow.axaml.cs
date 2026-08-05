using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using Avalonia.Controls;
using ModHearth.Utilities;
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

    private static double LoadDimension(string attributeName, double fallback)
    {
        try
        {
            string path = Path.Combine("ui", "windows", "SortRulesWindow.axaml");
            if (!File.Exists(path))
            {
                path = Path.Combine(AppContext.BaseDirectory, "ui", "windows", "SortRulesWindow.axaml");
            }
            if (File.Exists(path))
            {
                string content = File.ReadAllText(path);
                var match = Regex.Match(content, $@"{attributeName}\s*=\s*""(?<val>[^""]+)""", RegexOptions.IgnoreCase);
                if (match.Success && double.TryParse(match.Groups["val"].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double val))
                {
                    return val;
                }
            }
        }
        catch
        {
            // Fallback
        }
        return fallback;
    }

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
