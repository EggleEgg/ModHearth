namespace ModHearth.UI.ViewModels;

public sealed class ReleaseEntry
{
    public string Title { get; init; } = string.Empty;
    public string Subtitle { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);
    public string ButtonLabel { get; init; } = "Install";
    public bool CanInstall { get; init; } = true;
    public bool IsFirstItem { get; init; }
    public bool IsNotFirstItem { get; init; }
    public GitHubRelease Release { get; init; } = new GitHubRelease();

    public static ReleaseEntry FromRelease(GitHubRelease release, int index, string currentBuild)
    {
        string title = UpdateHelpers.GetReleaseTitle(release, index);
        string subtitle = UpdateHelpers.GetReleaseSubtitle(release, currentBuild);
        string? buildNumber = UpdateHelpers.TryGetBuildNumber(release);
        bool isCurrent = !string.IsNullOrWhiteSpace(buildNumber) &&
                         string.Equals(buildNumber, currentBuild, StringComparison.OrdinalIgnoreCase);

        return new ReleaseEntry
        {
            Title = title,
            Subtitle = subtitle,
            Description = release.Body ?? string.Empty,
            ButtonLabel = isCurrent ? "Reinstall" : "Install",
            IsFirstItem = index == 0,
            IsNotFirstItem = index != 0,
            Release = release
        };
    }
}
