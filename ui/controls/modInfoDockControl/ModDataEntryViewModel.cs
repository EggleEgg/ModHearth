using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Media;

namespace ModHearth.UI;

public sealed class ModDataEntryViewModel(string label, string value) : INotifyPropertyChanged
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

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}