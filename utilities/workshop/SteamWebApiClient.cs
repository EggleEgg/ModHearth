using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using ModHearth.Utilities.Logging;

namespace ModHearth.Utilities.Workshop
{
    public class WorkshopItemMetadata
    {
        public ulong PublishedFileId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string PreviewUrl { get; set; } = string.Empty;
        public string Author { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public DateTime UpdatedAt { get; set; }
        public string Description { get; set; } = string.Empty;
        public bool IsCollection { get; set; }
        public List<ulong> ChildrenIds { get; set; } = new();
    }

    public class SteamWebApiClient
    {
        private static readonly HttpClient HttpClient = new HttpClient();
        private const string PublishedFileDetailsUrl = "https://api.steampowered.com/ISteamRemoteStorage/GetPublishedFileDetails/v1/";
        private const string CollectionDetailsUrl = "https://api.steampowered.com/ISteamRemoteStorage/GetCollectionDetails/v1/";

        public async Task<List<WorkshopItemMetadata>> GetPublishedFileDetailsAsync(IEnumerable<ulong> ids)
        {
            var idList = ids.ToList();
            if (idList.Count == 0) return new List<WorkshopItemMetadata>();

            var content = new FormUrlEncodedContent(
                new[] { new KeyValuePair<string, string>("itemcount", idList.Count.ToString()) }
                .Concat(idList.Select((id, i) => new KeyValuePair<string, string>($"publishedfileids[{i}]", id.ToString())))
            );

            try
            {
                if (DevMode.IsEnabled) InfoLogger.LogRunDf($"SteamWebApiClient: Fetching details for {idList.Count} IDs...");
                var response = await HttpClient.PostAsync(PublishedFileDetailsUrl, content);
                response.EnsureSuccessStatusCode();
                var json = await response.Content.ReadAsStringAsync();
                
                if (DevMode.IsEnabled) InfoLogger.LogRunDf($"SteamWebApiClient: Received JSON: {json}");

                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement.GetProperty("response");
                if (!root.TryGetProperty("publishedfiledetails", out var detailsArray))
                {
                    if (DevMode.IsEnabled) InfoLogger.LogRunDf("SteamWebApiClient: No 'publishedfiledetails' property found in response.");
                    return new List<WorkshopItemMetadata>();
                }

                var results = new List<WorkshopItemMetadata>();
                foreach (var detail in detailsArray.EnumerateArray())
                {
                    int resultValue = GetIntProperty(detail, "result");
                    if (resultValue != 1)
                    {
                        ulong rawId = GetUlongProperty(detail, "publishedfileid");
                        if (DevMode.IsEnabled) InfoLogger.LogRunDf($"SteamWebApiClient: Item {rawId} returned result {resultValue}");
                        continue;
                    }

                    var meta = new WorkshopItemMetadata
                    {
                        PublishedFileId = GetUlongProperty(detail, "publishedfileid"),
                        Title = detail.TryGetProperty("title", out var t) ? t.GetString() ?? string.Empty : string.Empty,
                        PreviewUrl = detail.TryGetProperty("preview_url", out var p) ? p.GetString() ?? string.Empty : string.Empty,
                        FileSize = GetLongProperty(detail, "file_size"),
                        UpdatedAt = DateTimeOffset.FromUnixTimeSeconds(GetLongProperty(detail, "time_updated")).DateTime,
                        Description = detail.TryGetProperty("description", out var d) ? d.GetString() ?? string.Empty : string.Empty,
                        IsCollection = false 
                    };
                    if (DevMode.IsEnabled) InfoLogger.LogRunDf($"SteamWebApiClient: Parsed metadata for '{meta.Title}' ({meta.PublishedFileId})");
                    results.Add(meta);
                }
                return results;
            }
            catch (Exception ex)
            {
                if (DevMode.IsEnabled) InfoLogger.LogRunDf($"SteamWebApiClient: Error fetching file details: {ex.Message}");
                return new List<WorkshopItemMetadata>();
            }
        }

        public async Task<List<ulong>> GetCollectionDetailsAsync(ulong collectionId)
        {
            if (DevMode.IsEnabled) InfoLogger.LogRunDf($"SteamWebApiClient: Fetching collection details for {collectionId}...");
            var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("collectioncount", "1"),
                new KeyValuePair<string, string>("publishedfileids[0]", collectionId.ToString())
            });

            try
            {
                var response = await HttpClient.PostAsync(CollectionDetailsUrl, content);
                response.EnsureSuccessStatusCode();
                var json = await response.Content.ReadAsStringAsync();

                if (DevMode.IsEnabled) InfoLogger.LogRunDf($"SteamWebApiClient: Collection JSON: {json}");

                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement.GetProperty("response");
                if (!root.TryGetProperty("collectiondetails", out var collections))
                {
                    if (DevMode.IsEnabled) InfoLogger.LogRunDf("SteamWebApiClient: No 'collectiondetails' property found in response.");
                    return new List<ulong>();
                }

                var collection = collections.EnumerateArray().FirstOrDefault();
                if (collection.ValueKind == JsonValueKind.Undefined || GetIntProperty(collection, "result") != 1)
                {
                    if (DevMode.IsEnabled) InfoLogger.LogRunDf($"SteamWebApiClient: Collection {collectionId} result is not 1 or undefined.");
                    return new List<ulong>();
                }

                if (!collection.TryGetProperty("children", out var children))
                {
                    if (DevMode.IsEnabled) InfoLogger.LogRunDf($"SteamWebApiClient: Collection {collectionId} has no children.");
                    return new List<ulong>();
                }

                var childrenIds = children.EnumerateArray()
                    .Select(c => GetUlongProperty(c, "publishedfileid"))
                    .Where(id => id != 0)
                    .ToList();
                
                if (DevMode.IsEnabled) InfoLogger.LogRunDf($"SteamWebApiClient: Found {childrenIds.Count} children for collection {collectionId}");
                return childrenIds;
            }
            catch (Exception ex)
            {
                if (DevMode.IsEnabled) InfoLogger.LogRunDf($"SteamWebApiClient: Error fetching collection details: {ex.Message}");
                return new List<ulong>();
            }
        }

        private static ulong GetUlongProperty(JsonElement element, string propertyName, ulong defaultValue = 0)
        {
            if (element.TryGetProperty(propertyName, out var prop))
            {
                if (prop.ValueKind == JsonValueKind.Number)
                {
                    if (prop.TryGetUInt64(out var val)) return val;
                }
                else if (prop.ValueKind == JsonValueKind.String)
                {
                    if (ulong.TryParse(prop.GetString(), out var val)) return val;
                }
            }
            return defaultValue;
        }

        private static long GetLongProperty(JsonElement element, string propertyName, long defaultValue = 0)
        {
            if (element.TryGetProperty(propertyName, out var prop))
            {
                if (prop.ValueKind == JsonValueKind.Number)
                {
                    if (prop.TryGetInt64(out var val)) return val;
                }
                else if (prop.ValueKind == JsonValueKind.String)
                {
                    if (long.TryParse(prop.GetString(), out var val)) return val;
                }
            }
            return defaultValue;
        }

        private static int GetIntProperty(JsonElement element, string propertyName, int defaultValue = 0)
        {
            if (element.TryGetProperty(propertyName, out var prop))
            {
                if (prop.ValueKind == JsonValueKind.Number)
                {
                    if (prop.TryGetInt32(out var val)) return val;
                }
                else if (prop.ValueKind == JsonValueKind.String)
                {
                    if (int.TryParse(prop.GetString(), out var val)) return val;
                }
            }
            return defaultValue;
        }
    }
}
