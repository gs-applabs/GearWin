using System;
using System.IO;
using System.Linq;
using System.Management;

namespace OptimizerWpf.Services
{
    public enum PhysicalDriveKind { Hdd, Ssd, Nvme, Usb, Unknown }

    // Detects whether a drive letter is backed by an HDD, SSD, or removable/USB media - the user
    // asked for the disk tile's icon to change based on this. Removable media (USB flash drives,
    // SD cards) is detected the cheap, reliable way via System.IO.DriveInfo.DriveType. For fixed
    // drives, distinguishing HDD vs SSD needs the modern Storage Management WMI provider
    // (MSFT_PhysicalDisk.MediaType in root\Microsoft\Windows\Storage) - the older Win32_DiskDrive
    // class doesn't reliably expose this. Correlates DriveLetter -> MSFT_Partition -> MSFT_Disk ->
    // MSFT_PhysicalDisk via the storage subsystem's association classes.
    public static class DriveTypeService
    {
        // Fallback όταν το WMI δεν καταφέρνει να προσδιορίσει τον τύπο (ρητό αίτημα χρήστη: "από την
        // ονομασία των δίσκων μπορείς να καταλάβεις τι είδος είναι") - εκτίμηση βάσει γνωστών μοτίβων
        // ονομασίας μοντέλου δίσκου. ΣΗΜΕΙΩΣΗ ΕΙΛΙΚΡΙΝΕΙΑΣ: αυτό είναι ΕΚΤΙΜΗΣΗ, όχι σίγουρη ανίχνευση
        // (π.χ. το "WD Blue" καλύπτει ΚΑΙ HDD ΚΑΙ SSD/NVMe ανάλογα με το μοντέλο - το "SN" πρόθεμα
        // διακρίνει τα NVMe μοντέλα της σειράς) - χρησιμοποιείται ΜΟΝΟ όταν η πραγματική ανίχνευση
        // μέσω WMI αποτύχει εντελώς.
        public static PhysicalDriveKind GuessFromModelName(string? model)
        {
            if (string.IsNullOrWhiteSpace(model)) return PhysicalDriveKind.Unknown;
            var m = model.ToUpperInvariant();

            // Ρητό αίτημα χρήστη ("αντίστοιχα εικονίδια... για nvme") - ξεχωριστό κλάδο από το γενικό
            // SSD, με τα πιο σαφή NVMe-specific μοτίβα ονόματος πρώτα (μεγαλύτερη σιγουριά).
            if (m.Contains("NVME") || System.Text.RegularExpressions.Regex.IsMatch(m, @"\bSN\d{3}\b") ||
                System.Text.RegularExpressions.Regex.IsMatch(m, @"\bSAMSUNG\b.*\b(950|960|970|980|990|9100)\b") ||
                m.Contains("CRUCIAL P") || m.Contains("KINGSTON NV") || m.Contains("KINGSTON A2000") || m.Contains("KINGSTON KC3000"))
                return PhysicalDriveKind.Nvme;

            if (m.Contains("SSD") || m.Contains("KINGSTON KC"))
                return PhysicalDriveKind.Ssd;

            if (m.Contains("BARRACUDA") || m.Contains("IRONWOLF") || m.Contains("SKYHAWK") ||
                System.Text.RegularExpressions.Regex.IsMatch(m, @"\bWD\d{2,}E[A-Z]{3}\b") || m.Contains("TOSHIBA DT") || m.Contains("TOSHIBA P3"))
                return PhysicalDriveKind.Hdd;

            return PhysicalDriveKind.Unknown;
        }

        // Use the volume's single disk extent and the matching device handle;
        // MSFT_PhysicalDisk.DeviceId is not guaranteed to equal MSFT_Disk.Number.
        internal static bool IsDirectStorageBus(int bus) => bus is 1 or 2 or 3 or 7 or 10 or 11 or 12 or 13 or 17;

        internal static PhysicalDriveKind Classify(int bus, bool? seekPenalty)
        {
            if (bus == 7) return PhysicalDriveKind.Usb;
            if (bus == 17) return seekPenalty == true ? PhysicalDriveKind.Unknown : PhysicalDriveKind.Nvme;
            if (!IsDirectStorageBus(bus)) return PhysicalDriveKind.Unknown;
            if (bus is 12 or 13) return PhysicalDriveKind.Usb; // removable SD/MMC icon class
            return seekPenalty switch { true => PhysicalDriveKind.Hdd, false => PhysicalDriveKind.Ssd, _ => PhysicalDriveKind.Unknown };
        }

        public static PhysicalDriveKind Detect(string driveLetter)
        {
            try
            {
                var key = HomeDiskMetadataService.NormalizeDrive(driveLetter);
                if (key == null) return PhysicalDriveKind.Unknown;
                var info = new DriveInfo(key + "\\");
                if (!info.IsReady || info.DriveType is not (DriveType.Fixed or DriveType.Removable)) return PhysicalDriveKind.Unknown;
                var number = StorageTemperatureService.ResolveDiskNumber(key);
                if (number == null) return PhysicalDriveKind.Unknown;
                var traits = StorageTemperatureService.ReadDeviceTraits(number.Value);
                return traits == null ? PhysicalDriveKind.Unknown : Classify(traits.Value.Bus, traits.Value.SeekPenalty);
            }
            catch { return PhysicalDriveKind.Unknown; }
        }

        // One volume mapping and descriptor observation for the Home card.
        // WMI model failure must not discard a valid native classification.
        internal static HomeDiskMetadataService.Snapshot ReadHomeMetadata(string driveLetter)
        {
            var unknown = new HomeDiskMetadataService.Snapshot(null, PhysicalDriveKind.Unknown, false);
            try
            {
                var key = HomeDiskMetadataService.NormalizeDrive(driveLetter);
                if (key == null) return unknown;
                var info = new DriveInfo(key + "\\");
                if (info.DriveType is not (DriveType.Fixed or DriveType.Removable) || !info.IsReady) return unknown;
                var number = StorageTemperatureService.ResolveDiskNumber(key);
                if (number == null) return unknown;
                var traits = StorageTemperatureService.ReadDeviceTraits(number.Value);
                if (traits == null || !IsDirectStorageBus(traits.Value.Bus)) return unknown;
                string? model = null;
                try { model = ReadIdentity(number.Value)?.Model; }
                catch { /* native type is still usable */ }
                return ComposeHomeMetadata(traits.Value.Bus, traits.Value.SeekPenalty, model,
                    StorageTemperatureService.ResolveDiskNumber(key) == number);
            }
            catch { return unknown; }
        }

        internal static HomeDiskMetadataService.Snapshot ComposeHomeMetadata(int bus, bool? seek, string? model, bool sameDisk)
        {
            if (!sameDisk || !IsDirectStorageBus(bus))
                return new(null, PhysicalDriveKind.Unknown, false);
            var kind = Classify(bus, seek);
            // Contradictory NVMe/seek evidence is unknown, not a model-based override.
            if (kind != PhysicalDriveKind.Unknown || seek.HasValue)
                return new(model, kind, false);
            var guess = GuessFromModelName(model);
            return new(model, guess, guess != PhysicalDriveKind.Unknown);
        }

        public static string? GetBusTypeName(string driveLetter)
        {
            var number = StorageTemperatureService.ResolveDiskNumber(driveLetter);
            if (number == null) return null;
            var traits = StorageTemperatureService.ReadDeviceTraits(number.Value);
            return traits?.Bus switch
            {
                1 => "SCSI", 2 => "ATAPI", 3 => "ATA", 7 => "USB", 8 => "RAID", 9 => "iSCSI",
                10 => "SAS", 11 => "SATA", 12 => "SD", 13 => "MMC", 14 => "Virtual", 15 => "File-backed virtual",
                16 => "Storage Spaces", 17 => "NVMe", _ => null
            };
        }

        private static ManagementObjectSearcher Query(string scope, string wql) =>
            new(new ManagementScope(scope), new ObjectQuery(wql),
                new System.Management.EnumerationOptions { Timeout = TimeSpan.FromSeconds(4), ReturnImmediately = true, Rewindable = false });

        private static (string? Model, string? Pnp)? ReadIdentity(uint number)
        {
            using var searcher = Query(@"root\CIMV2", $"SELECT Model, PNPDeviceID FROM Win32_DiskDrive WHERE Index = {number}");
            using var rows = searcher.Get();
            (string? Model, string? Pnp)? result = null;
            int count = 0;
            foreach (ManagementObject row in rows)
            {
                using (row)
                {
                    if (++count != 1) return null;
                    result = (row["Model"]?.ToString()?.Trim(), row["PNPDeviceID"]?.ToString());
                }
            }
            return result;
        }

        public static string? GetDiskModel(string driveLetter)
        {
            try
            {
                var number = StorageTemperatureService.ResolveDiskNumber(driveLetter);
                if (number == null) return null;
                var traits = StorageTemperatureService.ReadDeviceTraits(number.Value);
                // Do not feed a virtual/RAID device's name into the model heuristic.
                if (traits == null || !IsDirectStorageBus(traits.Value.Bus)) return null;
                var model = ReadIdentity(number.Value)?.Model;
                return StorageTemperatureService.ResolveDiskNumber(driveLetter) == number ? model : null;
            }
            catch { return null; }
        }

        internal static bool MatchesSmartInstance(string? pnp, string? instance)
        {
            if (string.IsNullOrWhiteSpace(pnp) || string.IsNullOrWhiteSpace(instance) ||
                !instance.StartsWith(pnp, StringComparison.OrdinalIgnoreCase)) return false;
            if (instance.Length == pnp.Length) return true;
            var suffix = instance[pnp.Length..];
            return suffix.Length > 1 && suffix[0] == '_' && suffix[1..].All(char.IsAsciiDigit);
        }

        public static bool? GetSmartHealthy(string driveLetter)
        {
            try
            {
                var number = StorageTemperatureService.ResolveDiskNumber(driveLetter);
                if (number == null) return null;
                var traits = StorageTemperatureService.ReadDeviceTraits(number.Value);
                if (traits == null || !IsDirectStorageBus(traits.Value.Bus)) return null;
                var pnp = ReadIdentity(number.Value)?.Pnp;
                if (!string.IsNullOrWhiteSpace(pnp))
                {
                    try
                    {
                        using var searcher = Query(@"root\WMI", "SELECT InstanceName, PredictFailure FROM MSStorageDriver_FailurePredictStatus");
                        using var rows = searcher.Get();
                        int matched = 0, count = 0;
                        bool? healthy = null;
                        foreach (ManagementObject row in rows)
                        {
                            using (row)
                            {
                                if (++count > 4096) return null;
                                if (!MatchesSmartInstance(pnp, row["InstanceName"]?.ToString())) continue;
                                if (++matched != 1) return null;
                                if (row["PredictFailure"] is bool failure) healthy = !failure;
                            }
                        }
                        if (matched == 1) return StorageTemperatureService.ResolveDiskNumber(driveLetter) == number ? healthy : null;
                    }
                    catch { /* try only an explicit storage association below */ }
                }
                // Provider health is a summary, not raw SMART attributes. Use
                // only an explicit one-to-one association, never equal-number guesses.
                using var diskQuery = Query(@"root\Microsoft\Windows\Storage", $"SELECT * FROM MSFT_Disk WHERE Number = {number.Value}");
                using var disks = diskQuery.Get();
                int diskCount = 0;
                bool? result = null;
                foreach (ManagementObject disk in disks)
                {
                    using (disk)
                    {
                        if (++diskCount != 1) return null;
                        using var physical = disk.GetRelated("MSFT_PhysicalDisk", "MSFT_PhysicalDiskToDisk", null, null, null, null, false,
                            new System.Management.EnumerationOptions { Timeout = TimeSpan.FromSeconds(4), ReturnImmediately = true });
                        int count = 0;
                        foreach (ManagementObject row in physical)
                        {
                            using (row)
                            {
                                if (++count != 1) return null;
                                if (row["HealthStatus"] is object value)
                                    result = Convert.ToInt32(value) switch { 0 => true, 1 or 2 => false, _ => null };
                            }
                        }
                    }
                }
                return StorageTemperatureService.ResolveDiskNumber(driveLetter) == number ? result : null;
            }
            catch { return null; }
        }
    }
}
