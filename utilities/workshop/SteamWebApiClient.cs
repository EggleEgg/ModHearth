using System.Text.Json;
using ModHearth.Utilities.Logging;

namespace ModHearth.Utilities.Workshop
{
    public class WorkshopItemMetadata
    {
        public ulong PublishedFileId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string PreviewUrl { get; set; } = string.Empty;
        //Not fetched correctly, as it needs either the steam api or web api key (ISteamUser/GetPlayerSummaries)
        public string Author { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public DateTime UpdatedAt { get; set; }
        public string Description { get; set; } = string.Empty;
        public bool IsCollection { get; set; }
        public List<ulong> ChildrenIds { get; set; } = [];
    }

    /// <summary>
    /// A simple client for the Steam Web API to fetch workshop item details and collection details.
    /// </summary>
    public static class SteamWebApiClient
    {
        private static readonly HttpClient HttpClient = new();
        private const string PublishedFileDetailsUrl = "https://api.steampowered.com/ISteamRemoteStorage/GetPublishedFileDetails/v1/";
        private const string CollectionDetailsUrl = "https://api.steampowered.com/ISteamRemoteStorage/GetCollectionDetails/v1/";

        public static async Task<List<WorkshopItemMetadata>> GetPublishedFileDetailsAsync(IEnumerable<ulong> ids)
        {
            List<ulong> idList = [.. ids];
            if (idList.Count == 0) return [];

            FormUrlEncodedContent content = new(
                new[] { new KeyValuePair<string, string>("itemcount", idList.Count.ToString()) }
                .Concat(idList.Select((id, i) => new KeyValuePair<string, string>($"publishedfileids[{i}]", id.ToString())))
            );

            try
            {
                Loggers.Workshop.Log($"SteamWebApiClient: Fetching details for {idList.Count} IDs...");
                HttpResponseMessage response = await HttpClient.PostAsync(PublishedFileDetailsUrl, content);
                _ = response.EnsureSuccessStatusCode();
                string json = await response.Content.ReadAsStringAsync();

                Loggers.Workshop.Dev($"SteamWebApiClient: Received JSON: {json}");

                using JsonDocument doc = JsonDocument.Parse(json);
                JsonElement root = doc.RootElement.GetProperty("response");
                if (!root.TryGetProperty("publishedfiledetails", out JsonElement detailsArray))
                {
                    Loggers.Workshop.Log("SteamWebApiClient: No 'publishedfiledetails' property found in response.");
                    return [];
                }

                List<WorkshopItemMetadata> results = [];
                foreach (JsonElement detail in detailsArray.EnumerateArray())
                {
                    int resultValue = GetIntProperty(detail, "result");
                    if (resultValue != 1)
                    {
                        ulong rawId = GetUlongProperty(detail, "publishedfileid");
                        Loggers.Workshop.Log($"SteamWebApiClient: Item {rawId} returned result {resultValue}");
                        continue;
                    }

                    WorkshopItemMetadata meta = new()
                    {
                        PublishedFileId = GetUlongProperty(detail, "publishedfileid"),
                        Title = detail.TryGetProperty("title", out JsonElement t) ? t.GetString() ?? string.Empty : string.Empty,
                        PreviewUrl = detail.TryGetProperty("preview_url", out JsonElement p) ? p.GetString() ?? string.Empty : string.Empty,
                        FileSize = GetLongProperty(detail, "file_size"),
                        UpdatedAt = DateTimeOffset.FromUnixTimeSeconds(GetLongProperty(detail, "time_updated")).DateTime,
                        Description = detail.TryGetProperty("description", out JsonElement d) ? d.GetString() ?? string.Empty : string.Empty,
                        IsCollection = false
                    };
                    Loggers.Workshop.Log($"SteamWebApiClient: Parsed metadata for '{meta.Title}' ({meta.PublishedFileId})");
                    results.Add(meta);
                }
                return results;
            }
            catch (Exception ex)
            {
                Loggers.Workshop.Log($"SteamWebApiClient: Error fetching file details: {ex.Message}");
                return [];
            }
        }

        public static async Task<List<ulong>> GetCollectionDetailsAsync(ulong collectionId)
        {
            Loggers.Workshop.Log($"SteamWebApiClient: Fetching collection details for {collectionId}...");
            FormUrlEncodedContent content = new(
                [
                    new KeyValuePair<string, string>("collectioncount", "1"),
                    new KeyValuePair<string, string>("publishedfileids[0]", collectionId.ToString())
                ]);

            try
            {
                HttpResponseMessage response = await HttpClient.PostAsync(CollectionDetailsUrl, content);
                _ = response.EnsureSuccessStatusCode();
                string json = await response.Content.ReadAsStringAsync();

                Loggers.Workshop.Log($"SteamWebApiClient: Collection JSON: {json}");

                using JsonDocument doc = JsonDocument.Parse(json);
                JsonElement root = doc.RootElement.GetProperty("response");
                if (!root.TryGetProperty("collectiondetails", out JsonElement collections))
                {
                    Loggers.Workshop.Log("SteamWebApiClient: No 'collectiondetails' property found in response.");
                    return [];
                }

                JsonElement collection = collections.EnumerateArray().FirstOrDefault();
                if (collection.ValueKind == JsonValueKind.Undefined || GetIntProperty(collection, "result") != 1)
                {
                    Loggers.Workshop.Log($"SteamWebApiClient: Collection {collectionId} result is not 1 or undefined.");
                    return [];
                }

                if (!collection.TryGetProperty("children", out JsonElement children))
                {
                    Loggers.Workshop.Log($"SteamWebApiClient: Collection {collectionId} has no children.");
                    return [];
                }

                List<ulong> childrenIds = [.. children.EnumerateArray()
                    .Select(c => GetUlongProperty(c, "publishedfileid"))
                    .Where(id => id != 0)];

                Loggers.Workshop.Log($"SteamWebApiClient: Found {childrenIds.Count} children for collection {collectionId}");
                return childrenIds;
            }
            catch (Exception ex)
            {
                Loggers.Workshop.Log($"SteamWebApiClient: Error fetching collection details: {ex.Message}");
                return [];
            }
        }

        private static ulong GetUlongProperty(JsonElement element, string propertyName, ulong defaultValue = 0)
        {
            if (element.TryGetProperty(propertyName, out JsonElement prop))
            {
                switch (prop.ValueKind)
                {
                    case JsonValueKind.Number:
                        {
                            if (prop.TryGetUInt64(out ulong val)) return val;
                            break;
                        }

                    case JsonValueKind.String:
                        {
                            if (ulong.TryParse(prop.GetString(), out ulong val)) return val;
                            break;
                        }

                }
            }
            return defaultValue;
        }

        private static long GetLongProperty(JsonElement element, string propertyName, long defaultValue = 0)
        {
            if (element.TryGetProperty(propertyName, out JsonElement prop))
            {
                switch (prop.ValueKind)
                {
                    case JsonValueKind.Number:
                        {
                            if (prop.TryGetInt64(out long val)) return val;
                            break;
                        }

                    case JsonValueKind.String:
                        {
                            if (long.TryParse(prop.GetString(), out long val)) return val;
                            break;
                        }

                }
            }
            return defaultValue;
        }

        private static int GetIntProperty(JsonElement element, string propertyName, int defaultValue = 0)
        {
            if (element.TryGetProperty(propertyName, out JsonElement prop))
            {
                switch (prop.ValueKind)
                {
                    case JsonValueKind.Number:
                        {
                            if (prop.TryGetInt32(out int val)) return val;
                            break;
                        }

                    case JsonValueKind.String:
                        {
                            if (int.TryParse(prop.GetString(), out int val)) return val;
                            break;
                        }

                }
            }
            return defaultValue;
        }
    }
}
