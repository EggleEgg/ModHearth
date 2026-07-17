using System;
using System.Linq;
using System.Threading;
using Steamworks;

namespace ModHearth.Utilities;

public sealed class SteamWorkshopService
{
    public bool IsAvailable =>
        SteamManager.Initialized;

    public bool Subscribe(ulong workshopId)
    {
        lock (SteamManager.Gate)
        {
            if (!IsAvailable)
                return false;

            PublishedFileId_t publishedFileId = new PublishedFileId_t(workshopId);
            SteamAPICall_t call = SteamUGC.SubscribeItem(publishedFileId);
            if (call == SteamAPICall_t.Invalid)
                return false;

            bool completed = false;
            EResult result = EResult.k_EResultFail;

            using CallResult<RemoteStorageSubscribePublishedFileResult_t> callResult = CallResult<RemoteStorageSubscribePublishedFileResult_t>.Create((res, failure) =>
            {
                completed = true;
                if (!failure)
                {
                    result = res.m_eResult;
                }
            });

            callResult.Set(call);

            // Wait up to 15 seconds, running callbacks periodically.
            DateTime start = DateTime.UtcNow;
            while (!completed && (DateTime.UtcNow - start).TotalSeconds < 15)
            {
                SteamAPI.RunCallbacks();
                Thread.Sleep(50);
            }

            return completed && result == EResult.k_EResultOK;
        }
    }

    public bool Unsubscribe(ulong workshopId)
    {
        lock (SteamManager.Gate)
        {
            if (!IsAvailable)
                return false;

            PublishedFileId_t publishedFileId = new PublishedFileId_t(workshopId);
            SteamAPICall_t call = SteamUGC.UnsubscribeItem(publishedFileId);
            if (call == SteamAPICall_t.Invalid)
                return false;

            bool completed = false;
            EResult result = EResult.k_EResultFail;

            using CallResult<RemoteStorageUnsubscribePublishedFileResult_t> callResult = CallResult<RemoteStorageUnsubscribePublishedFileResult_t>.Create((res, failure) =>
            {
                completed = true;
                if (!failure)
                {
                    result = res.m_eResult;
                }
            });

            callResult.Set(call);

            // Wait up to 15 seconds, running callbacks periodically.
            DateTime start = DateTime.UtcNow;
            while (!completed && (DateTime.UtcNow - start).TotalSeconds < 15)
            {
                SteamAPI.RunCallbacks();
                Thread.Sleep(50);
            }

            return completed && result == EResult.k_EResultOK;
        }
    }

    public bool Download(ulong workshopId, bool highPriority = true)
    {
        lock (SteamManager.Gate)
        {
            if (!IsAvailable)
                return false;

            return SteamUGC.DownloadItem(new PublishedFileId_t(workshopId), highPriority);
        }
    }

    public bool IsSubscribed(ulong workshopId)
    {
        lock (SteamManager.Gate)
        {
            if (!IsAvailable)
                return false;

            // Try bitmask check first
            uint state = SteamUGC.GetItemState(new PublishedFileId_t(workshopId));
            if (state != 0)
            {
                return (state & (uint)EItemState.k_EItemStateSubscribed) != 0;
            }

            // Fallback to list enumeration
            uint count = SteamUGC.GetNumSubscribedItems();
            if (count == 0)
                return false;

            PublishedFileId_t[] items = new PublishedFileId_t[count];
            uint actualCount = SteamUGC.GetSubscribedItems(items, count);

            return items.Take((int)actualCount).Any(x => x.m_PublishedFileId == workshopId);
        }
    }
}