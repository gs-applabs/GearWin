using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Threading;
using System.Threading.Tasks;

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

                var backup = await RunAsync("start", "backup", $"-backupTarget:{targetDrive}", "-allCritical", "-vssCopy", "-quiet");
                if (!backup.Success) return backup;
                var catalog = await RunAsync("get", "versions", $"-backupTarget:{targetDrive}");
                if (!catalog.Success || string.IsNullOrWhiteSpace(catalog.Output))
                    return (false, "Windows Backup reported success but the backup catalog could not be checked; " +
                        "do not assume the image can be restored. " + catalog.Output);
                return (true, "Windows Backup completed and the selected disk's backup catalog is readable. " +
                    "Bare-metal boot recovery is NOT validated.\n" + catalog.Output);
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
                if (process is null)
                    return (false, "Windows Backup could not be started.");
                var stdout = process.StandardOutput.ReadToEndAsync();
                var stderr = process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();
                return (process.ExitCode == 0, (await stdout + await stderr).Trim());
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                return (false, ex.Message);
            }
        }
    }
}
