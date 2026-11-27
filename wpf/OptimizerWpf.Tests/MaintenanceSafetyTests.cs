using OptimizerWpf.Services;

namespace OptimizerWpf.Tests;

public class MaintenanceSafetyTests
{
    [Theory]
    [InlineData("\"C:\\Program Files\\Vendor\\app.exe\" /silent", @"C:\Program Files\Vendor\app.exe")]
    [InlineData(@"C:\Tools\app.exe /silent", @"C:\Tools\app.exe")]
    [InlineData(@"C:\Program Files\Vendor\app.exe /silent", null)]
    [InlineData(@"msiexec.exe /x {GUID}", null)]
    [InlineData(@"rundll32.exe example.dll,Run", null)]
    [InlineData("\"C:\\Tools\\app.exe", null)]
    public void RegistryCommandParsingDoesNotGuess(string command, string? expected)
        => Assert.Equal(expected, HealthCleanupService.GetExePath(command));

    [Theory]
    [InlineData(@"\\server\share\missing.exe")]
    [InlineData(@"relative.exe")]
    [InlineData(@"C:\")]
    [InlineData(@"C:\%GEARWIN_UNDEFINED_TEST_VAR%\missing.exe")]
    public void UncertainPathsAreNeverClassifiedAsMissing(string path)
        => Assert.False(HealthCleanupService.IsConfirmedMissingLocalPath(path));

    [Theory]
    [InlineData(true, 1, 1, true)]
    [InlineData(true, 0, 1, false)]
    [InlineData(false, 0, 1, false)]
    public void ProfileRestoreDoesNotOverwriteIndependentChanges(bool exists, int value, int applied, bool owned)
        => Assert.Equal(owned, PowerModeService.StillOwned(exists, value, applied));

    [Fact]
    public void GameSaveBackupVerifiesArchiveBeforeReplacing()
    {
        var temp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(temp);
        try
        {
            var saves = System.IO.Path.Combine(temp, "Saves");
            System.IO.Directory.CreateDirectory(saves);
            var save = System.IO.Path.Combine(saves, "game.sav");
            var archive = System.IO.Path.Combine(temp, "backup.zip");
            System.IO.File.WriteAllText(save, "new save");
            System.IO.File.WriteAllText(archive, "last good backup");
            using (var locked = System.IO.File.Open(save, System.IO.FileMode.Open, System.IO.FileAccess.ReadWrite, System.IO.FileShare.None))
            {
                Assert.False(GameSaveBackupService.BackupFolders(new[] { saves }, archive));
                Assert.Equal("last good backup", System.IO.File.ReadAllText(archive));
            }
            Assert.True(GameSaveBackupService.BackupFolders(new[] { saves }, archive));
            using var zip = System.IO.Compression.ZipFile.OpenRead(archive);
            using var reader = new System.IO.StreamReader(zip.GetEntry("Saves/game.sav")!.Open());
            Assert.Equal("new save", reader.ReadToEnd());
        }
        finally { System.IO.Directory.Delete(temp, true); }
    }

    [Fact]
    public void GameSaveBackupRefusesDestinationWithinSource()
    {
        var temp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(temp);
        try
        {
            var archive = System.IO.Path.Combine(temp, "backup.zip");
            System.IO.File.WriteAllText(archive, "old");
            Assert.False(GameSaveBackupService.BackupFolders(new[] { temp }, archive));
            Assert.Equal("old", System.IO.File.ReadAllText(archive));
        }
        finally { System.IO.Directory.Delete(temp, true); }
    }

    [Fact]
    public void CleanupPreservesRecentAndLockedFilesAndCountsRemovedBytes()
    {
        var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(root);
        try
        {
            var old = System.IO.Path.Combine(root, "old.txt");
            var recent = System.IO.Path.Combine(root, "recent.txt");
            var locked = System.IO.Path.Combine(root, "locked.txt");
            System.IO.File.WriteAllBytes(old, new byte[128]);
            System.IO.File.WriteAllBytes(recent, new byte[256]);
            System.IO.File.WriteAllBytes(locked, new byte[512]);
            System.IO.File.SetLastWriteTimeUtc(old, System.DateTime.UtcNow.AddDays(-10));
            System.IO.File.SetLastWriteTimeUtc(locked, System.DateTime.UtcNow.AddDays(-10));
            using (var hold = System.IO.File.Open(locked, System.IO.FileMode.Open, System.IO.FileAccess.ReadWrite, System.IO.FileShare.None))
                Assert.Equal(128L, MaintenancePaths.CleanFiles(root, System.TimeSpan.FromDays(7)));
            Assert.False(System.IO.File.Exists(old));
            Assert.True(System.IO.File.Exists(recent));
            Assert.True(System.IO.File.Exists(locked));
        }
        finally { System.IO.Directory.Delete(root, true); }
    }

    [Fact]
    public async System.Threading.Tasks.Task VaultRoundTripPreservesOriginalAndRefusesOverwrite()
    {
        var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(root);
        try
        {
            var source = System.IO.Path.Combine(root, "source");
            System.IO.Directory.CreateDirectory(source);
            System.IO.Directory.CreateDirectory(System.IO.Path.Combine(source, "empty"));
            var file = System.IO.Path.Combine(source, "file.txt");
            System.IO.File.WriteAllText(file, "original");
            await FolderLockService.LockFolderAsync(source, "fixture-password");
            var vault = source + FolderLockService.VaultExtension;
            Assert.True(System.IO.File.Exists(vault));
            Assert.Equal("original", System.IO.File.ReadAllText(file));
            await Assert.ThrowsAsync<System.IO.IOException>(() => FolderLockService.UnlockFolderAsync(vault, "fixture-password", source));
            var restore = System.IO.Path.Combine(root, "restored");
            await FolderLockService.UnlockFolderAsync(vault, "fixture-password", restore);
            Assert.Equal("original", System.IO.File.ReadAllText(System.IO.Path.Combine(restore, "file.txt")));
            Assert.True(System.IO.Directory.Exists(System.IO.Path.Combine(restore, "empty")));
            Assert.True(System.IO.File.Exists(vault));
        }
        finally { System.IO.Directory.Delete(root, true); }
    }

    [Fact]
    public async System.Threading.Tasks.Task MalformedVaultDoesNotCreateRestoreDestination()
    {
        var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(root);
        try
        {
            var vault = System.IO.Path.Combine(root, "bad.vault");
            System.IO.File.WriteAllBytes(vault, new byte[3]);
            var restore = System.IO.Path.Combine(root, "restore");
            await Assert.ThrowsAsync<System.IO.InvalidDataException>(() => FolderLockService.UnlockFolderAsync(vault, "fixture-password", restore));
            Assert.False(System.IO.Directory.Exists(restore));
        }
        finally { System.IO.Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData("Opera")]
    [InlineData("OperaGX")]
    public async System.Threading.Tasks.Task BrowserCleanupNeverDeletesProfileData(string browser)
    {
        var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(root);
        try
        {
            var cache = System.IO.Directory.CreateDirectory(System.IO.Path.Combine(root, "Cache")).FullName;
            var cached = System.IO.Path.Combine(cache, "old-cache");
            var bookmarks = System.IO.Path.Combine(root, "Bookmarks");
            var logins = System.IO.Path.Combine(root, "Login Data");
            foreach (var file in new[] { cached, bookmarks, logins })
            {
                System.IO.File.WriteAllBytes(file, new byte[64]);
                System.IO.File.SetLastWriteTimeUtc(file, System.DateTime.UtcNow.AddDays(-10));
            }
            var entry = new BrowserCacheEntry(browser, browser, new[] { root });
            Assert.Equal(64L, await HealthCleanupService.ClearBrowserCacheAsync(entry));
            Assert.False(System.IO.File.Exists(cached));
            Assert.True(System.IO.File.Exists(bookmarks));
            Assert.True(System.IO.File.Exists(logins));
        }
        finally { System.IO.Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData("ObsoleteMuiCache", @"HKCU\Software\Classes\Local Settings\Software\Microsoft\Windows\Shell\MuiCache", @"C:\Old\App.exe.FriendlyAppName", true)]
    [InlineData("OrphanedStartupEntry", @"HKCU\Software\Microsoft\Windows\CurrentVersion\Run", "App", false)]
    [InlineData("MissingUninstaller", @"HKLM\Software\Microsoft\Windows\CurrentVersion\Uninstall\App", null, false)]
    [InlineData("ObsoleteMuiCache", @"HKLM\Software\Classes\Local Settings\Software\Microsoft\Windows\Shell\MuiCache", @"C:\Old\App.exe.FriendlyAppName", false)]
    [InlineData("ObsoleteMuiCache", @"HKCU\Software\Classes\Local Settings\Software\Microsoft\Windows\Shell\MuiCache", "Unknown", false)]
    public void RegistryCleanerOnlyMutatesAllowlistedDisplayCache(string category, string path, string? name, bool expected)
        => Assert.Equal(expected, HealthCleanupService.CanDeleteFinding(new RegistryFinding(category, "", path, name)));

    [Fact]
    public void SettingsPersistenceSkipsIdenticalContent()
    {
        var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(root);
        try
        {
            var path = System.IO.Path.Combine(root, "settings.json");
            Assert.True(AppSettingsService.WriteIfChanged(path, "{}", null));
            System.IO.File.SetLastWriteTimeUtc(path, System.DateTime.UtcNow.AddDays(-1));
            var timestamp = System.IO.File.GetLastWriteTimeUtc(path);
            Assert.False(AppSettingsService.WriteIfChanged(path, "{}", "{}"));
            Assert.Equal(timestamp, System.IO.File.GetLastWriteTimeUtc(path));
            Assert.True(AppSettingsService.WriteIfChanged(path, "{\"Language\":\"el\"}", "{}"));
            Assert.Equal("{\"Language\":\"el\"}", System.IO.File.ReadAllText(path));
        }
        finally { System.IO.Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData(0L, 0L, false)]
    [InlineData(8589934592L, 4294967296L, false)]
    [InlineData(8589934592L, 268435456L, true)]
    [InlineData(17179869184L, 1073741824L, true)]
    public void MemoryMaintenanceRequiresRealPressure(long total, long available, bool expected)
        => Assert.Equal(expected, MemoryCleanerService.NeedsMaintenance(new MemoryCleanerService.MemoryStats(total, available, 0, 0, 0)));
}
