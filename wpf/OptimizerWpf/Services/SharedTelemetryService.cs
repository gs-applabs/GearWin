using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace OptimizerWpf.Services;

// On-demand, memory-only telemetry. No independent timer and no GPU counters
// unless a visible Home or active gaming widget is consuming them.
internal static class SharedTelemetryService
{
    private static readonly object Gate = new();
    private static readonly object CounterGate = new();
    private static int _consumers;
    private static int _generation;
    private static bool _stopped;
    private static PerformanceCounter[]? _gpuCounters;
    private static long _counterCreated;
    private static long _lastSampleAt;
    private static int? _lastGpu;
    private static Task<int?>? _inFlight;

    private sealed class Consumer : IDisposable
    {
        private int _released;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) != 0) return;
            lock (Gate)
            {
                if (--_consumers != 0) return;
                _generation++;
                _lastGpu = null;
                _lastSampleAt = 0;
            }
            // Native disposal must not block a dispatcher during navigation.
            _ = Task.Run(ReleaseIfUnused);
        }
    }

    internal static IDisposable? SubscribeGpu()
    {
        lock (Gate)
        {
            if (_stopped) return null;
            _consumers++;
            return new Consumer();
        }
    }

    internal static bool IsFresh(long now, long then, long ttl) => now >= then && now - then < ttl;

    internal static Task<int?> ReadGpuAsync()
    {
        lock (Gate)
        {
            if (_stopped || _consumers == 0) return Task.FromResult<int?>(null);
            if (_inFlight is { IsCompleted: false }) return _inFlight;
            if (_lastSampleAt != 0 && IsFresh(Environment.TickCount64, _lastSampleAt, 1500))
                return Task.FromResult(_lastGpu);
            int generation = _generation;
            return _inFlight = Task.Run(() => SampleGpu(generation));
        }
    }

    private static int? SampleGpu(int generation)
    {
        lock (CounterGate)
        {
            lock (Gate)
                if (_stopped || _consumers == 0 || generation != _generation) return null;
            int? value = null;
            var now = Environment.TickCount64;
            // Engines are process-dependent; periodically renew only while demanded.
            if (_counterCreated == 0 || !IsFresh(now, _counterCreated, 30000))
            {
                DisposeCounters();
                _gpuCounters = GpuInfoService.CreateUsageCounters();
                _counterCreated = now;
            }
            else if (_gpuCounters != null)
                value = GpuInfoService.SampleUsagePercent(_gpuCounters);

            lock (Gate)
            {
                if (!_stopped && _consumers > 0 && generation == _generation)
                {
                    _lastGpu = value;
                    _lastSampleAt = Environment.TickCount64;
                    return value;
                }
            }
            return null;
        }
    }

    private static void DisposeCounters()
    {
        if (_gpuCounters != null)
            foreach (var counter in _gpuCounters) try { counter.Dispose(); } catch { }
        _gpuCounters = null;
        _counterCreated = 0;
    }

    private static void ReleaseIfUnused()
    {
        lock (CounterGate)
        {
            lock (Gate) if (!_stopped && _consumers > 0) return;
            DisposeCounters();
        }
    }

    internal static void Stop()
    {
        lock (Gate) { _stopped = true; _generation++; }
        // Never block exit on a potentially stalled vendor/PDH counter.
        _ = Task.Run(ReleaseIfUnused);
    }
}
