using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace ModHearth.UI;

/// Ideally merge as many of these as possible with <see cref=ShortcutKeyHandler
public partial class MainWindow
{
    private async void MainWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled)
            return;

        if (e.Key == Key.Tab && (e.KeyModifiers == KeyModifiers.None || e.KeyModifiers == KeyModifiers.Shift))
        {
            if (HandleTabKey())
                e.Handled = true;
            return;
        }

        if (e.KeyModifiers != KeyModifiers.None)
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

    private bool HandleTabKey()
    {
        ListBox sourceList;
        ListBox targetList;
        System.Collections.ObjectModel.ObservableCollection<ModRefViewModel> sourceCol;
        System.Collections.ObjectModel.ObservableCollection<ModRefViewModel> targetCol;

        bool rightActive = rightModlist.IsKeyboardFocusWithin ||
                           (rightModlist.SelectedItems?.Count > 0 && leftModlist.SelectedItems?.Count == 0) ||
                           rightModlist.SelectedIndex >= 0 && leftModlist.SelectedIndex < 0;

        if (rightActive)
        {
            sourceList = rightModlist;
            targetList = leftModlist;
            sourceCol = activeMods;
            targetCol = inactiveMods;
        }
        else
        {
            sourceList = leftModlist;
            targetList = rightModlist;
            sourceCol = inactiveMods;
            targetCol = activeMods;
        }

        List<ModRefViewModel> sourceVisible = [.. sourceCol.Where(vm => vm.IsVisible)];
        List<ModRefViewModel> targetVisible = [.. targetCol.Where(vm => vm.IsVisible)];

        if (sourceVisible.Count == 0 && targetVisible.Count == 0)
            return false;

        ModRefViewModel? selectedVm = sourceList.SelectedItem as ModRefViewModel
            ?? sourceList.SelectedItems?.Cast<ModRefViewModel>().FirstOrDefault();

        int visualRowIndex = selectedVm != null ? sourceVisible.IndexOf(selectedVm) : -1;
        if (visualRowIndex < 0)
            visualRowIndex = Math.Clamp(sourceList.SelectedIndex, 0, sourceVisible.Count - 1);
        if (visualRowIndex < 0)
            visualRowIndex = 0;

        if (targetVisible.Count == 0)
            return false;

        visualRowIndex = Math.Clamp(visualRowIndex, 0, targetVisible.Count - 1);
        ModRefViewModel targetVm = targetVisible[visualRowIndex];

        leftModlist.SelectedItems?.Clear();
        rightModlist.SelectedItems?.Clear();
        _ = targetList.SelectedItems?.Add(targetVm);
        modListController.UpdateSelectionState(leftModlist);
        modListController.UpdateSelectionState(rightModlist);
        targetList.ScrollIntoView(targetVm);
        TrackSelectedMod(targetVm);
        ShowModInfo(targetVm.ModReference);
        _ = targetList.Focus();

        return true;
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
            return [.. rightModlist.SelectedItems.OfType<ModRefViewModel>()];

        if (leftModlist.SelectedItems != null && leftModlist.SelectedItems.Count > 0)
            return [.. leftModlist.SelectedItems.OfType<ModRefViewModel>()];

        return [];
    }
}
