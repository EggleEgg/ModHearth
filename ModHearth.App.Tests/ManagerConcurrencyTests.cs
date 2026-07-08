using System.Threading.Tasks;
using Xunit;

namespace ModHearth.App.Tests;

/// <summary>
/// Multithreaded stress tests. If your machine is slow, you may need to increase timeout
/// </summary>
public class ManagerConcurrencyTests
{
    [Fact(Timeout = 60000)]
    public async Task ConcurrentManagerOperations_DoNotThrow()
    {
        ModHearthManager manager = new ModHearthManager();
        manager.Initialize(); // one real baseline call, not inside the hammer loop

        const int cheapIterations = 2000;
        const int expensiveIterations = 500;
        const int initializeIterations = 5; // keep this low — it's expensive by design, not by bug

        Exception? captured = null;
        object captureLock = new object();

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
            () => Hammer(() => manager.SetActiveMods(new List<DFHMod>(manager.enabledMods)), cheapIterations),
            () => Hammer(() => manager.GetInstalledCacheModIds(), cheapIterations),
            () => Hammer(() => manager.RefreshInstalledCacheModIds(), cheapIterations),
            () => Hammer(() => manager.FindModlistProblems(), cheapIterations),
            () => Hammer(() => manager.AutoSortEnabledMods(), cheapIterations),
            () => Hammer(() => manager.FindAllModsFromDisk(), expensiveIterations),
            () => Hammer(() => manager.EnsureModRawDependencyCacheAsync().GetAwaiter().GetResult(), expensiveIterations)
        ));

        Assert.Null(captured);
    }
}
