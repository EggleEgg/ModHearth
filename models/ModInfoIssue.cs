namespace ModHearth.Models
{
    /// <summary>
    /// Which kind of metadata problem Dwarf Fortress would report while reading a mod's
    /// <c>info.txt</c>. These mirror the error-log categories produced by
    /// <see cref="ModHearth.Utilities.ModRaws.ModErrorLogAnalyzer"/>.
    /// </summary>
    public enum ModInfoIssueKind
    {
        InvalidIdentifier,
        InvalidVersionMetadata,
        UnrecognizedToken
    }

    /// <summary>
    /// A single statically predicted <c>info.txt</c> problem. <see cref="Category"/> uses the same
    /// wording as the error-log analyzer so <see cref="ModIssueClassifier.ForErrorLogCategory"/>
    /// can assign the same severity without a second mapping table.
    /// </summary>
    public sealed record ModInfoIssue(ModInfoIssueKind Kind, string Detail)
    {
        public string Category => Kind switch
        {
            ModInfoIssueKind.InvalidIdentifier => "Invalid mod identifier",
            ModInfoIssueKind.InvalidVersionMetadata => "Invalid mod version metadata",
            _ => "Unrecognized mod info token"
        };
    }
}
