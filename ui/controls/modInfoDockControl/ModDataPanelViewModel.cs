using System.Collections.ObjectModel;
using Dock.Model.Mvvm.Controls;

namespace ModHearth.UI;

public sealed class ModDataPanelViewModel : Tool
{
    public ObservableCollection<ModDataEntryViewModel> Entries { get; } = [];

    private bool hasSelection;
    public bool HasSelection
    {
        get => hasSelection;
        set
        {
            if (hasSelection == value)
                return;
            hasSelection = value;
            OnPropertyChanged();
        }
    }
}