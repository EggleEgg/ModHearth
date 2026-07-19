using System.Text.Json.Serialization;

namespace ModHearth;

public sealed class ModRelationshipRule
{
    [JsonPropertyName("beforeIds")]
    public List<string> BeforeIds { get; set; } = new();

    [JsonPropertyName("afterIds")]
    public List<string> AfterIds { get; set; } = new();

    [JsonPropertyName("requiredIds")]
    public List<string> RequiredIds { get; set; } = new();

    [JsonPropertyName("incompatibleIds")]
    public List<string> IncompatibleIds { get; set; } = new();

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
            BeforeIds = new List<string>(BeforeIds),
            AfterIds = new List<string>(AfterIds),
            RequiredIds = new List<string>(RequiredIds),
            IncompatibleIds = new List<string>(IncompatibleIds)
        };
    }
}

public enum ModRelationshipKind
{
    Before,
    After,
    Required,
    Incompatible
}
