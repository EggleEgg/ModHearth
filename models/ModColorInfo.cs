using Avalonia.Media;
using ModHearth.Utilities;

namespace ModHearth.Models
{
    /// <summary>
    /// Represents a color option in the mod color picker
    /// </summary>
    public class ModColorInfo : ObservableObject
    {
        private ModColor _modColor;
        private Color _color;
        private string _name = string.Empty;
        private bool _isSelected;

        public ModColor ModColor
        {
            get => _modColor;
            set
            {
                if (_modColor != value)
                {
                    _modColor = value;
                    OnPropertyChanged();
                }
            }
        }

        public Color Color
        {
            get => _color;
            set
            {
                if (_color != value)
                {
                    _color = value;
                    OnPropertyChanged();
                }
            }
        }

        public string Name
        {
            get => _name;
            set
            {
                if (_name != value)
                {
                    _name = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged();
                }
            }
        }
    }
}
