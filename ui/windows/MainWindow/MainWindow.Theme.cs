namespace ModHearth.UI;

public partial class MainWindow
{
    public void ApplyCustomStyle(Style style)
    {
        if (style == null)
            return;

        RefreshDescriptionHtml();
        RefreshModDataViewer();

        int theme = ConfigManager.GetTheme();
        if (themeComboBox != null && themeComboBox.SelectedIndex != theme)
            themeComboBox.SelectedIndex = theme;
    }

    private void ApplyStyle(Style style)
    {
        if (style == null)
            return;

        ApplyCustomStyle(style);

        // Use the centralized theme manager to apply styles recursively to the entire visual tree, 
        // including window background, theme variant, application resources, labels, listboxes, notification container, etc.
        WindowThemeManager.ApplyToOpenWindows(style);
    }

    private async Task OnThemeChangedAsync()
    {
        if (themeComboBox.SelectedIndex < 0)
            return;

        ConfigManager.SetTheme(themeComboBox.SelectedIndex);
        try
        {
            ApplyStyle(ConfigManager.LoadStyle());
        }
        catch (Exception ex)
        {
            await DialogService.ShowMessageAsync(this, ex.Message, "Style load failed");
            Close();
            return;
        }
        UpdateProblemIndicators();
        UpdateDuplicateWarningIndicators();
    }
}
