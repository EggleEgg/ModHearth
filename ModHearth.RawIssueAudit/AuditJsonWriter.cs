using System.Text.Json;
using System.Text.Json.Serialization;

namespace ModHearth.RawIssueAudit
{
    internal static class AuditJsonWriter
    {
        private static readonly JsonSerializerOptions Options = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
        };

        public static void Write(AuditReport report, string path)
        {
            string fullPath = Path.GetFullPath(path);
            string? directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrWhiteSpace(directory))
                _ = Directory.CreateDirectory(directory);

            File.WriteAllText(fullPath, JsonSerializer.Serialize(report, Options));
        }
    }
}
