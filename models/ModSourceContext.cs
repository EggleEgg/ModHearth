namespace ModHearth.Models
{
    /// <summary>
    /// Immutable snapshot of the configured mod roots. Callers that classify many mods (especially
    /// inside parallel loops) resolve the roots once and reuse the snapshot instead of re-querying
    /// <see cref="ConfigManager"/> - and potentially re-saving config - for every mod.
    /// </summary>
    public readonly record struct ModSourceContext(string? ModsFolderPath, string? VanillaFolderPath)
    {
        public static ModSourceContext FromConfig()
            => new(ConfigManager.GetModsPath(), ConfigManager.GetVanillaModsPath());
    }
}
