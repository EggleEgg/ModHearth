using Avalonia.Media;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ModHearth.UI;

public sealed class ModDataEntryViewModel : INotifyPropertyChanged
{
    private IBrush background = Brushes.Transparent;

    public ModDataEntryViewModel(string label, string value)
    {
        Label = label;
        Value = value;
    }

    public string Label { get; }
    public string Value { get; }

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