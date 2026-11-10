using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using ModHearth.Models;
using ModHearth.UI;
using ModHearth.UI.ViewModels;
using Xunit;

namespace ModHearth.App.Tests
{
    /// <summary>
    /// Verifies the cache-only source category renders through the shared mod row control.
    /// </summary>
    [Collection(AvaloniaUiTestCollection.Name)]
    public class ModRefControlCacheOnlyTests
    {
        [AvaloniaFact]
        public void CacheOnlySourceIconFollowsViewModelClassification()
        {
            ModReference modref = new()
            {
                ID = "cache_only_mod",
                Name = "Cache Only Mod",
                Path = Path.Combine(Path.GetTempPath(), "installed_mods", "123 (10)")
            };
            ModRefViewModel vm = new(modref) { IsCacheOnlyModSource = true };

            ModRefControl control = new() { DataContext = vm };

            Image? icon = control.FindControl<Image>("CacheOnlySourceIcon");
            Assert.NotNull(icon);
            Assert.True(icon.IsVisible);

            vm.IsCacheOnlyModSource = false;
            Assert.False(icon.IsVisible);

            vm.IsCacheOnlyModSource = true;
            Assert.True(icon.IsVisible);
        }
    }
}
