using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace OptimizerWpf.Services;

// The existing PerformanceProfileService remains the only adaptive governor.
// This coordinator has no timer, polling thread, or persistent activity log.
// Leases are acquired at service boundaries, never recursively inside a lease.
internal enum CoreWork { CleanupScan, Cleanup, Memory, Repair, UpdateScan, Backup, Registry, HardwareScan }
internal enum CorePriority { Background, User }

internal sealed class OperationCoordinator
{
    private sealed class Request
    {
        internal readonly CoreWork Kind;
        internal readonly CorePriority Priority;
        internal readonly TaskCompletionSource<IDisposable> Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Request(CoreWork kind, CorePriority priority) { Kind = kind; Priority = priority; }
    }

    private sealed class Lease : IDisposable
    {
        private OperationCoordinator? _owner;
        private readonly Request _request;
        internal Lease(OperationCoordinator owner, Request request) { _owner = owner; _request = request; }
        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.Release(_request);
    }

    private readonly object _gate = new();
    private readonly List<Request> _active = new();
    private readonly List<Request> _pending = new();
    private readonly Func<int> _capacity;
    private const int MaxPending = 32;
    internal OperationCoordinator(Func<int> capacity) => _capacity = capacity;

    internal static bool IsHeavyMutation(CoreWork kind) => kind is CoreWork.Cleanup or CoreWork.Memory or CoreWork.Repair or CoreWork.Backup or CoreWork.Registry;

    // Conservative first policy: do not run a heavy mutation alongside another heavy
    // operation or a competing scan. Hardware alerts retain an independent read-only slot.
    internal static bool Conflicts(CoreWork a, CoreWork b)
    {
        if (a == b) return true;
        if (a == CoreWork.HardwareScan || b == CoreWork.HardwareScan) return false;
        return IsHeavyMutation(a) || IsHeavyMutation(b);
    }

    internal int ActiveCount { get { lock (_gate) return _active.Count; } }
    internal int PendingCount { get { lock (_gate) return _pending.Count; } }

    internal async Task<IDisposable> AcquireAsync(CoreWork kind, CorePriority priority, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var request = new Request(kind, priority);
        lock (_gate)
        {
            if (_pending.Count >= MaxPending) throw new InvalidOperationException("Maintenance queue is full.");
            _pending.Add(request);
            Dispatch();
        }
        using var registration = ct.Register(() =>
        {
            lock (_gate)
            {
                if (_pending.Remove(request))
                {
                    request.Completion.TrySetCanceled(ct);
                    Dispatch();
                }
            }
        });
        var lease = await request.Completion.Task.ConfigureAwait(false);
        if (ct.IsCancellationRequested)
        {
            lease.Dispose();
            ct.ThrowIfCancellationRequested();
        }
        return lease;
    }

    private void Release(Request request)
    {
        lock (_gate)
        {
            _active.Remove(request);
            Dispatch();
        }
    }

    internal void PolicyChanged() { lock (_gate) Dispatch(); }

    // User work wins over queued background work; running work is never interrupted.
    // Do not jump ahead of a blocked user request with conflicting background work.
    private void Dispatch()
    {
        var limit = Math.Clamp(_capacity(), 1, 3);
        while (_active.Count < limit)
        {
            var next = _pending.FirstOrDefault(r => r.Priority == CorePriority.User &&
                !_active.Any(a => Conflicts(a.Kind, r.Kind)));
            next ??= _pending.FirstOrDefault(r => r.Priority == CorePriority.Background &&
                !_active.Any(a => Conflicts(a.Kind, r.Kind)) &&
                !_pending.Any(p => p.Priority == CorePriority.User && Conflicts(p.Kind, r.Kind)));
            if (next == null) return;
            _pending.Remove(next);
            _active.Add(next);
            next.Completion.TrySetResult(new Lease(this, next));
        }
    }
}

internal static class GearWinCore
{
    private static readonly OperationCoordinator Coordinator = new(() => PerformanceProfileService.Current.MaxParallelScans);

    static GearWinCore() => PerformanceProfileService.Changed += Coordinator.PolicyChanged;

    internal static Task<IDisposable> EnterAsync(CoreWork kind, CorePriority priority = CorePriority.User, CancellationToken ct = default)
        => Coordinator.AcquireAsync(kind, priority, ct);

    internal static async Task<T> RunAsync<T>(CoreWork kind, Func<Task<T>> work, CorePriority priority = CorePriority.User, CancellationToken ct = default)
    {
        using var lease = await EnterAsync(kind, priority, ct);
        ct.ThrowIfCancellationRequested();
        return await work();
    }
}
