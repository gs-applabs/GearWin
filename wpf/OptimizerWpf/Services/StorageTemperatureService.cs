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

    internal static int? Read(string drive)
    {
        try
        {
            var letter = drive.TrimEnd('\\');
            if (letter.Length != 2 || !char.IsAsciiLetter(letter[0]) || letter[1] != ':') return null;
            using var volume = Open(@"\\.\" + letter);
            if (volume.IsInvalid) return null;
            var extents = new byte[32];
            if (!DeviceIoControl(volume, VolumeDiskExtents, null, 0, extents, extents.Length, out int used, IntPtr.Zero)
                || used < 32 || BinaryPrimitives.ReadUInt32LittleEndian(extents) != 1) return null;
            uint number = BinaryPrimitives.ReadUInt32LittleEndian(extents.AsSpan(8));
            using var disk = Open(@"\\.\PhysicalDrive" + number);
            if (disk.IsInvalid) return SensorService.GetDiskTemperatureByNumber(number) ?? ReadReliabilityCounter(number);
            // Device first; adapter property is not used as a substitute for device temperature.
            var query = new byte[12];
            BinaryPrimitives.WriteUInt32LittleEndian(query, 52);
            var buffer = new byte[4096];
            var reading = DeviceIoControl(disk, StorageQueryProperty, query, query.Length, buffer, buffer.Length,
                out used, IntPtr.Zero) && used >= 0 && used <= buffer.Length
                ? ParseDescriptor(buffer.AsSpan(0, used)) : null;
            return reading ?? SensorService.GetDiskTemperatureByNumber(number) ?? ReadReliabilityCounter(number);
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
