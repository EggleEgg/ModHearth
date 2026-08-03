using Avalonia.Media;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ModHearth.UI;

public sealed class ModUpdateLogItemViewModel : INotifyPropertyChanged, ISelectableItem
{
    private readonly IBrush defaultBackgroundBrush = Brushes.Transparent;
    private IBrush selectedBackgroundBrush;
    private bool isSelected;
    private IBrush backgroundBrush;
    private IBrush rowBrush;
    private bool isFilteredOut;
    private bool isVisible = true;
    private TextDecorationCollection? textDecorations;

    public ModUpdateLogItemViewModel(
        ModUpdateLogEntry entry,
        ModReference modref,
        IBrush rowBrush,
        IBrush selectedBackgroundBrush,
        bool isActive)
    {
        Entry = entry ?? throw new ArgumentNullException(nameof(entry));
        ModReference = modref ?? throw new ArgumentNullException(nameof(modref));
        this.rowBrush = rowBrush ?? throw new ArgumentNullException(nameof(rowBrush));
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
    public IBrush RowBrush
    {
        get => rowBrush;
        private set
        {
            if (Equals(rowBrush, value))
                return;
            rowBrush = value;
            OnPropertyChanged();
        }
    }
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

    // Lets the DataGrid's State column sort by importance (Updated > Added > Deleted) instead of alphabetically, via SortMemberPath in the XAML.
    public int StateSortRank => Entry.ChangeType switch
    {
        ModUpdateChangeType.Updated => 2,
        ModUpdateChangeType.Added => 1,
        ModUpdateChangeType.Deleted => 0,
        _ => 0
    };
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

    public bool IsFilteredOut
    {
        get => isFilteredOut;
        set
        {
            if (isFilteredOut == value)
                return;
            isFilteredOut = value;
            if (Style.instance != null)
                RefreshStyle(Style.instance);
            OnPropertyChanged();
        }
    }

    public bool IsVisible
    {
        get => isVisible;
        set
        {
            if (isVisible == value)
                return;
            isVisible = value;
            OnPropertyChanged();
        }
    }

    public TextDecorationCollection? TextDecorations
    {
        get => textDecorations;
        private set
        {
            if (Equals(textDecorations, value))
                return;
            textDecorations = value;
            OnPropertyChanged();
        }
    }

    public bool MatchesFilter(string filter, SearchFilterMode mode)
    {
        if (string.IsNullOrWhiteSpace(filter))
            return true;

        switch (mode)
        {
            case SearchFilterMode.Regex:
                {
                    try
                    {
                        string fullTarget = $"{ModName} {Entry.ModId} {Entry.SteamId} {Entry.Path} {SourceType} {StateText} {DateText}";
                        return System.Text.RegularExpressions.Regex.IsMatch(fullTarget, filter, System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
                    }
                    catch
                    {
                        return false;
                    }
                }

            case SearchFilterMode.Color:
                return true;

        }

        string? candidate = mode switch
        {
            SearchFilterMode.Name => ModName,
            SearchFilterMode.Id => Entry.ModId,
            SearchFilterMode.SteamFileId => Entry.SteamId,
            _ => ModName
        };

        return (!string.IsNullOrWhiteSpace(candidate) && candidate.Contains(filter, StringComparison.OrdinalIgnoreCase)) ||
               (!string.IsNullOrWhiteSpace(Path) && Path.Contains(filter, StringComparison.OrdinalIgnoreCase)) ||
               (!string.IsNullOrWhiteSpace(StateText) && StateText.Contains(filter, StringComparison.OrdinalIgnoreCase));
    }

    public void RefreshStyle(Style style)
    {
        selectedBackgroundBrush = style != null
            ? BrushCache.GetBrush(style.modRefHighlightColor.ToAvaloniaColor())
            : Brushes.Transparent;

        IBrush defaultBrush = style != null
            ? BrushCache.GetBrush(style.textColor.ToAvaloniaColor())
            : Brushes.White;

        if (IsFilteredOut && style != null)
        {
            RowBrush = BrushCache.GetBrush(style.modRefTextFilteredColor.ToAvaloniaColor());
        }
        else
        {
            RowBrush = ModUpdateLogControl.GetRowBrush(Entry, defaultBrush, IsActive);
        }

        var targetDecoration = IsFilteredOut ? Avalonia.Media.TextDecorations.Strikethrough : null;
        if (TextDecorations != targetDecoration)
        {
            TextDecorations = targetDecoration;
        }

        if (IsSelected)
        {
            backgroundBrush = selectedBackgroundBrush;
            OnPropertyChanged(nameof(BackgroundBrush));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
