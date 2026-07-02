namespace ModHearth;

internal static class DevMode
{
    public static bool IsEnabled
    {
        get
        {
            string? value = Environment.GetEnvironmentVariable("MODHEARTH_DEVMODE");
            return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
        }
    }
}