using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace OptimizerWpf.Services;

// Only one disk metadata query at a time; a new selection never queues behind it.
// Cache has a strict key bound (26 drive letters), no background eviction timer.
internal static class HomeDiskMetadataService
{
    internal sealed record Snapshot(string? Model, PhysicalDriveKind Kind, bool Estimated);
    private static readonly object Gate = new();
    private static readonly DriveMetadataSlot<Snapshot> Metadata = new(drive =>
        Task.Run(() => DriveTypeService.ReadHomeMetadata(drive)));

    internal static string? NormalizeDrive(string? drive)
    {
        var key = drive?.TrimEnd('\\').ToUpperInvariant();
        return key is { Length: 2 } && char.IsAsciiLetter(key[0]) && key[1] == ':' ? key : null;
    }

    internal static Task<Snapshot?> ReadAsync(string drive) => Metadata.ReadAsync(drive);

    private static long _generation;
    internal static long TopologyGeneration => System.Threading.Interlocked.Read(ref _generation);

    internal static void InvalidateTopology()
    {
        lock (Gate)
        {
            System.Threading.Interlocked.Increment(ref _generation);
            Metadata.Invalidate();
        }
    }

    private static long _smartGeneration;
    private static Task<bool?>? _smartTask;
    private static string? _smartDrive;
    private static long _smartAt;

    internal static Task<bool?> ReadSystemSmartAsync(string drive)
    {
        var key = NormalizeDrive(drive);
        if (key == null) return Task.FromResult<bool?>(null);
        lock (Gate)
        {
            var generation = TopologyGeneration;
            if (_smartTask is { IsCompleted: false })
                return _smartDrive == key && _smartGeneration == generation ? _smartTask : Task.FromResult<bool?>(null);
            if (_smartTask is { IsCompletedSuccessfully: true } && _smartDrive == key && _smartGeneration == generation &&
                SharedTelemetryService.IsFresh(Environment.TickCount64, _smartAt, 60000))
                return _smartTask;
            _smartDrive = key;
            _smartGeneration = generation;
            _smartAt = Environment.TickCount64;
            return _smartTask = Task.Run(() =>
            {
                var value = DriveTypeService.GetSmartHealthy(key);
                return generation == TopologyGeneration ? value : null;
            });
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
    private long _generation, _activeGeneration;

    internal void Invalidate()
    {
        lock (_gate) { _generation++; _cache.Clear(); }
    }

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
                return _drive == key && _activeGeneration == _generation ? _active : Task.FromResult<T?>(null);
            _drive = key;
            _activeGeneration = _generation;
            return _active = ReadCoreAsync(key, _generation);
        }
    }

    private async Task<T?> ReadCoreAsync(string drive, long generation)
    {
        var result = await _read(drive).ConfigureAwait(false);
        lock (_gate)
        {
            if (generation != _generation) return null;
            _cache[drive] = (result, _clock());
            return result;
        }
    }
}
