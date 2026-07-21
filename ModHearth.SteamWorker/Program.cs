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
/// Usage: ModHearth.SteamWorker &lt;action&gt; &lt;workshopId&gt;
///   subscribe;      -- SteamUGC.SubscribeItem, waits for the callback
///   unsubscribe;    -- SteamUGC.UnsubscribeItem, waits for the callback
///   download;       -- SteamUGC.DownloadItem (highPriority), fire-and-forget
///   issubscribed;   -- SteamUGC.GetItemState / GetSubscribedItems, synchronous
///
/// Exit code 0 = success, 1 = failure (details on stderr).
/// </summary>
internal static class Program
{
    private const string DwarfFortressSteamAppId = "975370";
    private static readonly TimeSpan CallbackWaitTimeout = TimeSpan.FromSeconds(15);

    private static int Main(string[] args)
    {
        if (args.Length < 2)
            return Fail("Usage: ModHearth.SteamWorker <subscribe|unsubscribe|download|issubscribed> <workshopId>");

        string action = args[0].Trim().ToLowerInvariant();
        if (!ulong.TryParse(args[1], out ulong rawId))
            return Fail($"Invalid workshop id: '{args[1]}'");

        PublishedFileId_t id = new PublishedFileId_t(rawId);
        string appIdFilePath = Path.Combine(AppContext.BaseDirectory, "steam_appid.txt");

        try
        {
            File.WriteAllText(appIdFilePath, DwarfFortressSteamAppId);

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
        finally
        {
            // Same reasoning as before: this file is only ever consulted during SteamAPI_Init()'s
            // handshake. Deleting it right after, on top of this whole process exiting a moment
            // later anyway, leaves essentially nothing on disk for any scan to catch.
            try { if (File.Exists(appIdFilePath)) File.Delete(appIdFilePath); } catch { /* best effort */ }
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
        return SteamUGC.DownloadItem(id, true)
            ? Success()
            : Fail("SteamUGC.DownloadItem returned false.");
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

    private static void WaitForCallback(Func<bool> isDone)
    {
        DateTime start = DateTime.UtcNow;
        while (!isDone() && (DateTime.UtcNow - start) < CallbackWaitTimeout)
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