using Avalonia.Media;
using ModHearth.Utilities;

namespace ModHearth.UI.Controls.ModInfoDockControl
{
    public sealed class ModDataEntryViewModel(string label, string value) : ObservableObject
    {
        private IBrush background = Brushes.Transparent;

        public string Label { get; } = label;
        public string Value { get; } = value;

        public IBrush Background
        {
            get => background;
            set
            {
                if (Equals(background, value))
                    return;
                background = value;
                OnPropertyChanged();
            }
        }
    }
}