using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Media;

namespace ModHearth.UI;

public class ModRefViewModel : INotifyPropertyChanged
{
    private readonly ModReference modref;
    private bool isProblem;
    private bool isFilteredOut;
    private bool isCached;
    private bool isSelected;
    private bool isJumpHighlighted;
    private bool isDragging;
    private bool showDropAbove;
    private bool showDropBelow;
    private string? problemTooltip;

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
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ModReference ModReference => modref;

    public DFHMod DfMod => modref.ToDFHMod();

    public string DisplayName { get; }

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
        Style style = Style.instance ?? Style.GetFallback();
        Color baseColor = style.modRefColor.ToAvaloniaColor();
        if (IsDragging)
        {
            BackgroundBrush = new SolidColorBrush(LightenColor(baseColor, 0.35f));
            return;
        }

        Color? overlay = null;
        if (IsJumpHighlighted)
            overlay = style.modRefJumpHighlightColor.ToAvaloniaColor();
        else if (IsSelected)
            overlay = style.modRefHighlightColor.ToAvaloniaColor();

        Color blended = overlay.HasValue
            ? BlendColor(baseColor, overlay.Value)
            : baseColor;
        BackgroundBrush = new SolidColorBrush(blended);
    }

    private void RefreshTextStyle()
    {
        Style style = Style.instance ?? Style.GetFallback();
        Color color;
        if (IsProblem)
            color = style.modRefTextBadColor.ToAvaloniaColor();
        else if (IsFilteredOut)
            color = style.modRefTextFilteredColor.ToAvaloniaColor();
        else
            color = style.modRefTextColor.ToAvaloniaColor();

        TextBrush = new SolidColorBrush(color);
        TextDecorations = IsFilteredOut ? Avalonia.Media.TextDecorations.Strikethrough : null;
    }

    private void RefreshAuxStyles()
    {
        Style style = Style.instance ?? Style.GetFallback();
        CacheBarBrush = new SolidColorBrush(style.modRefCacheBarColor.ToAvaloniaColor());
        DropHighlightBrush = new SolidColorBrush(style.modRefHighlightColor.ToAvaloniaColor());
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
}
