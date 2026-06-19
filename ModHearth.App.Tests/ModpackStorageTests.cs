using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Xunit;

namespace ModHearth.Tests;

public class ModpackStorageTests
{
    [Fact]
    public void WriteModpackFile_Creates_Missing_DfhackConfig_Directory()
    {
        string root = Path.Combine(Path.GetTempPath(), "modhearth-tests", Guid.NewGuid().ToString("N"));
        try
        {
            string targetPath = Path.Combine(root, "dfhack-config", "mod-manager.json");
            var manager = new ModHearthManager();
            MethodInfo? writeMethod = typeof(ModHearthManager).GetMethod(
                "WriteModpackFile",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(writeMethod);

            List<DFHModpack> modpacks =
            [
                new DFHModpack(true, new List<DFHMod>(), "Default")
            ];

            writeMethod.Invoke(manager, new object[] { targetPath, modpacks });

            Assert.True(Directory.Exists(Path.Combine(root, "dfhack-config")));
            Assert.True(File.Exists(targetPath));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void FindModpacks_Creates_Default_Modpack_When_Active_File_Missing()
    {
        string dfRoot = Path.Combine(Path.GetTempPath(), "modhearth-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dfRoot);

        try
        {
            var manager = new ModHearthManager();
            SetPrivateField(manager, "config", new ModHearthConfig { DFFolderPathOverride = dfRoot });

            MethodInfo? findModpacks = typeof(ModHearthManager).GetMethod(
                "FindModpacks",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(findModpacks);

            bool loaded = (bool)findModpacks.Invoke(manager, new object?[] { null })!;
            Assert.True(loaded);
            Assert.NotEmpty(manager.modpacks);
            Assert.NotNull(manager.SelectedModlist);
            Assert.Equal(ModHearthManager.ModpackStorageBackend.LocalFallback, manager.ActiveModpackBackend);
            Assert.True(File.Exists(manager.GetLocalFallbackModpacksPath()));
        }
        finally
        {
            string localPath = Path.Combine(AppContext.BaseDirectory, "modpacks.local.json");
            if (File.Exists(localPath))
                File.Delete(localPath);

            if (Directory.Exists(dfRoot))
                Directory.Delete(dfRoot, recursive: true);
        }
    }

    private static void SetPrivateField(object target, string fieldName, object value)
    {
        FieldInfo? field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        field.SetValue(target, value);
    }
}
