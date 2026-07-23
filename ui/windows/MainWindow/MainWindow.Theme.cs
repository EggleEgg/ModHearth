using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;

namespace ModHearth.UI;

public partial class MainWindow
{
    private void ApplyStyle(Style style)
    {
        if (style == null)
            return;

        Style.instance = style;
        IBrush formBrush = BrushCache.GetBrush(style.formColor.ToAvaloniaColor());
        IBrush textBrush = BrushCache.GetBrush(style.textColor.ToAvaloniaColor());
        IBrush panelBrush = BrushCache.GetBrush(style.modRefPanelColor.ToAvaloniaColor());
        IBrush panelBrushClear = BrushCache.GetBrush(style.modRefPanelColorClear.ToAvaloniaColor());
        IBrush buttonBrush = BrushCache.GetBrush(style.buttonColor.ToAvaloniaColor());
        IBrush buttonTextBrush = BrushCache.GetBrush(style.buttonTextColor.ToAvaloniaColor());
        IBrush buttonOutlineBrush = BrushCache.GetBrush(style.buttonOutlineColor.ToAvaloniaColor());

        Background = formBrush;
        leftHeaderLabel.Foreground = textBrush;
        rightHeaderLabel.Foreground = textBrush;
        modTitleLabel.Foreground = textBrush;
        buildVersionLabel.Foreground = textBrush;

        //Avalonia DynamicResource
        Application? app = Application.Current;
        if (app != null)
        {
            app.Resources["SeparatorBrush"] = BrushCache.GetBrush(style.backgroundColor.ToAvaloniaColor());
        }

        RefreshDescriptionHtml();
        RefreshModDataViewer();

        var notificationContainer = this.FindControl<StackPanel>("notificationContainer");
        if (notificationContainer != null)
        {
            foreach (var child in notificationContainer.Children)
            {
                if (child is Border b)
                {
                    b.Background = panelBrushClear;
                    b.BorderBrush = buttonOutlineBrush;
                    if (b.Child is StackPanel sp)
                    {
                        foreach (var innerChild in sp.Children)
                        {
                            if (innerChild is TextBlock tb)
                                tb.Foreground = textBrush;
                        }
                    }
                }
            }
        }

        dfhackStatusLabel.Foreground = textBrush;

        leftModlist.Background = panelBrush;
        rightModlist.Background = panelBrush;

        bool isDarkTheme = ConfigManager.GetTheme() == 1;
        IBrush inputTextBrush = isDarkTheme ? Brushes.White : Brushes.Black;

        leftSearchBar.ApplyStyle(style);
        rightSearchBar.ApplyStyle(style);

        ComboBox[] comboBoxes = { modpackComboBox, themeComboBox };
        foreach (ComboBox comboBox in comboBoxes)
        {
            comboBox.Background = panelBrush;
            comboBox.Foreground = inputTextBrush;
        }

        Button[] buttons =
        {
            saveButton,
            undoChangesButton,
            clearInstalledModsButton,
            clearModlistButton,
            reloadButton,
            newListButton,
            renameListButton,
            deleteListButton,
            importButton,
            exportButton,
            sortButton,
            sortRulesButton,
            updateLogButton,
            redoConfigButton,
            warningIssuesButton,
            updateButton,
            runDwarfFortressButton,
        };

        foreach (Button button in buttons)
        {
            if (button == null)
                continue;

            button.Background = buttonBrush;
            button.Foreground = buttonTextBrush;
            button.BorderBrush = buttonOutlineBrush;
            button.BorderThickness = new Thickness(1);
        }

        foreach (ModRefViewModel vm in modViewMap.Values)
            vm.RefreshStyle();

        int theme = ConfigManager.GetTheme();
        if (themeComboBox != null && themeComboBox.SelectedIndex != theme)
            themeComboBox.SelectedIndex = theme;

        RequestedThemeVariant = theme == 0 ? ThemeVariant.Light : ThemeVariant.Dark;
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
