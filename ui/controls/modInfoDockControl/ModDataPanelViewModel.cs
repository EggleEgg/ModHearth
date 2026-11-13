using System.Collections.ObjectModel;
using Dock.Model.Mvvm.Controls;

namespace ModHearth.UI.Controls.ModInfoDockControl
{
    public sealed class ModDataPanelViewModel : Tool
    {
        public ObservableCollection<ModDataEntryViewModel> Entries { get; } = [];

        public bool HasSelection
        {
            get;
            set
            {
                if (field == value)
                    return;
                field = value;
                OnPropertyChanged();
            }
        }
    }
}