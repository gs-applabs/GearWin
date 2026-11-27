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

    internal static string? NormalizeDrive(string? drive)
    {
        var key = drive?.TrimEnd('\\').ToUpperInvariant();
        return key is { Length: 2 } && char.IsAsciiLetter(key[0]) && key[1] == ':' ? key : null;
    }

    internal static Task<Snapshot?> ReadAsync(string drive)
    {
        var key = NormalizeDrive(drive);
        if (key == null)
            return Task.FromResult<Snapshot?>(null);
        lock (Gate)
        {
            if (Cache.TryGetValue(key, out var item) &&
                SharedTelemetryService.IsFresh(Environment.TickCount64, item.At, 120000))
                return Task.FromResult<Snapshot?>(item.Value);
            if (_active is { IsCompleted: false })
                return string.Equals(_drive, key, StringComparison.OrdinalIgnoreCase)
                    ? _active : Task.FromResult<Snapshot?>(null);
            _drive = key;
            return _active = ReadCoreAsync(key);
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

    private static Task<bool?>? _smartTask;
    private static string? _smartDrive;
    private static long _smartAt;

    internal static Task<bool?> ReadSystemSmartAsync(string drive)
    {
        var key = NormalizeDrive(drive);
        if (key == null) return Task.FromResult<bool?>(null);
        lock (Gate)
        {
            if (_smartTask is { IsCompleted: false })
                return _smartDrive == key ? _smartTask : Task.FromResult<bool?>(null);
            if (_smartTask is { IsCompletedSuccessfully: true } && _smartDrive == key &&
                SharedTelemetryService.IsFresh(Environment.TickCount64, _smartAt, 60000))
                return _smartTask;
            _smartDrive = key;
            _smartAt = Environment.TickCount64;
            return _smartTask = Task.Run(() => DriveTypeService.GetSmartHealthy(key));
        }
    }
}
