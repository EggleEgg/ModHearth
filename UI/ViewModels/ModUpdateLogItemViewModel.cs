using Avalonia.Media;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ModHearth.UI;

public sealed class ModUpdateLogItemViewModel : INotifyPropertyChanged, ISelectableItem
{
    private readonly IBrush defaultBackgroundBrush;
    private readonly IBrush selectedBackgroundBrush;
    private bool isSelected;
    private IBrush backgroundBrush;

    public ModUpdateLogItemViewModel(
        ModUpdateLogEntry entry,
        ModReference modref,
        IBrush rowBrush,
        IBrush selectedBackgroundBrush,
        bool isActive)
    {
        Entry = entry ?? throw new ArgumentNullException(nameof(entry));
        ModReference = modref ?? throw new ArgumentNullException(nameof(modref));
        RowBrush = rowBrush ?? throw new ArgumentNullException(nameof(rowBrush));
        this.selectedBackgroundBrush = selectedBackgroundBrush ?? Brushes.Transparent;
        defaultBackgroundBrush = Brushes.Transparent;
        backgroundBrush = defaultBackgroundBrush;

        DateText = entry.TimestampUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
        ModName = string.IsNullOrWhiteSpace(entry.ModName) ? entry.ModId : entry.ModName;
        SourceType = entry.SourceType;
        StateText = entry.ChangeType.ToString();
        IsActive = isActive;
        ActiveText = isActive ? "Yes" : "No";
        Path = entry.Path;
    }

    public ModUpdateLogEntry Entry { get; }
    public ModReference ModReference { get; }
    public IBrush RowBrush { get; }
    public IBrush BackgroundBrush
    {
        get => backgroundBrush;
        private set
        {
            if (Equals(backgroundBrush, value))
                return;
            backgroundBrush = value;
            OnPropertyChanged();
        }
    }
    public string DateText { get; }
    public string ModName { get; }
    public string SourceType { get; }
    public string StateText { get; }
    public bool IsActive { get; }
    public string ActiveText { get; }
    public string Path { get; }

    public bool IsSelected
    {
        get => isSelected;
        set
        {
            if (isSelected == value)
                return;
            isSelected = value;
            BackgroundBrush = isSelected ? selectedBackgroundBrush : defaultBackgroundBrush;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
