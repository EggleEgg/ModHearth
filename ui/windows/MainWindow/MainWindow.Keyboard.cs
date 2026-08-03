using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace ModHearth.UI;

/// Ideally merge as many of these as possible with <see cref=ShortcutKeyHandler
public partial class MainWindow
{
    private async void MainWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled || e.KeyModifiers != KeyModifiers.None)
            return;

        if (e.Key == Key.Escape)
        {
            if (HandleEscapeKey(e.Source))
                e.Handled = true;
            return;
        }

        if (e.Key != Key.Delete || !CanHandleDeleteKeyFromSource(e.Source))
            return;

        List<ModRefViewModel> selection = GetSelectedModsForDeletion();
        if (selection.Count == 0)
            return;

        e.Handled = true;
        await DeleteSelectedModsAsync(selection.Select(vm => vm.ModReference).ToList());
    }

    private bool HandleEscapeKey(object? source)
    {
        bool handled = false;
        if ((leftModlist.SelectedItems?.Count ?? 0) > 0 || (rightModlist.SelectedItems?.Count ?? 0) > 0)
        {
            ShowFallbackInfo();
            handled = true;
        }

        if (leftSearchBar.ClearSearchSelection())
            handled = true;
        if (rightSearchBar.ClearSearchSelection())
            handled = true;

        if (source is Control control && control.FindAncestorOfType<ModSearchBar>() != null)
        {
            _ = Focus();
            handled = true;
        }

        return handled;
    }

    private static bool CanHandleDeleteKeyFromSource(object? source)
    {
        if (source is not Control control)
            return true;

        return control.FindAncestorOfType<TextBox>() == null &&
               control.FindAncestorOfType<ComboBox>() == null;
    }

    private List<ModRefViewModel> GetSelectedModsForDeletion()
    {
        if (rightModlist.SelectedItems != null && rightModlist.SelectedItems.Count > 0)
            return rightModlist.SelectedItems.OfType<ModRefViewModel>().ToList();

        if (leftModlist.SelectedItems != null && leftModlist.SelectedItems.Count > 0)
            return leftModlist.SelectedItems.OfType<ModRefViewModel>().ToList();

        return new List<ModRefViewModel>();
    }
}
