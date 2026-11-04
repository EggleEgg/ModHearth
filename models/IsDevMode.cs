namespace ModHearth.Models
{
    internal static class DevMode
    {
        // -1 = unresolved, 0 = off, 1 = on. Resolved once from the environment; afterwards only Set() changes it.
        private static int state = -1;

        public static bool IsEnabled
        {
            get
            {
                int current = Volatile.Read(ref state);
                if (current < 0)
                {
                    _ = Interlocked.CompareExchange(ref state, FromEnvironment() ? 1 : 0, -1);
                    current = Volatile.Read(ref state);
                }
                return current == 1;
            }
        }

        public static void Set(bool enabled) => Volatile.Write(ref state, enabled ? 1 : 0);

        private static bool FromEnvironment()
        {
            string? value = Environment.GetEnvironmentVariable("MODHEARTH_DEVMODE");
            return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
        }
    }
}
