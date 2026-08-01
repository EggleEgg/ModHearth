using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Media;
using ModHearth.Utilities.Logging;

namespace ModHearth.UI;

/// <summary>
/// For creating new view instances per window
/// </summary>
public sealed record RuleBadgeInfo(IImage? Icon, int Count);
public class ModRefViewModel : INotifyPropertyChanged, ISelectableItem
{
    private static readonly List<WeakReference<ModRefViewModel>> allInstances = new();
    private static readonly object instancesLock = new();

    private readonly ModReference modref;
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
    private ReferenceOverlayKind referenceOverlay;
    private Thickness ruleGapMargin = new Thickness(0);
    private string? problemTooltip;
    private string? duplicateWarningTooltip;
    private string ruleBadgesText = string.Empty;
    private string? ruleBadgesTooltip;
    private IEnumerable<RuleBadgeInfo> ruleBadges = Array.Empty<RuleBadgeInfo>();

    private IBrush backgroundBrush = Brushes.Transparent;
    private IBrush textBrush = Brushes.Black;
    private IBrush cacheBarBrush = Brushes.Transparent;
    private IBrush dropHighlightBrush = Brushes.Transparent;
    private TextDecorationCollection? textDecorations;

    public ModRefViewModel(ModReference modref)
    {
        this.modref = modref;
        string baseName = modref.name ?? modref.ID ?? "Unknown Mod";
        DisplayName = string.IsNullOrWhiteSpace(modref.displayedVersion)
            ? baseName
            : $"{baseName} {modref.displayedVersion}";

        lock (instancesLock)
        {
            CleanupInstancesLocked();
            allInstances.Add(new WeakReference<ModRefViewModel>(this));
        }
    }

    private static void CleanupInstancesLocked()
    {
        allInstances.RemoveAll(weak => !weak.TryGetTarget(out _));
    }

    public static void RefreshAllStyles()
    {
        List<ModRefViewModel> targets = new();
        lock (instancesLock)
        {
            CleanupInstancesLocked();
            foreach (var weak in allInstances)
            {
                if (weak.TryGetTarget(out var vm))
                {
                    targets.Add(vm);
                }
            }
        }

        foreach (var vm in targets)
        {
            vm.RefreshStyle();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ModReference ModReference => modref;

    public DFHMod DfMod => modref.ToDFHMod();

    public string DisplayName { get; }

    public DateTime? LastModifiedTime => modref.LastModifiedTime;

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

    public ReferenceOverlayKind ReferenceOverlay
    {
        get => referenceOverlay;
        set
        {
            if (referenceOverlay == value)
                return;
            referenceOverlay = value;
            RefreshBackground();
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

    public Thickness RuleGapMargin
    {
        get => ruleGapMargin;
        set
        {
            if (ruleGapMargin == value)
                return;
            ruleGapMargin = value;
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

    public string RuleBadgesText
    {
        get => ruleBadgesText;
        set
        {
            if (ruleBadgesText == value)
                return;
            ruleBadgesText = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasRuleBadges));
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
    public int intBeforeCount;
    public int intAfterCount;
    public int intRequiredCount;
    public int intIncompatibleCount;

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
        get => intBeforeCount;
        set
        {
            if (intBeforeCount == value)
                return;
            intBeforeCount = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(RelationshipTooltipText));
        }
    }

    public int AfterCount
    {
        get => intAfterCount;
        set
        {
            if (intAfterCount == value)
                return;
            intAfterCount = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(RelationshipTooltipText));
        }
    }

    public int RequiredCount
    {
        get => intRequiredCount;
        set
        {
            if (intRequiredCount == value)
                return;
            intRequiredCount = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(RelationshipTooltipText));
        }
    }

    public int IncompatibleCount
    {
        get => intIncompatibleCount;
        set
        {
            if (intIncompatibleCount == value)
                return;
            intIncompatibleCount = value;
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

            StringBuilder sb = new StringBuilder("Custom Sort Rules");
            if (BeforeCount > 0) sb.AppendLine().Append($"• Before: {BeforeCount}");
            if (AfterCount > 0) sb.AppendLine().Append($"• After: {AfterCount}");
            if (RequiredCount > 0) sb.AppendLine().Append($"• Required: {RequiredCount}");
            if (IncompatibleCount > 0) sb.AppendLine().Append($"• Incompatible: {IncompatibleCount}");
            return sb.ToString();
        }
    }

    public void RefreshStyle()
    {
        RefreshBackground();
        RefreshTextStyle();
        RefreshAuxStyles();
    }

    public void RefreshBackground()
    {
        Style style = Style.instance ?? throw new InvalidOperationException("Style not loaded.");
        Color baseColor = style.panelColor.ToAvaloniaColor();
        Color targetColor;

        if (IsDragging)
            targetColor = LightenColor(baseColor, 0.35f);
        else
        {
            Color? referenceOverlayColor = ReferenceOverlay switch
            {
                ReferenceOverlayKind.AboveSelection => style.modRefCacheBarColor.ToAvaloniaColor(),
                ReferenceOverlayKind.BelowSelection => style.modRefJumpHighlightColor.ToAvaloniaColor(),
                _ => null
            };

            Color blended = referenceOverlayColor.HasValue
                ? BlendColor(baseColor, referenceOverlayColor.Value)
                : baseColor;

            Color? selectionOverlay = null;
            if (IsJumpHighlighted)
                selectionOverlay = style.modRefJumpHighlightColor.ToAvaloniaColor();
            else if (IsSelected)
                selectionOverlay = style.modRefHighlightColor.ToAvaloniaColor();

            targetColor = selectionOverlay.HasValue
                ? BlendColor(blended, selectionOverlay.Value)
                : blended;
        }

        // Skip allocation & property notifications if color hasn't changed
        if (!(BackgroundBrush is ISolidColorBrush scb && scb.Color == targetColor))
            BackgroundBrush = BrushCache.GetBrush(targetColor);

        RefreshModColorUnderlay();
    }

    private void RefreshTextStyle()
    {
        Style style = Style.instance ?? throw new InvalidOperationException("Style not loaded.");
        Color color;

        // During search mismatch rendering, filtered style must win over issue/warning overlays.
        if (IsFilteredOut)
            color = style.modRefTextFilteredColor.ToAvaloniaColor();
        else if (IsProblem)
            color = style.modRefTextBadColor.ToAvaloniaColor();
        else if (IsDuplicateWarning)
            color = style.modRefTextWarningColor.ToAvaloniaColor();
        else
            color = style.textColor.ToAvaloniaColor();

        IBrush newBrush = BrushCache.GetBrush(color);
        if (textBrush != newBrush)
        {
            textBrush = newBrush;
            OnPropertyChanged(nameof(TextBrush));
        }
        else
        {
            OnPropertyChanged(nameof(TextBrush));
        }

        var targetDecoration = IsFilteredOut ? Avalonia.Media.TextDecorations.Strikethrough : null;
        if (textDecorations != targetDecoration)
        {
            textDecorations = targetDecoration;
            OnPropertyChanged(nameof(TextDecorations));
        }
        else
        {
            OnPropertyChanged(nameof(TextDecorations));
        }
    }

    private void RefreshAuxStyles()
    {
        Style style = Style.instance ?? throw new InvalidOperationException("Style not loaded.");
        Color cacheBarColor = style.modRefCacheBarColor.ToAvaloniaColor();
        Color dropHighlightColor = style.modRefHighlightColor.ToAvaloniaColor();

        if (!(CacheBarBrush is ISolidColorBrush cb && cb.Color == cacheBarColor))
            CacheBarBrush = BrushCache.GetBrush(cacheBarColor);

        if (!(DropHighlightBrush is ISolidColorBrush dh && dh.Color == dropHighlightColor))
            DropHighlightBrush = BrushCache.GetBrush(dropHighlightColor);
    }

    public bool MatchesFilter(string filter, SearchFilterMode mode)
    {
        if (string.IsNullOrWhiteSpace(filter))
            return true;

        if (mode == SearchFilterMode.Regex)
        {
            try
            {
                // Treat entire mod info as target for regex to be most flexible
                string fullTarget = $"{modref.name} {modref.ID} {modref.steamID} {modref.description}";
                return Regex.IsMatch(fullTarget, filter, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            }
            catch
            {
                return false;
            }
        }

        if (mode == SearchFilterMode.Color)
        {
            // Filter is a comma-separated string of selected ModColor names
            // If we're here, filter is not null or whitespace (checked at top)
            var selectedColorNames = filter.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .ToList();

            if (selectedColorNames.Count == 0)
                return true;

            return selectedColorNames.Any(name =>
                string.Equals(name, modref.AssignedColor.ToString(), StringComparison.OrdinalIgnoreCase));
        }

        string? candidate = mode switch
        {
            SearchFilterMode.Name => modref.name,
            SearchFilterMode.Id => modref.ID,
            SearchFilterMode.SteamFileId => modref.steamID,
            _ => modref.name
        };

        return !string.IsNullOrWhiteSpace(candidate) &&
               candidate.Contains(filter, StringComparison.OrdinalIgnoreCase);
    }

    private static Color BlendColor(Color baseColor, Color overlay)
    {
        if (overlay.A >= 255)
            return overlay;

        float a = overlay.A / 255f;
        byte r = (byte)(baseColor.R * (1 - a) + overlay.R * a);
        byte g = (byte)(baseColor.G * (1 - a) + overlay.G * a);
        byte b = (byte)(baseColor.B * (1 - a) + overlay.B * a);
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
            InfoLogger.Log($"changed color {HasColorUnderlay}");
        }
    }
    public bool HasColorUnderlay => ColorUnderlayBrush != null;

    private static Color LightenColor(Color baseColor, float amount)
    {
        amount = Math.Clamp(amount, 0f, 1f);
        byte r = (byte)Math.Clamp(baseColor.R + (255 - baseColor.R) * amount, 0, 255);
        byte g = (byte)Math.Clamp(baseColor.G + (255 - baseColor.G) * amount, 0, 255);
        byte b = (byte)Math.Clamp(baseColor.B + (255 - baseColor.B) * amount, 0, 255);
        return Color.FromArgb(255, r, g, b);
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public enum ReferenceOverlayKind
    {
        None,
        AboveSelection,
        BelowSelection
    }
}
