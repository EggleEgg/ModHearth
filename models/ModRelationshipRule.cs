using System.Text.Json.Serialization;

namespace ModHearth;

public sealed class ModRelationshipRule
{
    [JsonPropertyName("beforeIds")]
    public List<string> BeforeIds { get; set; } = [];

    [JsonPropertyName("afterIds")]
    public List<string> AfterIds { get; set; } = [];

    [JsonPropertyName("requiredIds")]
    public List<string> RequiredIds { get; set; } = [];

    [JsonPropertyName("incompatibleIds")]
    public List<string> IncompatibleIds { get; set; } = [];

    [JsonIgnore]
    public bool IsEmpty =>
        BeforeIds.Count == 0 &&
        AfterIds.Count == 0 &&
        RequiredIds.Count == 0 &&
        IncompatibleIds.Count == 0;

    public ModRelationshipRule Clone()
    {
        return new ModRelationshipRule
        {
            BeforeIds = [.. BeforeIds],
            AfterIds = [.. AfterIds],
            RequiredIds = [.. RequiredIds],
            IncompatibleIds = [.. IncompatibleIds]
        };
    }
}

public enum ModRelationshipKind
{
    // Unlike vanilla df these 2 only care about position, not dependencies (except against each other)
    Before,
    After,

    // Needs to be anywhere in the list
    Required,
    // Conflicts with everything else
    Incompatible
}
