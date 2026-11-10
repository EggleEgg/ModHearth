using Xunit;

namespace ModHearth.App.Tests
{
    /// <summary>
    /// Serializes the Avalonia UI tests with the rest of the suite. They mutate process-wide
    /// <see cref="Models.ConfigManager"/> state, so they must not race other tests.
    /// </summary>
    [CollectionDefinition(Name, DisableParallelization = true)]
    public sealed class AvaloniaUiTestCollection
    {
        public const string Name = "Avalonia UI";
    }
}
