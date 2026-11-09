using System.Globalization;
using Avalonia.Media;
using ModHearth.Models;
using ModHearth.UI.Contracts;
using ModHearth.UI.Helpers;
using ModHearth.UI.Theme;
using ModHearth.Utilities;
using ModHearth.Utilities.Logging;

namespace ModHearth.UI.ViewModels
{
    public sealed class ModUpdateLogItemViewModel : ObservableObject, ISelectableItem, IThemedViewModel
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

            DateText = entry.TimestampUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
            ModName = string.IsNullOrWhiteSpace(entry.ModName) ? entry.ModId : entry.ModName;
            SourceType = entry.SourceType;
            StateText = entry.ChangeType.ToString();
            IsActive = isActive;
            ActiveText = isActive ? "Yes" : "No";
            Path = entry.Path;

            ThemedViewModelRegistry.Register(this);
        }

        public ModUpdateLogEntry Entry { get; }
        public ModReference ModReference { get; }
        public IBrush RowBrush
        {
            get => rowBrush;
            private set => SetField(ref rowBrush, value);
        }
        public IBrush BackgroundBrush
        {
            get => backgroundBrush;
            private set => SetField(ref backgroundBrush, value);
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
                if (!SetField(ref isSelected, value))
                    return;
                BackgroundBrush = isSelected ? selectedBackgroundBrush : defaultBackgroundBrush;
            }
        }

        public bool IsFilteredOut
        {
            get => isFilteredOut;
            set
            {
                if (!SetField(ref isFilteredOut, value))
                    return;
                if (Style.instance != null)
                    RefreshStyle(Style.instance);
            }
        }

        public bool IsVisible
        {
            get => isVisible;
            set => SetField(ref isVisible, value);
        }

        public TextDecorationCollection? TextDecorations
        {
            get => textDecorations;
            private set => SetField(ref textDecorations, value);
        }

        private string? searchTargetCache;

        /// <summary>
        /// Combined text used by regex searches. The view model is immutable, so this is built once.
        /// </summary>
        private string SearchTarget => searchTargetCache ??=
            $"{ModName} {Entry.ModId} {Entry.SteamId} {Entry.Path} {SourceType} {StateText} {DateText}";

        public bool MatchesFilter(SearchFilterContext context)
        {
            if (context == null || !context.HasFilter)
                return true;

            switch (context.Mode)
            {
                case SearchFilterMode.Regex:
                    return context.MatchesRegex(SearchTarget);

                case SearchFilterMode.Color:
                    return true;
            }

            string? candidate = context.Mode switch
            {
                SearchFilterMode.Id => Entry.ModId,
                SearchFilterMode.SteamFileId => Entry.SteamId,
                _ => ModName,
            };

            return context.Contains(candidate) ||
                   context.Contains(Path) ||
                   context.Contains(StateText);
        }

        public void RefreshStyle(Style style)
        {
            selectedBackgroundBrush = style != null
                ? BrushCache.GetBrush(style.ModRefHighlightColor.ToAvaloniaColor())
                : Brushes.Transparent;

            IBrush defaultBrush = style != null
                ? BrushCache.GetBrush(style.TextColor.ToAvaloniaColor())
                : Brushes.White;

            RowBrush = IsFilteredOut && style != null
                ? BrushCache.GetBrush(style.ModRefTextFilteredColor.ToAvaloniaColor())
                : ModUpdateLogControl.GetRowBrush(Entry, defaultBrush, IsActive);

            TextDecorationCollection? targetDecoration = IsFilteredOut ? Avalonia.Media.TextDecorations.Strikethrough : null;
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
    }
}
