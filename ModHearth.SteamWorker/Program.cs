using System;
using System.IO;
using System.Threading;
using Steamworks;

namespace ModHearth.SteamWorker;

/// <summary>
/// Standalone helper process that owns every direct Steamworks.NET call for App 975370 (Dwarf
/// Fortress's AppId). ModHearth.exe never links against Steamworks.NET and never calls
/// SteamAPI_Init itself -- it just spawns this process per action and reads its exit code. Because
/// Steam's own process/AppId association is keyed off whichever process actually made the
/// SteamAPI_Init call, keeping that call entirely inside this short-lived, single-purpose process
/// means ModHearth.exe itself never has anything for Steam to (mis)attribute App 975370's "running"
/// state to -- not "closed sooner", structurally absent.
///
/// Usage: ModHearth.SteamWorker <action> <workshopId>
///   subscribe;      -- SteamUGC.SubscribeItem, waits for the callback
///   unsubscribe;    -- SteamUGC.UnsubscribeItem, waits for the callback
///   download;       -- SteamUGC.DownloadItem (highPriority), waits for callback & reports progress
///   issubscribed;   -- SteamUGC.GetItemState / GetSubscribedItems, synchronous
///
/// Exit code 0 = success, 1 = failure (details on stderr).
/// </summary>
internal static class Program
{
    private const string DwarfFortressSteamAppId = "975370";
    private static readonly TimeSpan CallbackWaitTimeout = TimeSpan.FromSeconds(15);
    // Actual content downloads (unlike subscribe/unsubscribe, which are near-instant metadata
    // operations) can legitimately take minutes for larger mods or when several are sharing
    // bandwidth, so RunDownload gets a much longer budget before giving up. If this changes, keep
    // SteamWorkshopService.DownloadWorkerTimeout longer than this -- otherwise the parent process
    // kills this worker before it ever gets a chance to report whether the download finished.
    private static readonly TimeSpan DownloadCallbackWaitTimeout = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan ProgressReportInterval = TimeSpan.FromMilliseconds(500);

    private static int Main(string[] args)
    {
        if (args.Length < 2)
            return Fail("Usage: ModHearth.SteamWorker <subscribe|unsubscribe|download|issubscribed> <workshopId>");

        string action = args[0].Trim().ToLowerInvariant();
        if (!ulong.TryParse(args[1], out ulong rawId))
            return Fail($"Invalid workshop id: '{args[1]}'");

        PublishedFileId_t id = new PublishedFileId_t(rawId);

        try
        {
            if (!SteamAPI.Init())
                return Fail("SteamAPI.Init() failed. Is Steam running and logged in?");

            try
            {
                return action switch
                {
                    "subscribe" => RunSubscribe(id),
                    "unsubscribe" => RunUnsubscribe(id),
                    "download" => RunDownload(id),
                    "issubscribed" => RunIsSubscribed(id),
                    _ => Fail($"Unknown action '{action}'.")
                };
            }
            finally
            {
                SteamAPI.Shutdown();
            }
        }
        catch (Exception ex)
        {
            return Fail(ex.Message);
        }
    }


    private static int RunSubscribe(PublishedFileId_t id)
    {
        SteamAPICall_t call = SteamUGC.SubscribeItem(id);
        if (call == SteamAPICall_t.Invalid)
            return Fail("SteamUGC.SubscribeItem returned an invalid call handle.");

        bool completed = false;
        EResult result = EResult.k_EResultFail;

        using CallResult<RemoteStorageSubscribePublishedFileResult_t> callResult =
            CallResult<RemoteStorageSubscribePublishedFileResult_t>.Create((res, failure) =>
            {
                completed = true;
                if (!failure)
                    result = res.m_eResult;
            });
        callResult.Set(call);

        WaitForCallback(() => completed);

        return completed && result == EResult.k_EResultOK
            ? Success()
            : Fail($"Subscribe did not complete successfully (result: {result}).");
    }

    private static int RunUnsubscribe(PublishedFileId_t id)
    {
        SteamAPICall_t call = SteamUGC.UnsubscribeItem(id);
        if (call == SteamAPICall_t.Invalid)
            return Fail("SteamUGC.UnsubscribeItem returned an invalid call handle.");

        bool completed = false;
        EResult result = EResult.k_EResultFail;

        using CallResult<RemoteStorageUnsubscribePublishedFileResult_t> callResult =
            CallResult<RemoteStorageUnsubscribePublishedFileResult_t>.Create((res, failure) =>
            {
                completed = true;
                if (!failure)
                    result = res.m_eResult;
            });
        callResult.Set(call);

        WaitForCallback(() => completed);

        return completed && result == EResult.k_EResultOK
            ? Success()
            : Fail($"Unsubscribe did not complete successfully (result: {result}).");
    }

    private static int RunDownload(PublishedFileId_t id)
    {
        // DownloadItem only *starts* the download/update; a `true` return means the request was
        // accepted, not that the content is on disk. Steam's docs are explicit: register and wait
        // for DownloadItemResult_t before touching the item on disk.
        if (!SteamUGC.DownloadItem(id, true))
            return Fail("SteamUGC.DownloadItem returned false (invalid id, or not logged in).");

        AppId_t runningAppId = SteamUtils.GetAppID();
        bool completed = false;
        EResult result = EResult.k_EResultFail;

        // DownloadItemResult_t is a global callback -- per Steam's docs it fires for every item
        // download completion regardless of which app requested it, so both the app id and the
        // published file id must be checked before treating a callback as "ours."
        using Callback<DownloadItemResult_t> callback = Callback<DownloadItemResult_t>.Create(res =>
        {
            if (res.m_unAppID != runningAppId || res.m_nPublishedFileId != id)
                return;

            completed = true;
            result = res.m_eResult;
        });

        if (!WaitForDownloadCompletion(id, () => completed, DownloadCallbackWaitTimeout))
            return Fail($"Timed out after {DownloadCallbackWaitTimeout.TotalMinutes:0} minute(s) waiting for the download to complete.");

        return result == EResult.k_EResultOK
            ? Success()
            : Fail($"Download did not complete successfully (result: {result}).");
    }

    // Like WaitForCallback, but also polls and reports real download progress on stdout as
    // "PROGRESS <bytesDownloaded> <bytesTotal>" lines, since GetItemDownloadInfo is only meaningful
    // while a download is actively in flight. Consumed by SteamWorkshopService.RunWorker on the
    // parent process side; throttled independently of the callback-pump cadence to avoid flooding
    // stdout on long downloads.
    private static bool WaitForDownloadCompletion(PublishedFileId_t id, Func<bool> isDone, TimeSpan timeout)
    {
        DateTime start = DateTime.UtcNow;
        DateTime nextProgressReportUtc = DateTime.UtcNow;

        while (!isDone() && (DateTime.UtcNow - start) < timeout)
        {
            SteamAPI.RunCallbacks();

            if (DateTime.UtcNow >= nextProgressReportUtc &&
                SteamUGC.GetItemDownloadInfo(id, out ulong bytesDownloaded, out ulong bytesTotal) &&
                bytesTotal > 0)
            {
                Console.WriteLine($"PROGRESS {bytesDownloaded} {bytesTotal}");
                nextProgressReportUtc = DateTime.UtcNow.Add(ProgressReportInterval);
            }

            Thread.Sleep(50);
        }

        return isDone();
    }
    
    private static int RunIsSubscribed(PublishedFileId_t id)
    {
        uint state = SteamUGC.GetItemState(id);
        if (state != 0)
            return (state & (uint)EItemState.k_EItemStateSubscribed) != 0 ? Success() : Fail("Not subscribed.");

        uint count = SteamUGC.GetNumSubscribedItems();
        if (count == 0)
            return Fail("Not subscribed.");

        PublishedFileId_t[] items = new PublishedFileId_t[count];
        uint actualCount = SteamUGC.GetSubscribedItems(items, count);
        for (int i = 0; i < actualCount; i++)
        {
            if (items[i].m_PublishedFileId == id.m_PublishedFileId)
                return Success();
        }

        return Fail("Not subscribed.");
    }

    private static void WaitForCallback(Func<bool> isDone, TimeSpan? timeout = null)
    {
        TimeSpan effectiveTimeout = timeout ?? CallbackWaitTimeout;
        DateTime start = DateTime.UtcNow;
        while (!isDone() && (DateTime.UtcNow - start) < effectiveTimeout)
        {
            SteamAPI.RunCallbacks();
            Thread.Sleep(50);
        }
    }

    private static int Success() => 0;

    private static int Fail(string message)
    {
        Console.Error.WriteLine(message);
        return 1;
    }
}
