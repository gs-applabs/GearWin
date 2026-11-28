using System;
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Management;
using Microsoft.Win32.SafeHandles;

namespace OptimizerWpf.Services;

// Read-only Windows storage queries. Never guess a physical disk from a model name.
// Fail closed for spanned/virtual volumes, unsupported drivers and malformed responses.
internal static class StorageTemperatureService
{
    private const uint VolumeDiskExtents = 0x00560000;
    private const uint StorageQueryProperty = 0x002D1400;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string name, uint access, uint share,
        IntPtr security, uint disposition, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(SafeFileHandle handle, uint code,
        byte[]? input, int inputSize, [Out] byte[] output, int outputSize,
        out int returned, IntPtr overlapped);

    private static SafeFileHandle Open(string name) =>
        CreateFile(name, 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);

    internal static uint? ResolveDiskNumber(string drive)
    {
        try
        {
            var letter = drive.TrimEnd('\\');
            if (letter.Length != 2 || !char.IsAsciiLetter(letter[0]) || letter[1] != ':') return null;
            using var volume = Open(@"\\.\" + letter);
            if (volume.IsInvalid) return null;
            var extents = new byte[32];
            if (!DeviceIoControl(volume, VolumeDiskExtents, null, 0, extents, extents.Length, out int used, IntPtr.Zero)
                || used < 0 || used > extents.Length) return null;
            return ParseDiskNumber(extents.AsSpan(0, used));
        }
        catch { return null; }
    }

    private static byte[]? QueryProperty(SafeFileHandle disk, uint property)
    {
        var query = new byte[12];
        BinaryPrimitives.WriteUInt32LittleEndian(query, property);
        var buffer = new byte[4096];
        return DeviceIoControl(disk, StorageQueryProperty, query, query.Length, buffer, buffer.Length,
            out int used, IntPtr.Zero) && used >= 0 && used <= buffer.Length ? buffer[..used] : null;
    }

    internal static (int Bus, bool? SeekPenalty)? ReadDeviceTraits(uint number)
    {
        try
        {
            using var disk = Open(@"\\.\PhysicalDrive" + number);
            if (disk.IsInvalid) return null;
            var descriptor = QueryProperty(disk, 0); // StorageDeviceProperty
            int? bus = descriptor == null ? null : ParseBusType(descriptor);
            if (!bus.HasValue) return null;
            var seek = QueryProperty(disk, 7); // StorageDeviceSeekPenaltyProperty
            return (bus.Value, seek == null ? null : ParseSeekPenalty(seek));
        }
        catch { return null; }
    }

    private static bool ValidHeader(ReadOnlySpan<byte> data, int minimum)
    {
        if (data.Length < minimum) return false;
        uint version = BinaryPrimitives.ReadUInt32LittleEndian(data);
        uint size = BinaryPrimitives.ReadUInt32LittleEndian(data[4..]);
        return version >= minimum && version <= size && size >= minimum && size <= data.Length;
    }

    internal static int? ParseBusType(ReadOnlySpan<byte> data)
    {
        if (!ValidHeader(data, 36)) return null;
        uint value = BinaryPrimitives.ReadUInt32LittleEndian(data[28..]);
        return value <= 255 ? (int)value : null;
    }

    internal static bool? ParseSeekPenalty(ReadOnlySpan<byte> data)
    {
        if (!ValidHeader(data, 9) || data[8] > 1) return null;
        return data[8] == 1;
    }

    internal static int? Read(string drive)
    {
        try
        {
            var diskNumber = ResolveDiskNumber(drive);
            if (!diskNumber.HasValue) return null;
            uint number = diskNumber.Value;
            using var disk = Open(@"\\.\PhysicalDrive" + number);
            if (disk.IsInvalid) return null;
            // Keep the same handle for identity and temperature; do not query
            // seek penalty on every temperature sample.
            var descriptor = QueryProperty(disk, 0);
            int? bus = descriptor == null ? null : ParseBusType(descriptor);
            if (bus == null || !DriveTypeService.IsDirectStorageBus(bus.Value)) return null;
            var temperature = QueryProperty(disk, 52);
            var reading = temperature == null ? null : ParseDescriptor(temperature);
            if (reading.HasValue) return reading;
            if (ResolveDiskNumber(drive) != number) return null;
            return SensorService.GetDiskTemperatureByNumber(number) ?? ReadReliabilityCounter(number);
        }
        catch { return null; }
    }

    private static int? ReadReliabilityCounter(uint number)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                new ManagementScope(@"root\Microsoft\Windows\Storage"),
                new ObjectQuery($"SELECT * FROM MSFT_Disk WHERE Number = {number}"),
                new EnumerationOptions { Timeout = TimeSpan.FromSeconds(4), ReturnImmediately = true });
            using var disks = searcher.Get();
            foreach (ManagementObject disk in disks)
            {
                using (disk)
                using (var counters = disk.GetRelated("MSFT_StorageReliabilityCounter", "MSFT_DiskToStorageReliabilityCounter", null, null, null, null, false,
                    new EnumerationOptions { Timeout = TimeSpan.FromSeconds(4), ReturnImmediately = true }))
                {
                    int? temperature = null;
                    int count = 0;
                    foreach (ManagementObject counter in counters)
                    {
                        using (counter)
                        {
                            count++;
                            if (counter["Temperature"] is object value)
                            {
                                int temp = Convert.ToInt32(value);
                                if (SensorService.IsValidTemperature(temp)) temperature = temp;
                            }
                        }
                    }
                    return count == 1 ? temperature : null;
                }
            }
        }
        catch { }
        return null;
    }

    internal static uint? ParseDiskNumber(ReadOnlySpan<byte> extents)
    {
        // VOLUME_DISK_EXTENTS: aligned DISK_EXTENT begins at byte 8 on win-x64.
        if (extents.Length < 32 || BinaryPrimitives.ReadUInt32LittleEndian(extents) != 1) return null;
        return BinaryPrimitives.ReadUInt32LittleEndian(extents.Slice(8));
    }

    internal static int? ParseDescriptor(ReadOnlySpan<byte> data)
    {
        const int header = 24, item = 16;
        if (data.Length < header) return null;
        uint version = BinaryPrimitives.ReadUInt32LittleEndian(data);
        uint size = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(4));
        int count = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(12));
        if (version < header || size > data.Length || size < header || count == 0 ||
            count > (size - header) / item) return null;
        for (int i = 0; i < count; i++)
        {
            var entry = data.Slice(header + i * item, item);
            if (BinaryPrimitives.ReadUInt16LittleEndian(entry) != 0) continue;
            int value = BinaryPrimitives.ReadInt16LittleEndian(entry.Slice(2));
            return SensorService.IsValidTemperature(value) ? value : null;
        }
        return null;
    }
}
