using Avalonia.Media;
using ModHearth.Utils;

namespace ModHearth.Models
{
    /// <summary>
    /// Represents a color option in the mod color picker
    /// </summary>
    public class ModColorInfo : ObservableObject
    {
        public ModColor ModColor
        {
            get;
            set => SetField(ref field, value);
        }

        public Color Color
        {
            get;
            set => SetField(ref field, value);
        }

        public string Name
        {
            get;
            set => SetField(ref field, value);
        } = string.Empty;

        public bool IsSelected
        {
            get;
            set => SetField(ref field, value);
        }
    }
}
