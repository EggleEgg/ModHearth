namespace ModHearth.utilities
{
    /// <summary>
    /// Math helpers
    /// </summary>
    public static class MMath
    {
        public const float epsilon = 0.0001f;
        public static bool Equals(this double value, double other, float epsilon = epsilon) => Math.Abs(value - other) < epsilon;
        public static bool Equals(this float value, float other, float epsilon = epsilon) => MathF.Abs(value - other) < epsilon;
    }
}