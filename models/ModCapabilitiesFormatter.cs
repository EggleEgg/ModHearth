namespace ModHearth;

/// <summary>
/// Converts ModCapabilities flags into the user-facing tag set. Deliberately narrower than the raw
/// flag list -- DfHackStartup is a refinement of DfHack+Lua (init.d/ auto-run scripts), not a
/// separate category most users need to see -- matching the source proposal's "expose fewer of these
/// to the user" guidance.
/// </summary>
public static class ModCapabilitiesFormatter
{
    public static IEnumerable<string> ToDisplayTags(this ModCapabilities capabilities)
    {
        if (capabilities.HasFlag(ModCapabilities.Raw)) yield return "Raw";
        if (capabilities.HasFlag(ModCapabilities.Graphics)) yield return "Graphics";
        if (capabilities.HasFlag(ModCapabilities.Sound)) yield return "Sound";
        if (capabilities.HasFlag(ModCapabilities.DfHack)) yield return "DFHack";
        if (capabilities.HasFlag(ModCapabilities.Lua)) yield return "Lua";
        if (capabilities.HasFlag(ModCapabilities.Native)) yield return "Native";
    }

    public static string ToDisplayString(this ModCapabilities capabilities)
    {
        string joined = string.Join(", ", capabilities.ToDisplayTags());
        return string.IsNullOrEmpty(joined) ? "None" : joined;
    }
}