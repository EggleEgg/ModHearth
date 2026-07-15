using Avalonia.Media;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ModHearth.Models;

public class ModColorInfo : INotifyPropertyChanged
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
                OnPropertyChanged(nameof(Brush));
            }
        }
    }

    public IBrush Brush => new SolidColorBrush(Color);

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

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}