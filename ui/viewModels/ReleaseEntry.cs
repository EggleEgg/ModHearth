namespace ModHearth.UI;

public sealed class ReleaseEntry
{
    public string Title { get; init; } = string.Empty;

    public string Subtitle { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public bool HasDescription =>
        !string.IsNullOrWhiteSpace(Description);

    public bool IsNotFirstItem { get; init; }

    public bool CanInstall { get; init; }

    public string ButtonLabel { get; init; } = string.Empty;

    // Optional: if InstallClicked handler needs to know which release
    public object? Tag { get; init; }
}