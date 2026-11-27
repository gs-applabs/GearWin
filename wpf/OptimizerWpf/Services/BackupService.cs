using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace OptimizerWpf.Services
{
    public static class BackupService
    {
        private static readonly SemaphoreSlim BackupGate = new(1, 1);

        internal sealed record BackupTarget(string Drive, string VolumeGuid, uint DiskNumber, string Label);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetVolumeNameForVolumeMountPoint(string mount, StringBuilder volume, uint length);

        internal static string NormalizeDrive(string? drive)
        {
            if (drive == null || drive.Length != 2 || !char.IsAsciiLetter(drive[0]) || drive[1] != ':')
                throw new ArgumentException("Select a local backup volume.");
            return char.ToUpperInvariant(drive[0]) + ":";
        }

        internal static string VolumeArgument(string volume)
        {
            const string prefix = @"\\?\Volume{";
            if (!volume.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
                !volume.EndsWith("}\\", StringComparison.Ordinal) ||
                !Guid.TryParseExact(volume.Substring(prefix.Length, volume.Length - prefix.Length - 2), "D", out var guid) ||
                guid == Guid.Empty)
                throw new IOException("Invalid volume GUID.");
            return prefix + guid.ToString("D") + "}";
        }

        private static string ReadVolumeGuid(string drive)
        {
            var volume = new StringBuilder(128);
            if (!GetVolumeNameForVolumeMountPoint(drive + "\\", volume, (uint)volume.Capacity))
                throw new IOException("Could not identify the selected volume.", new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()));
            return VolumeArgument(volume.ToString());
        }

        private static BackupTarget PrepareTarget(string drive)
        {
            drive = NormalizeDrive(drive);
            string identity = ReadVolumeGuid(drive);
            var disk = SeparateBackupDisk(drive) ?? throw new IOException(
                "The backup volume must be on a separate physical disk from the Windows and boot partitions.");
            var label = new DriveInfo(drive).VolumeLabel;
            if (ReadVolumeGuid(drive) != identity) throw new IOException("Backup volume changed during identification.");
            return new BackupTarget(drive, identity, disk, label);
        }

        internal static Task<BackupTarget> PrepareTargetAsync(string drive) => Task.Run(() => PrepareTarget(drive));

        internal static bool SameTarget(BackupTarget expected, BackupTarget actual) =>
            expected.Drive == actual.Drive && expected.VolumeGuid == actual.VolumeGuid &&
            expected.DiskNumber == actual.DiskNumber && expected.Label == actual.Label;

        internal static async Task<(bool Success, string Output)> StartSystemImageBackupAsync(BackupTarget target)
        {
            if (!await BackupGate.WaitAsync(0))
                return (false, "A system image backup is already running.");

            try
            {
                using var lease = await GearWinCore.EnterAsync(CoreWork.Backup);
                if (!SameTarget(target, await PrepareTargetAsync(target.Drive)))
                    return (false, "Backup volume changed after confirmation. No backup started.");

                // wbadmin accepts a volume GUID for the backup target. Do not rely on a
                // drive letter that can be reassigned by Windows while the job is queued.
                var backup = await RunAsync("start", "backup", "-backupTarget:" + target.VolumeGuid,
                    "-allCritical", "-vssCopy", "-quiet");
                if (!backup.Success) return backup;
                if (!SameTarget(target, await PrepareTargetAsync(target.Drive)))
                    return (false, "Backup reported success but the confirmed volume identity changed; verification unavailable.");
                var catalog = await RunAsync("get", "versions", "-backupTarget:" + target.Drive);
                if (!SameTarget(target, await PrepareTargetAsync(target.Drive)) ||
                    !catalog.Success || string.IsNullOrWhiteSpace(catalog.Output))
                    return (false, "Windows Backup reported success but the backup catalog could not be checked; " +
                        "do not assume the image can be restored. " + catalog.Output);
                return (true, "Windows Backup completed and the selected disk's backup catalog is readable. " +
                    "A newly created version and bare-metal boot recovery are NOT independently validated.\n" + catalog.Output);
            }
            catch (Exception ex) when (ex is ManagementException or IOException or UnauthorizedAccessException or
                System.Runtime.InteropServices.COMException or ArgumentException or OperationCanceledException or TimeoutException)
            {
                return (false, ex.Message);
            }
            finally { BackupGate.Release(); }
        }

        public static async Task<(bool Success, string Output)> GetBackupVersionsAsync(string targetDrive)
        {
            try
            {
                var drive = NormalizeDrive(targetDrive);
                var identity = ReadVolumeGuid(drive);
                var result = await RunAsync("get", "versions", "-backupTarget:" + drive);
                return ReadVolumeGuid(drive) == identity ? result : (false, "Catalog volume changed during query.");
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
            {
                return (false, ex.Message);
            }
        }

        private static uint? SeparateBackupDisk(string targetDrive)
        {
            var volume = new DriveInfo(targetDrive);
            if (!volume.IsReady || volume.DriveType is not (DriveType.Fixed or DriveType.Removable))
                return null;

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
                            return null;
                        targetDisk = disk;
                    }
                }
            }
            return bootFound && systemFound && targetDisk is uint number && !criticalDisks.Contains(number) ? number : null;
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
                var stdout = HealthCleanupService.ReadBoundedOutputAsync(process.StandardOutput, 32768);
                var stderr = HealthCleanupService.ReadBoundedOutputAsync(process.StandardError, 32768);
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
