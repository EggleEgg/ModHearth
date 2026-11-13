namespace ModHearth.Utils.Thread
{
    /// <summary>
    /// Shared parallelization settings. <see cref="ParallelOptions"/> instances are safe to reuse as
    /// long as they are never mutated, so CPU-bound scans share one instance instead of rebuilding it.
    /// </summary>
    internal static class Parallelism
    {
        public static readonly ParallelOptions CpuBound = new()
        {
            MaxDegreeOfParallelism = Environment.ProcessorCount
        };
    }
}
