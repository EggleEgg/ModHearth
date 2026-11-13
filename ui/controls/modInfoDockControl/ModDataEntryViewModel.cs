using Avalonia.Media;
using ModHearth.Utils;

namespace ModHearth.UI.Controls.ModInfoDockControl
{
    public sealed class ModDataEntryViewModel(string label, string value) : ObservableObject
    {
        public string Label { get; } = label;
        public string Value { get; } = value;

        public IBrush Background
        {
            get;
            set => SetField(ref field, value);
        } = Brushes.Transparent;
    }
}