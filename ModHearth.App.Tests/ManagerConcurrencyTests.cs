using System.Collections.Concurrent;
using ModHearth.Utilities;
using Xunit;

namespace ModHearth.App.Tests;

/// <summary>
/// Multithreaded stress tests. If your machine is slow, you may need to increase timeout
/// </summary>
public class ManagerConcurrencyTests
{
    [Fact(Timeout = 60000)]
    public async Task ConcurrentManagerOperationsDoNotThrow()
    {
        ModHearthManager manager = new();
        _ = manager.Initialize(); // one real baseline call, not inside the hammer loop

        const int cheapIterations = 2000;
        const int expensiveIterations = 500;
        const int initializeIterations = 5; // keep this low, it's expensive by design, not by bug

        Exception? captured = null;
        object captureLock = new();

        void Hammer(Action action, int iterations)
        {
            for (int i = 0; i < iterations; i++)
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    lock (captureLock)
                        captured ??= ex;
                    return;
                }
            }
        }

        await Task.Run(() => Parallel.Invoke(
            () => Hammer(() => manager.Initialize(), initializeIterations),
            () => Hammer(() => manager.SetActiveMods([.. manager.enabledMods]), cheapIterations),
            () => Hammer(() => manager.GetInstalledCacheModIds(), cheapIterations),
            () => Hammer(() => manager.RefreshInstalledCacheModIds(), cheapIterations),
            () => Hammer(() => manager.FindModlistProblems(), cheapIterations),
            () => Hammer(() => manager.ModSortEnabledMods(), cheapIterations),
            () => Hammer(() => manager.FindAllModsFromDisk(), expensiveIterations),
            () => Hammer(() => manager.EnsureModRawDependencyCacheAsync().GetAwaiter().GetResult(), expensiveIterations)
        ));

        Assert.Null(captured);
    }

    [Fact]
    public void BatchDeletionNotificationBatchesBySourceFolderConcurrently()
    {
        ConcurrentBag<string> notifications = [];
        using (var scope = new ModDeletionBatchScope((msg, icon) => notifications.Add(msg)))
        {
            Parallel.For(0, 100, i =>
            {
                string sourceFolder = i < 50 ? "C:/mods/folderA" : "C:/mods/folderB";
                string itemPath = $"C:/mods/folderA/mod{i}";
                ModDeletionNotifier.NotifyDeleted((m, ic) => notifications.Add(m), itemPath, sourceFolder);
            });
        }

        Assert.Equal(2, notifications.Count);
        Assert.Contains(notifications, n => n.Contains("Deleted 50 mod folders from folderA") || n.Contains("Deleted 50 mod folders"));
        Assert.Contains(notifications, n => n.Contains("Deleted 50 mod folders from folderB") || n.Contains("Deleted 50 mod folders"));
    }
}
