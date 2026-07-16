using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Dock.Model.Mvvm.Controls;

namespace ModHearth.UI;

public sealed class ModDataPanelViewModel : Tool
{
    public ObservableCollection<ModDataEntryViewModel> Entries { get; } = new();

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