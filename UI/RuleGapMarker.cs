using Avalonia.Media;

namespace ModHearth.UI;

public sealed class RuleGapMarker
{
    public RuleGapMarker(IBrush lineBrush)
    {
        LineBrush = lineBrush ?? Brushes.Transparent;
    }

    public IBrush LineBrush { get; }
}
