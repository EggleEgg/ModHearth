using System.Runtime.CompilerServices;
using ModHearth.Models;

namespace ModHearth.Utils.Logging
{
    [InterpolatedStringHandler]
    internal ref struct DevLogHandler
    {
        private DefaultInterpolatedStringHandler inner;
        public bool IsEnabled { get; }

        public DevLogHandler(int literalLength, int formattedCount, out bool shouldAppend)
        {
            IsEnabled = shouldAppend = DevMode.IsEnabled;
            inner = shouldAppend ? new DefaultInterpolatedStringHandler(literalLength, formattedCount) : default;
        }

        public void AppendLiteral(string value) => inner.AppendLiteral(value);
        public void AppendFormatted<T>(T value) => inner.AppendFormatted(value);
        public void AppendFormatted<T>(T value, string? format) => inner.AppendFormatted(value, format);
        public void AppendFormatted<T>(T value, int alignment) => inner.AppendFormatted(value, alignment);
        public void AppendFormatted<T>(T value, int alignment, string? format) => inner.AppendFormatted(value, alignment, format);
        public string ToStringAndClear() => IsEnabled ? inner.ToStringAndClear() : string.Empty;
    }
}
