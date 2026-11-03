using System.Text;
using Avalonia.Media;
using ModHearth.Models;
using ModHearth.UI.Helpers;
using ModHearth.UI.Rendering;
using ModHearth.UI.Theme;
using ModHearth.Utilities;
using ModHearth.Utilities.Logging;

namespace ModHearth.UI.ViewModels
{
    /// <summary>
    /// Represents a mod reference in the UI, with properties for display, filtering, selection, and theming.
    /// </summary>
    public sealed record RuleBadgeInfo(IImage? Icon, int Count);
    public class ModRefViewModel : ObservableObject, ISelectableItem, IThemedViewModel
    {
        private bool isProblem;
        private bool isDuplicateWarning;
        private bool isFilteredOut;
        private bool isVisible = true;
        private bool isCached;
        private bool isSelected;
        private bool isJumpHighlighted;
        private bool isDragging;
        private bool isVanillaModSource;
        private bool isLocalModSource;
        private bool isSteamModSource;
        private bool isSteamLocalModSource;
        private bool showDropAbove;
        private bool showDropBelow;
        private string? problemTooltip;
        private string? duplicateWarningTooltip;
        private string? ruleBadgesTooltip;
        private IEnumerable<RuleBadgeInfo> ruleBadges = [];

        private IBrush backgroundBrush = Brushes.Transparent;
        private IBrush textBrush = Brushes.Black;
        private IBrush cacheBarBrush = Brushes.Transparent;
        private IBrush dropHighlightBrush = Brushes.Transparent;
        private TextDecorationCollection? textDecorations;

        public ModRefViewModel(ModReference modref)
        {
            ModReference = modref;
            string baseName = modref.Name ?? modref.ID ?? "Unknown Mod";
            DisplayName = string.IsNullOrWhiteSpace(modref.DisplayedVersion)
                ? baseName
                : $"{baseName} {modref.DisplayedVersion}";

            ThemedViewModelRegistry.Register(this);
        }

        public ModReference ModReference { get; }

        public DfhMod DfMod => ModReference.ToDFHMod();

        public string DisplayName { get; }

        public DateTime? LastModifiedTime => ModReference.LastModifiedTime;

        public bool IsProblem
        {
            get => isProblem;
            set
            {
                if (isProblem == value)
                    return;
                isProblem = value;
                RefreshTextStyle();
                OnPropertyChanged();
            }
        }

        public bool IsDuplicateWarning
        {
            get => isDuplicateWarning;
            set
            {
                if (isDuplicateWarning == value)
                    return;
                isDuplicateWarning = value;
                RefreshTextStyle();
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
                RefreshTextStyle();
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

        public bool IsCached
        {
            get => isCached;
            set
            {
                if (isCached == value)
                    return;
                isCached = value;
                OnPropertyChanged();
            }
        }

        public bool IsSelected
        {
            get => isSelected;
            set
            {
                if (isSelected == value)
                    return;
                isSelected = value;
                RefreshBackground();
                OnPropertyChanged();
            }
        }

        public bool IsJumpHighlighted
        {
            get => isJumpHighlighted;
            set
            {
                if (isJumpHighlighted == value)
                    return;
                isJumpHighlighted = value;
                RefreshBackground();
                OnPropertyChanged();
            }
        }

        public bool IsDragging
        {
            get => isDragging;
            set
            {
                if (isDragging == value)
                    return;
                isDragging = value;
                RefreshBackground();
                OnPropertyChanged();
            }
        }

        public bool IsLocalModSource
        {
            get => isLocalModSource;
            set
            {
                if (isLocalModSource == value)
                    return;
                isLocalModSource = value;
                OnPropertyChanged();
            }
        }

        public bool IsVanillaModSource
        {
            get => isVanillaModSource;
            set
            {
                if (isVanillaModSource == value)
                    return;
                isVanillaModSource = value;
                OnPropertyChanged();
            }
        }

        public bool IsSteamModSource
        {
            get => isSteamModSource;
            set
            {
                if (isSteamModSource == value)
                    return;
                isSteamModSource = value;
                OnPropertyChanged();
            }
        }

        public bool IsSteamLocalModSource
        {
            get => isSteamLocalModSource;
            set
            {
                if (isSteamLocalModSource == value)
                    return;
                isSteamLocalModSource = value;
                OnPropertyChanged();
            }
        }

        public bool ShowDropAbove
        {
            get => showDropAbove;
            set
            {
                if (showDropAbove == value)
                    return;
                showDropAbove = value;
                OnPropertyChanged();
            }
        }

        public bool ShowDropBelow
        {
            get => showDropBelow;
            set
            {
                if (showDropBelow == value)
                    return;
                showDropBelow = value;
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

        // Filter icons are tinted per text color; a handful of theme colors is all we should ever hold.
        private static readonly BoundedLruCache<Color, IImage?> filterIconTintCache = new(32);

        private static IImage? GetTintedFilterIcon(Color tint) => filterIconTintCache.GetOrAdd(tint, static c => ImageSourceLoader.LoadFromAssetUri("filterIcon.svg", c));

        private Color filterIconColor;
        private Color lastFilterColor;
        private IImage? cachedFilterIcon;

        public IImage? FilterIconImage
        {
            get
            {
                Color color = filterIconColor;
                if (cachedFilterIcon == null || lastFilterColor != color)
                {
                    lastFilterColor = color;
                    cachedFilterIcon = GetTintedFilterIcon(color);
                }
                return cachedFilterIcon;
            }
        }

        public IBrush TextBrush
        {
            get => textBrush;
            private set
            {
                if (Equals(textBrush, value))
                    return;
                textBrush = value;
                OnPropertyChanged();
            }
        }

        public IBrush CacheBarBrush
        {
            get => cacheBarBrush;
            private set
            {
                if (Equals(cacheBarBrush, value))
                    return;
                cacheBarBrush = value;
                OnPropertyChanged();
            }
        }

        public IBrush DropHighlightBrush
        {
            get => dropHighlightBrush;
            private set
            {
                if (Equals(dropHighlightBrush, value))
                    return;
                dropHighlightBrush = value;
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

        public string? ProblemTooltip
        {
            get => problemTooltip;
            set
            {
                if (problemTooltip == value)
                    return;
                problemTooltip = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HoverTooltip));
            }
        }

        public string? DuplicateWarningTooltip
        {
            get => duplicateWarningTooltip;
            set
            {
                if (duplicateWarningTooltip == value)
                    return;
                duplicateWarningTooltip = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HoverTooltip));
            }
        }

        public string? HoverTooltip
        {
            get
            {
                if (string.IsNullOrWhiteSpace(problemTooltip))
                    return duplicateWarningTooltip;
                if (string.IsNullOrWhiteSpace(duplicateWarningTooltip))
                    return problemTooltip;
                return $"{problemTooltip}{Environment.NewLine}{Environment.NewLine}{duplicateWarningTooltip}";
            }
        }

        public string? RuleBadgesTooltip
        {
            get => ruleBadgesTooltip;
            set
            {
                if (ruleBadgesTooltip == value)
                    return;
                ruleBadgesTooltip = value;
                OnPropertyChanged();
            }
        }
        public IEnumerable<RuleBadgeInfo> RuleBadges
        {
            get => ruleBadges;
            set
            {
                if (ruleBadges == value)
                    return;
                ruleBadges = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasRuleBadges));
            }
        }
        public bool HasRuleBadges => RuleBadges.Any();

        private int relationshipCount;
        private int beforeCount;
        private int afterCount;
        private int requiredCount;
        private int incompatibleCount;

        public int RelationshipCount
        {
            get => relationshipCount;
            set
            {
                if (relationshipCount == value)
                    return;
                relationshipCount = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasRelationships));
            }
        }

        public int BeforeCount
        {
            get => beforeCount;
            set
            {
                if (beforeCount == value)
                    return;
                beforeCount = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(RelationshipTooltipText));
            }
        }

        public int AfterCount
        {
            get => afterCount;
            set
            {
                if (afterCount == value)
                    return;
                afterCount = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(RelationshipTooltipText));
            }
        }

        public int RequiredCount
        {
            get => requiredCount;
            set
            {
                if (requiredCount == value)
                    return;
                requiredCount = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(RelationshipTooltipText));
            }
        }

        public int IncompatibleCount
        {
            get => incompatibleCount;
            set
            {
                if (incompatibleCount == value)
                    return;
                incompatibleCount = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(RelationshipTooltipText));
            }
        }

        public bool HasRelationships => RelationshipCount > 0;

        public string? RelationshipTooltipText
        {
            get
            {
                if (!HasRelationships)
                    return null;

                StringBuilder sb = new("Custom Sort Rules");
                if (BeforeCount > 0) _ = sb.AppendLine().Append($"• Before: {BeforeCount}");
                if (AfterCount > 0) _ = sb.AppendLine().Append($"• After: {AfterCount}");
                if (RequiredCount > 0) _ = sb.AppendLine().Append($"• Required: {RequiredCount}");
                if (IncompatibleCount > 0) _ = sb.AppendLine().Append($"• Incompatible: {IncompatibleCount}");
                return sb.ToString();
            }
        }

        public void RefreshStyle() => RefreshStyle(Style.instance ?? throw new InvalidOperationException("Style not loaded."));

        public void RefreshStyle(Style style)
        {
            if (style == null)
                return;
            RefreshBackground(style);
            RefreshTextStyle(style);
            RefreshAuxStyles(style);
        }

        public void RefreshBackground() => RefreshBackground(Style.instance ?? throw new InvalidOperationException("Style not loaded."));

        private void RefreshBackground(Style style)
        {
            Color baseColor = style.PanelColor.ToAvaloniaColor();
            Color targetColor;

            if (IsDragging)
            {
                targetColor = LightenColor(baseColor, 0.35f);
            }
            else
            {
                Color? selectionOverlay = null;
                if (IsJumpHighlighted)
                    selectionOverlay = style.ModRefJumpHighlightColor.ToAvaloniaColor();
                else if (IsSelected)
                    selectionOverlay = style.ModRefHighlightColor.ToAvaloniaColor();

                targetColor = selectionOverlay.HasValue
                    ? BlendColor(baseColor, selectionOverlay.Value)
                    : baseColor;
            }

            // Skip allocation & property notifications if color hasn't changed
            if (!(BackgroundBrush is ISolidColorBrush scb && scb.Color == targetColor))
                BackgroundBrush = BrushCache.GetBrush(targetColor);

            RefreshModColorUnderlay();
        }

        private void RefreshTextStyle() => RefreshTextStyle(Style.instance ?? throw new InvalidOperationException("Style not loaded."));

        private void RefreshTextStyle(Style style)
        {
            Color color;

            // During search mismatch rendering, filtered style must win over issue/warning overlays.
            if (IsFilteredOut)
            {
                color = style.ModRefTextFilteredColor.ToAvaloniaColor();
            }
            else if (IsDuplicateWarning)
            {
                color = IsProblem
                ? style.ModRefTextBadColor.ToAvaloniaColor()
                : style.ModRefTextWarningColor.ToAvaloniaColor();
            }
            else
            {
                color = IsProblem
                ? style.ModRefTextBadColor.ToAvaloniaColor()
                : style.TextColor.ToAvaloniaColor();
            }

            IBrush newBrush = BrushCache.GetBrush(color);
            if (textBrush != newBrush)
            {
                textBrush = newBrush;
                OnPropertyChanged(nameof(TextBrush));
            }

            if (filterIconColor != color)
            {
                filterIconColor = color;
                OnPropertyChanged(nameof(FilterIconImage));
            }

            TextDecorationCollection? targetDecoration = IsFilteredOut ? Avalonia.Media.TextDecorations.Strikethrough : null;
            if (textDecorations != targetDecoration)
            {
                textDecorations = targetDecoration;
                OnPropertyChanged(nameof(TextDecorations));
            }
        }

        private void RefreshAuxStyles(Style style)
        {
            Color cacheBarColor = style.ModRefCacheBarColor.ToAvaloniaColor();
            Color dropHighlightColor = style.ModRefHighlightColor.ToAvaloniaColor();

            if (!(CacheBarBrush is ISolidColorBrush cb && cb.Color == cacheBarColor))
                CacheBarBrush = BrushCache.GetBrush(cacheBarColor);

            if (!(DropHighlightBrush is ISolidColorBrush dh && dh.Color == dropHighlightColor))
                DropHighlightBrush = BrushCache.GetBrush(dropHighlightColor);
        }

        private string? searchTargetCache;
        private string? searchTargetName;
        private string? searchTargetId;
        private string? searchTargetSteamId;
        private string? searchTargetDescription;

        /// <summary>
        /// Combined text used by regex searches, rebuilt only when one of its source fields changes.
        /// </summary>
        private string GetSearchTarget()
        {
            string name = ModReference.Name ?? string.Empty;
            string id = ModReference.ID ?? string.Empty;
            string steamId = ModReference.SteamID ?? string.Empty;
            string description = ModReference.Description ?? string.Empty;

            if (searchTargetCache == null ||
                !string.Equals(searchTargetName, name, StringComparison.Ordinal) ||
                !string.Equals(searchTargetId, id, StringComparison.Ordinal) ||
                !string.Equals(searchTargetSteamId, steamId, StringComparison.Ordinal) ||
                !string.Equals(searchTargetDescription, description, StringComparison.Ordinal))
            {
                searchTargetName = name;
                searchTargetId = id;
                searchTargetSteamId = steamId;
                searchTargetDescription = description;
                searchTargetCache = $"{name} {id} {steamId} {description}";
            }

            return searchTargetCache;
        }

        public bool MatchesFilter(SearchFilterContext context)
        {
            if (context == null || !context.HasFilter)
                return true;

            switch (context.Mode)
            {
                case SearchFilterMode.Regex:
                    // Treat entire mod info as target for regex to be most flexible
                    return context.MatchesRegex(GetSearchTarget());

                case SearchFilterMode.Color:
                    return context.MatchesColor(ModReference.AssignedColor);
            }

            string? candidate = context.Mode switch
            {
                SearchFilterMode.Name => ModReference.Name,
                SearchFilterMode.Id => ModReference.ID,
                SearchFilterMode.SteamFileId => ModReference.SteamID,
                SearchFilterMode.Regex => throw new NotImplementedException(),
                SearchFilterMode.Color => throw new NotImplementedException(),
                SearchFilterMode.ModifiedTime => ModReference.Name,
                _ => ModReference.Name
            };

            return context.Contains(candidate);
        }

        private static Color BlendColor(Color baseColor, Color overlay)
        {
            if (overlay.A >= 255)
                return overlay;

            float a = overlay.A / 255f;
            byte r = (byte)((baseColor.R * (1 - a)) + (overlay.R * a));
            byte g = (byte)((baseColor.G * (1 - a)) + (overlay.G * a));
            byte b = (byte)((baseColor.B * (1 - a)) + (overlay.B * a));
            return Color.FromArgb(255, r, g, b);
        }

        public void RefreshModColorUnderlay()
        {
            ModColor modColor = ModReference.AssignedColor;
            if (modColor == ModColor.None)
            {
                if (ColorUnderlayBrush != null)
                    ColorUnderlayBrush = null;
                return;
            }

            Color color = ModColorMap.GetColor(modColor);

            if (!(ColorUnderlayBrush is ISolidColorBrush scb && scb.Color == color))
            {
                ColorUnderlayBrush = BrushCache.GetBrush(color);
            }
        }

        private IBrush? colorUnderlayBrush;
        public IBrush? ColorUnderlayBrush
        {
            get => colorUnderlayBrush;
            set
            {
                if (Equals(colorUnderlayBrush, value))
                    return;
                colorUnderlayBrush = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasColorUnderlay));
                if (DevMode.IsEnabled)
                    InfoLogger.Log($"changed color {HasColorUnderlay}");
            }
        }
        public bool HasColorUnderlay => ColorUnderlayBrush != null;

        private static Color LightenColor(Color baseColor, float amount)
        {
            amount = Math.Clamp(amount, 0f, 1f);
            byte r = (byte)Math.Clamp(baseColor.R + ((255 - baseColor.R) * amount), 0, 255);
            byte g = (byte)Math.Clamp(baseColor.G + ((255 - baseColor.G) * amount), 0, 255);
            byte b = (byte)Math.Clamp(baseColor.B + ((255 - baseColor.B) * amount), 0, 255);
            return Color.FromArgb(255, r, g, b);
        }
    }
}
