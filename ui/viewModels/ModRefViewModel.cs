using System.Globalization;
using System.Text;
using Avalonia.Media;
using ModHearth.Models;
using ModHearth.UI.Contracts;
using ModHearth.UI.Helpers;
using ModHearth.UI.Rendering;
using ModHearth.UI.Theme;
using ModHearth.Utils;
using ModHearth.Utils.Logging;

namespace ModHearth.UI.ViewModels
{
    /// <summary>
    /// Represents a mod reference in the UI, with properties for display, filtering, selection, and theming.
    /// </summary>
    public sealed record RuleBadgeInfo(IImage? Icon, int Count);
    public class ModRefViewModel : ObservableObject, ISelectableItem, IThemedViewModel, IFilterableItem
    {
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

        /// <summary>
        /// Highest tier among guaranteed problems reported for this mod (load-order rules,
        /// duplicate folders, error-log issues). <see cref="ModIssueTier.None"/> means none.
        /// </summary>
        public ModIssueTier ProblemTier
        {
            get;
            set
            {
                if (SetField(ref field, value))
                    OnProblemSeverityChanged();
            }
        }

        /// <summary>
        /// Tier for duplicate-raw warnings. Error-log duplicates are guaranteed and get a tier;
        /// cache-only "potential" duplicates use <see cref="ModIssueTier.None"/> so they stay
        /// tooltip-only and never show a severity icon.
        /// </summary>
        public ModIssueTier DuplicateWarningTier
        {
            get;
            set
            {
                if (SetField(ref field, value))
                    OnProblemSeverityChanged();
            }
        }

        /// <summary>
        /// Effective severity shown in the row. Combining here keeps the two problem sources
        /// independent while guaranteeing the worst tier wins.
        /// </summary>
        public ModIssueTier IssueTier => (ModIssueTier)Math.Max((int)ProblemTier, (int)DuplicateWarningTier);

        public bool HasGuaranteedIssue => IssueTier != ModIssueTier.None;
        public bool IsErrorIssue => IssueTier == ModIssueTier.Error;
        public bool IsWarningIssue => IssueTier == ModIssueTier.Warning;
        public bool IsInfoIssue => IssueTier == ModIssueTier.Info;

        public bool IsDuplicateWarning
        {
            get;
            set
            {
                if (SetField(ref field, value))
                    RefreshTextStyle();
            }
        }

        public bool IsFilteredOut
        {
            get;
            set
            {
                if (SetField(ref field, value))
                    RefreshTextStyle();
            }
        }

        public bool IsVisible
        {
            get;
            set => SetField(ref field, value);
        } = true;

        public bool IsCached
        {
            get;
            set => SetField(ref field, value);
        }

        public bool IsSelected
        {
            get;
            set
            {
                if (SetField(ref field, value))
                    RefreshBackground();
            }
        }

        public bool IsJumpHighlighted
        {
            get;
            set
            {
                if (SetField(ref field, value))
                    RefreshBackground();
            }
        }

        public bool IsDragging
        {
            get;
            set
            {
                if (SetField(ref field, value))
                    RefreshBackground();
            }
        }

        public bool IsLocalModSource
        {
            get;
            set => SetField(ref field, value);
        }

        public bool IsVanillaModSource
        {
            get;
            set => SetField(ref field, value);
        }

        public bool IsSteamModSource
        {
            get;
            set => SetField(ref field, value);
        }

        public bool IsSteamLocalModSource
        {
            get;
            set => SetField(ref field, value);
        }

        /// <summary>
        /// True when this mod is only available through Dwarf Fortress's disposable installed_mods
        /// cache. It is a real, deletable folder, but not one of the user's mod source folders.
        /// </summary>
        public bool IsCacheOnlyModSource
        {
            get;
            set => SetField(ref field, value);
        }

        public bool ShowDropAbove
        {
            get;
            set => SetField(ref field, value);
        }

        public bool ShowDropBelow
        {
            get;
            set => SetField(ref field, value);
        }

        public IBrush BackgroundBrush
        {
            get;
            private set => SetField(ref field, value);
        } = Brushes.Transparent;

        // Filter icons are tinted per text color; a handful of theme colors is all we should ever hold.
        private static readonly BoundedLruCache<Color, IImage?> filterIconTintCache = new(32);

        private static IImage? GetTintedFilterIcon(Color tint) => filterIconTintCache.GetOrAdd(tint, static c => ImageSourceLoader.LoadFromAssetUri("filterIcon.svg", c));

        private Color filterIconColor;
        private Color lastFilterColor;

        public IImage? FilterIconImage
        {
            get
            {
                Color color = filterIconColor;
                if (field == null || lastFilterColor != color)
                {
                    lastFilterColor = color;
                    field = GetTintedFilterIcon(color);
                }
                return field;
            }
        }

        public IBrush TextBrush
        {
            get;
            private set => SetField(ref field, value);
        } = Brushes.Black;

        /// <summary>
        /// Tint for the severity icon. Kept in sync with <see cref="TextBrush"/> so the icon and
        /// label always communicate the same tier.
        /// </summary>
        public IBrush IssueBrush
        {
            get;
            private set => SetField(ref field, value);
        } = Brushes.Transparent;

        public IBrush CacheBarBrush
        {
            get;
            private set => SetField(ref field, value);
        } = Brushes.Transparent;

        public IBrush DropHighlightBrush
        {
            get;
            private set => SetField(ref field, value);
        } = Brushes.Transparent;

        public TextDecorationCollection? TextDecorations
        {
            get => textDecorations;
            private set => SetField(ref textDecorations, value);
        }

        public string? ProblemTooltip
        {
            get;
            set
            {
                if (SetField(ref field, value))
                    OnPropertyChanged(nameof(HoverTooltip));
            }
        }

        public string? DuplicateWarningTooltip
        {
            get;
            set
            {
                if (SetField(ref field, value))
                    OnPropertyChanged(nameof(HoverTooltip));
            }
        }

        public string? HoverTooltip
        {
            get
            {
                if (string.IsNullOrWhiteSpace(ProblemTooltip))
                    return DuplicateWarningTooltip;
                if (string.IsNullOrWhiteSpace(DuplicateWarningTooltip))
                    return ProblemTooltip;
                return $"{ProblemTooltip}{Environment.NewLine}{Environment.NewLine}{DuplicateWarningTooltip}";
            }
        }

        public string? RuleBadgesTooltip
        {
            get;
            set => SetField(ref field, value);
        }
        public IEnumerable<RuleBadgeInfo> RuleBadges
        {
            get;
            set
            {
                if (SetField(ref field, value))
                    OnPropertyChanged(nameof(HasRuleBadges));
            }
        } = [];
        public bool HasRuleBadges => RuleBadges.Any();

        public int RelationshipCount
        {
            get;
            set
            {
                if (SetField(ref field, value))
                    OnPropertyChanged(nameof(HasRelationships));
            }
        }

        public int BeforeCount
        {
            get;
            set
            {
                if (SetField(ref field, value))
                    OnPropertyChanged(nameof(RelationshipTooltipText));
            }
        }

        public int AfterCount
        {
            get;
            set
            {
                if (SetField(ref field, value))
                    OnPropertyChanged(nameof(RelationshipTooltipText));
            }
        }

        public int RequiredCount
        {
            get;
            set
            {
                if (SetField(ref field, value))
                    OnPropertyChanged(nameof(RelationshipTooltipText));
            }
        }

        public int IncompatibleCount
        {
            get;
            set
            {
                if (SetField(ref field, value))
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
                if (BeforeCount > 0) _ = sb.AppendLine().Append(CultureInfo.InvariantCulture, $"• Before: {BeforeCount}");
                if (AfterCount > 0) _ = sb.AppendLine().Append(CultureInfo.InvariantCulture, $"• After: {AfterCount}");
                if (RequiredCount > 0) _ = sb.AppendLine().Append(CultureInfo.InvariantCulture, $"• Required: {RequiredCount}");
                if (IncompatibleCount > 0) _ = sb.AppendLine().Append(CultureInfo.InvariantCulture, $"• Incompatible: {IncompatibleCount}");
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
            else
            {
                color = IssueTier switch
                {
                    ModIssueTier.Error => style.ModRefIssueErrorColor.ToAvaloniaColor(),
                    ModIssueTier.Warning => style.ModRefIssueWarningColor.ToAvaloniaColor(),
                    ModIssueTier.Info => style.ModRefIssueInfoColor.ToAvaloniaColor(),

                    // Potential-only duplicate warnings stay at the mildest tint but have no icon.
                    _ => IsDuplicateWarning
                        ? style.ModRefIssueInfoColor.ToAvaloniaColor()
                        : style.TextColor.ToAvaloniaColor()
                };
            }

            IBrush newBrush = BrushCache.GetBrush(color);
            TextBrush = newBrush;
            IssueBrush = newBrush;

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

        private void OnProblemSeverityChanged()
        {
            RefreshTextStyle();
            OnPropertyChanged(nameof(IssueTier));
            OnPropertyChanged(nameof(HasGuaranteedIssue));
            OnPropertyChanged(nameof(IsErrorIssue));
            OnPropertyChanged(nameof(IsWarningIssue));
            OnPropertyChanged(nameof(IsInfoIssue));
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
                SearchFilterMode.Id => ModReference.ID,
                SearchFilterMode.SteamFileId => ModReference.SteamID,
                _ => ModReference.Name,
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

        public IBrush? ColorUnderlayBrush
        {
            get;
            set
            {
                if (SetField(ref field, value))
                {
                    OnPropertyChanged(nameof(HasColorUnderlay));
                    Loggers.Diag.Dev($"changed color {HasColorUnderlay}");
                }
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
