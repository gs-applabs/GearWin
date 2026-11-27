using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace OptimizerWpf.Services;

// Only one disk metadata query at a time; a new selection never queues behind it.
// Cache has a strict key bound (one drive letter), no background eviction timer.
internal static class HomeDiskMetadataService
{
    internal sealed record Snapshot(string? Model, PhysicalDriveKind Kind, bool Estimated);
    private static readonly object Gate = new();
    private static readonly Dictionary<string, (Snapshot Value, long At)> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static Task<Snapshot?>? _active;
    private static string? _drive;

    internal static Task<Snapshot?> ReadAsync(string drive)
    {
        drive = drive.TrimEnd('\\').ToUpperInvariant();
        if (drive.Length != 2 || !char.IsAsciiLetter(drive[0]) || drive[1] != ':')
            return Task.FromResult<Snapshot?>(null);
        lock (Gate)
        {
            if (Cache.TryGetValue(drive, out var item) &&
                SharedTelemetryService.IsFresh(Environment.TickCount64, item.At, 120000))
                return Task.FromResult<Snapshot?>(item.Value);
            if (_active is { IsCompleted: false })
                return string.Equals(_drive, drive, StringComparison.OrdinalIgnoreCase)
                    ? _active : Task.FromResult<Snapshot?>(null);
            _drive = drive;
            return _active = ReadCoreAsync(drive);
        }
    }

    private static async Task<Snapshot?> ReadCoreAsync(string drive)
    {
        var result = await Task.Run(() =>
        {
            var model = DriveTypeService.GetDiskModel(drive);
            var kind = DriveTypeService.Detect(drive);
            bool estimated = false;
            if (kind == PhysicalDriveKind.Unknown)
            {
                kind = DriveTypeService.GuessFromModelName(model);
                estimated = kind != PhysicalDriveKind.Unknown;
            }
            return new Snapshot(model, kind, estimated);
        }).ConfigureAwait(false);
        lock (Gate) Cache[drive] = (result, Environment.TickCount64);
        return result;
    }
}
