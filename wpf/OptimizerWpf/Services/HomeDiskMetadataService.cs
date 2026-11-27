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
    private static readonly DriveMetadataSlot<Snapshot> Metadata = new(drive =>
        Task.Run(() =>
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
        }));

    internal static string? NormalizeDrive(string? drive)
    {
        var key = drive?.TrimEnd('\\').ToUpperInvariant();
        return key is { Length: 2 } && char.IsAsciiLetter(key[0]) && key[1] == ':' ? key : null;
    }

    internal static Task<Snapshot?> ReadAsync(string drive) => Metadata.ReadAsync(drive);

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


// The bounded slot is independently testable without WMI, native handles or physical disks.
internal sealed class DriveMetadataSlot<T> where T : class
{
    private readonly object _gate = new();
    private readonly Dictionary<string, (T Value, long At)> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Func<string, Task<T>> _read;
    private readonly Func<long> _clock;
    private Task<T?>? _active;
    private string? _drive;

    internal DriveMetadataSlot(Func<string, Task<T>> read, Func<long>? clock = null)
    {
        _read = read;
        _clock = clock ?? (() => Environment.TickCount64);
    }

    internal int CachedCount { get { lock (_gate) return _cache.Count; } }

    internal Task<T?> ReadAsync(string drive)
    {
        var key = HomeDiskMetadataService.NormalizeDrive(drive);
        if (key == null) return Task.FromResult<T?>(null);
        lock (_gate)
        {
            if (_cache.TryGetValue(key, out var item) &&
                SharedTelemetryService.IsFresh(_clock(), item.At, 120000))
                return Task.FromResult<T?>(item.Value);
            if (_active is { IsCompleted: false })
                return _drive == key ? _active : Task.FromResult<T?>(null);
            _drive = key;
            return _active = ReadCoreAsync(key);
        }
    }

    private async Task<T?> ReadCoreAsync(string drive)
    {
        var result = await _read(drive).ConfigureAwait(false);
        lock (_gate) _cache[drive] = (result, _clock());
        return result;
    }
}
