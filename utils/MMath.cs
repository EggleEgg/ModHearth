namespace ModHearth.Utils
{
    /// <summary>
    /// Math helpers
    /// </summary>
    public static class MMath
    {
        public const float epsilon = 0.0001f;
        public static bool Equals(this double value, double other, float epsilon = epsilon) => Math.Abs(value - other) < epsilon;
        public static bool Equals(this float value, float other, float epsilon = epsilon) => MathF.Abs(value - other) < epsilon;

        /// <summary>
        /// An <see cref="IEqualityComparer{T}"/> for double values using a tolerance epsilon.
        /// </summary>
        public sealed class ToleranceComparer(double tolerance) : IEqualityComparer<double>
        {
            public bool Equals(double x, double y) => Math.Abs(x - y) < tolerance;

            public int GetHashCode(double obj) => 0;
        }
    }
}
