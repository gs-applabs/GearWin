using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Threading;
using System.Threading.Tasks;
using System.Text;

namespace OptimizerWpf.Services
{
    public static class BackupService
    {
        private static readonly SemaphoreSlim BackupGate = new(1, 1);

        public static async Task<(bool Success, string Output)> StartSystemImageBackupAsync(string targetDrive)
        {
            if (!IsDriveLetter(targetDrive))
                return (false, "Select a local backup volume.");

            if (!await BackupGate.WaitAsync(0))
                return (false, "A system image backup is already running.");

            try
            {
                using var lease = await GearWinCore.EnterAsync(CoreWork.Backup);
                if (!await Task.Run(() => IsSeparateBackupDisk(targetDrive)))
                    return (false, "The backup volume must be on a separate physical disk from the Windows and boot partitions.");

                // Capture a baseline. A readable catalog may contain only older images.
                var previous = await RunAsync("get", "versions", $"-backupTarget:{targetDrive}");
                var backup = await RunAsync("start", "backup", $"-backupTarget:{targetDrive}", "-allCritical", "-vssCopy", "-quiet");
                if (!backup.Success) return backup;
                var catalog = await RunAsync("get", "versions", $"-backupTarget:{targetDrive}");
                if (!catalog.Success || string.IsNullOrWhiteSpace(catalog.Output))
                    return (false, "Windows Backup reported success but the image catalog cannot be read. " +
                        "Do not assume recovery is possible. " + catalog.Output);
                if (!HasNewCatalogVersion(previous.Success ? previous.Output : null, catalog.Output))
                    return (false, "Backup completed but a NEW catalog version could not be verified. " +
                        "Do not assume this run produced a restorable image. " + catalog.Output);
                return (true, "A NEW Windows Backup catalog version was detected. " +
                    "Actual Windows Recovery Environment restore/UEFI boot is not yet validated.\n" + catalog.Output);
            }
            catch (Exception ex) when (ex is ManagementException or IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
            {
                return (false, ex.Message);
            }
            finally
            {
                BackupGate.Release();
            }
        }

        public static Task<(bool Success, string Output)> GetBackupVersionsAsync(string targetDrive) =>
            IsDriveLetter(targetDrive)
                ? RunAsync("get", "versions", $"-backupTarget:{targetDrive}")
                : Task.FromResult((false, "Select a local backup volume."));

        private static bool IsDriveLetter(string drive) =>
            drive.Length == 2 && char.IsAsciiLetter(drive[0]) && drive[1] == ':';

        private static bool IsSeparateBackupDisk(string targetDrive)
        {
            var volume = new DriveInfo(targetDrive);
            if (!volume.IsReady || volume.DriveType is not (DriveType.Fixed or DriveType.Removable))
                return false;

            using var searcher = new ManagementObjectSearcher(@"root\Microsoft\Windows\Storage",
                "SELECT DiskNumber, DriveLetter, IsBoot, IsSystem FROM MSFT_Partition");
            using var partitions = searcher.Get();
            var criticalDisks = new HashSet<uint>();
            uint? targetDisk = null;
            bool bootFound = false, systemFound = false;
            foreach (ManagementObject partition in partitions)
            {
                using (partition)
                {
                    if (partition["DiskNumber"] is not uint disk)
                        continue;

                    bool boot = partition["IsBoot"] is true;
                    bool system = partition["IsSystem"] is true;
                    bootFound |= boot;
                    systemFound |= system;
                    if (boot || system)
                        criticalDisks.Add(disk);

                    char letter = partition["DriveLetter"] switch
                    {
                        char value => value,
                        ushort value => (char)value,
                        string { Length: 1 } value => value[0],
                        _ => '\0',
                    };
                    if (char.ToUpperInvariant(letter) == char.ToUpperInvariant(targetDrive[0]))
                    {
                        if (targetDisk is not null)
                            return false;
                        targetDisk = disk;
                    }
                }
            }
            return bootFound && systemFound && targetDisk is uint number && !criticalDisks.Contains(number);
        }

        // Pure helper: compare before/after catalogs, not only nonempty output. Localization
        // of wbadmin output differs across languages, so equality is intentionally generic.
        public static bool HasNewCatalogVersion(string? before, string? after)
        {
            if (string.IsNullOrWhiteSpace(after)) return false;
            static string Normalize(string value) => string.Join("\n", value.Split('\n')
                .Select(line => line.Trim()).Where(line => line.Length > 0));
            return string.IsNullOrWhiteSpace(before) ||
                !string.Equals(Normalize(before), Normalize(after), StringComparison.OrdinalIgnoreCase);
        }

        private static async Task<(bool Success, string Output)> RunAsync(params string[] arguments)
        {
            var startInfo = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "wbadmin.exe"))
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            foreach (string argument in arguments)
                startInfo.ArgumentList.Add(argument);

            try
            {
                using var process = Process.Start(startInfo);
                if (process is null) return (false, "Windows Backup could not be started.");
                // Bounded capture prevents large backup status logs from consuming RAM.
                static async Task<string> ReadBoundedAsync(StreamReader reader)
                {
                    const int maxCharacters = 65536;
                    var buffer = new char[4096];
                    var output = new StringBuilder();
                    bool truncated = false;
                    int count;
                    while ((count = await reader.ReadAsync(buffer.AsMemory())) != 0)
                    {
                        int allowed = Math.Min(count, maxCharacters - output.Length);
                        if (allowed > 0) output.Append(buffer, 0, allowed);
                        if (allowed < count) truncated = true;
                    }
                    if (truncated) output.Append("\n[Output truncated; see Windows event logs for details.]");
                    return output.ToString();
                }
                var stdout = ReadBoundedAsync(process.StandardOutput);
                var stderr = ReadBoundedAsync(process.StandardError);
                bool isBackup = arguments.Length >= 2 && arguments[0] == "start" && arguments[1] == "backup";
                if (isBackup)
                {
                    // A backup can legitimately take hours. Do not force-kill live imaging.
                    await process.WaitForExitAsync();
                }
                else
                {
                    using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(90));
                    try { await process.WaitForExitAsync(deadline.Token); }
                    catch (OperationCanceledException)
                    {
                        if (!process.HasExited) process.Kill(entireProcessTree: true);
                        await process.WaitForExitAsync();
                        await Task.WhenAll(stdout, stderr);
                        return (false, "Windows Backup metadata query exceeded 90 seconds.");
                    }
                }
                var outputs = await Task.WhenAll(stdout, stderr);
                return (process.ExitCode == 0, string.Join("\n", outputs).Trim());
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                return (false, ex.Message);
            }
        }
    }
}
