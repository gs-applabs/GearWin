using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace OptimizerWpf.Services;

// The existing PerformanceProfileService remains the only adaptive governor.
// This coordinator has no timer, polling thread, or persistent activity log.
// Leases are acquired at service boundaries, never recursively inside a lease.
internal enum CoreWork { CleanupScan, Cleanup, Memory, Repair, UpdateScan, Backup, Registry, HardwareScan, HealthScan, SoftwareChange, ProfileChange, NetworkChange, StartupChange, Diagnostics, Benchmark, FileProtection, TransferScan, TweakChange, SystemSnapshot, ProcessSnapshot }
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
    private bool _stopping;
    private long _softwareEpoch;
    internal long SoftwareEpoch => Interlocked.Read(ref _softwareEpoch);
    internal OperationCoordinator(Func<int> capacity) => _capacity = capacity;

    internal static bool IsHeavyMutation(CoreWork kind) => kind is CoreWork.Cleanup or CoreWork.Memory or CoreWork.Repair or CoreWork.Backup or CoreWork.Registry or CoreWork.SoftwareChange or CoreWork.ProfileChange or CoreWork.NetworkChange or CoreWork.StartupChange or CoreWork.Benchmark or CoreWork.FileProtection or CoreWork.TweakChange;

    // These domains only observe running processes or hardware metadata.
    // Generic Diagnostics can traverse files and is NOT a lightweight snapshot.
    internal static bool IsSnapshot(CoreWork kind) =>
        kind is CoreWork.HardwareScan or CoreWork.SystemSnapshot or CoreWork.ProcessSnapshot;

    internal static bool Conflicts(CoreWork a, CoreWork b)
    {
        if (a == b) return true;
        // Benchmark integrity requires quiet disk activity, including hardware inventory.
        if (a == CoreWork.Benchmark || b == CoreWork.Benchmark)
            return a != CoreWork.ProcessSnapshot && b != CoreWork.ProcessSnapshot;
        if (IsSnapshot(a) || IsSnapshot(b)) return false;
        // Keep unknown mutation scopes conservative until resource identities are known.
        return IsHeavyMutation(a) || IsHeavyMutation(b);
    }

    private bool HasCapacity(CoreWork kind, CorePriority priority)
    {
        int automatic = Math.Clamp(_capacity(), 1, 3);
        if (IsSnapshot(kind))
        {
            // Reserve a small independent read-only lane; a user must be able to
            // inspect progress and processes even during a lengthy mutation.
            int snapshots = _active.Count(r => IsSnapshot(r.Kind));
            return snapshots < (priority == CorePriority.User ? 2 : 1);
        }
        int regular = _active.Count(r => !IsSnapshot(r.Kind));
        // Explicit compatible user tools may coexist even in Low tier.
        // Adaptive budget throttles optional work; never revoke running leases.
        int limit = priority == CorePriority.User ? Math.Max(2, automatic) : automatic;
        return regular < limit;
    }

    internal bool HasHeavyWork { get { lock (_gate) return _active.Any(r => IsHeavyMutation(r.Kind)); } }
    internal int ActiveCount { get { lock (_gate) return _active.Count; } }
    internal int PendingCount { get { lock (_gate) return _pending.Count; } }

    internal async Task<IDisposable> AcquireAsync(CoreWork kind, CorePriority priority, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var request = new Request(kind, priority);
        lock (_gate)
        {
            if (_stopping) throw new OperationCanceledException("GearWin is shutting down.");
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

    // Optional periodic work must not accumulate queued requests or delay the user.
    internal IDisposable? TryAcquireBackground(CoreWork kind)
    {
        lock (_gate)
        {
            if (_stopping || !HasCapacity(kind, CorePriority.Background) ||
                _active.Any(a => Conflicts(a.Kind, kind)) ||
                _pending.Any(p => p.Priority == CorePriority.User || Conflicts(p.Kind, kind))) return null;
            var request = new Request(kind, CorePriority.Background);
            _active.Add(request);
            return new Lease(this, request);
        }
    }

    private void Release(Request request)
    {
        lock (_gate)
        {
            if (_active.Remove(request) && request.Kind == CoreWork.SoftwareChange)
                Interlocked.Increment(ref _softwareEpoch);
            Dispatch();
        }
    }

    internal void PolicyChanged() { lock (_gate) Dispatch(); }

    internal bool TryStopWhenSafe()
    {
        lock (_gate)
        {
            if (_active.Any(r => IsHeavyMutation(r.Kind))) return false;
            StopAccepting();
            return true;
        }
    }

    internal void StopAccepting()
    {
        lock (_gate)
        {
            _stopping = true;
            foreach (var request in _pending) request.Completion.TrySetCanceled();
            _pending.Clear();
            // Never terminate an already running Windows repair or backup.
        }
    }

    // User work wins over queued background work; running work is never interrupted.
    // Do not jump ahead of a blocked user request with conflicting background work.
    private void Dispatch()
    {
        if (_stopping) return;
        while (true)
        {
            var next = _pending.FirstOrDefault(r => r.Priority == CorePriority.User &&
                HasCapacity(r.Kind, r.Priority) && !_active.Any(a => Conflicts(a.Kind, r.Kind)) &&
                !_pending.TakeWhile(p => !ReferenceEquals(p, r)).Any(p =>
                    p.Priority == CorePriority.User && Conflicts(p.Kind, r.Kind)));
            next ??= _pending.FirstOrDefault(r => r.Priority == CorePriority.Background &&
                HasCapacity(r.Kind, r.Priority) && !_active.Any(a => Conflicts(a.Kind, r.Kind)) &&
                !_pending.Any(p => p.Priority == CorePriority.User &&
                    (Conflicts(p.Kind, r.Kind) || IsSnapshot(p.Kind) == IsSnapshot(r.Kind))));
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

    internal static long SoftwareEpoch => Coordinator.SoftwareEpoch;
    internal static void Stop() => Coordinator.StopAccepting();
    private sealed class CombinedLease : IDisposable
    {
        private IDisposable? _local;
        private IDisposable? _process;
        internal CombinedLease(IDisposable local, IDisposable process) { _local = local; _process = process; }
        public void Dispose()
        {
            try { Interlocked.Exchange(ref _process, null)?.Dispose(); }
            finally { Interlocked.Exchange(ref _local, null)?.Dispose(); }
        }
    }

    // Synchronous optional callback only (auto Gaming Mode). Never await while holding it.
    internal static IDisposable? TryEnterBackground(CoreWork kind)
    {
        var local = Coordinator.TryAcquireBackground(kind);
        if (local == null || !OperationCoordinator.IsHeavyMutation(kind)) return local;
        try
        {
            var process = CoreProcessLease.TryAcquire(CoreProcessLease.Name);
            if (process != null) return new CombinedLease(local, process);
        }
        catch (Exception ex) { System.Diagnostics.Trace.TraceError(ex.Message); }
        local.Dispose();
        return null;
    }

    private static string WorkLabel(CoreWork kind) => kind switch
    {
        CoreWork.CleanupScan => LanguageService.Current == "el" ? "Σάρωση καθαρισμού" : "Cleanup scan",
        CoreWork.Cleanup => LanguageService.Current == "el" ? "Καθαρισμός" : "Cleanup",
        CoreWork.Memory => LanguageService.Current == "el" ? "Συντήρηση RAM" : "Memory maintenance",
        CoreWork.Repair => LanguageService.Current == "el" ? "Επισκευή Windows" : "Windows repair",
        CoreWork.UpdateScan => LanguageService.Current == "el" ? "Έλεγχος ενημερώσεων" : "Update scan",
        CoreWork.Backup => LanguageService.Current == "el" ? "Αντίγραφο ασφαλείας" : "Backup",
        CoreWork.Registry => LanguageService.Current == "el" ? "Μητρώο" : "Registry",
        CoreWork.HealthScan => LanguageService.Current == "el" ? "Έλεγχος υγείας" : "Health check",
        CoreWork.SoftwareChange => LanguageService.Current == "el" ? "Αλλαγές εφαρμογών" : "Software changes",
        CoreWork.ProfileChange => LanguageService.Current == "el" ? "Προφίλ απόδοσης" : "Performance profile",
        CoreWork.NetworkChange => LanguageService.Current == "el" ? "Ρυθμίσεις δικτύου" : "Network changes",
        CoreWork.StartupChange => LanguageService.Current == "el" ? "Ρυθμίσεις εκκίνησης" : "Startup changes",
        CoreWork.Diagnostics => LanguageService.Current == "el" ? "Διαγνωστικός έλεγχος" : "Diagnostics",
        CoreWork.SystemSnapshot => LanguageService.Current == "el" ? "Πληροφορίες συστήματος" : "System information",
        CoreWork.ProcessSnapshot => LanguageService.Current == "el" ? "Λίστα διεργασιών" : "Process list",
        CoreWork.Benchmark => LanguageService.Current == "el" ? "Μέτρηση απόδοσης" : "Benchmark",
        CoreWork.FileProtection => LanguageService.Current == "el" ? "Προστασία αρχείων" : "File protection",
        CoreWork.TransferScan => LanguageService.Current == "el" ? "Έλεγχος μεταφοράς" : "Transfer check",
        CoreWork.TweakChange => LanguageService.Current == "el" ? "Ρυθμίσεις Windows" : "Windows settings",
        _ => LanguageService.Current == "el" ? "Έλεγχος υλικού" : "Hardware check"
    };

    // Track pending and running work from admission to disposal, including exceptions.
    internal static async Task<IDisposable> EnterAsync(CoreWork kind, CorePriority priority = CorePriority.User, CancellationToken ct = default)
    {
        var label = WorkLabel(kind);
        var status = StatusService.BeginScope(label + (LanguageService.Current == "el" ? " (αναμονή)" : " (waiting)"));
        IDisposable? local = null, process = null;
        try
        {
            local = await Coordinator.AcquireAsync(kind, priority, ct).ConfigureAwait(false);
            if (OperationCoordinator.IsHeavyMutation(kind))
            {
                process = await CoreProcessLease.AcquireAsync(CoreProcessLease.Name, TimeSpan.FromSeconds(30), ct).ConfigureAwait(false);
            }
            ct.ThrowIfCancellationRequested();
            status.Update(label + (LanguageService.Current == "el" ? " (εκτέλεση)" : " (running)"));
            var work = process == null ? local : new CombinedLease(local, process);
            return new CombinedLease(status, work);
        }
        catch
        {
            try { process?.Dispose(); }
            finally { try { local?.Dispose(); } finally { status.Dispose(); } }
            throw;
        }
    }

    internal static bool TryStopWhenSafe() => Coordinator.TryStopWhenSafe();

    internal static async Task RunAsync(CoreWork kind, Func<Task> work, CorePriority priority = CorePriority.User, CancellationToken ct = default)
    {
        using var lease = await EnterAsync(kind, priority, ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        await work().ConfigureAwait(false);
    }

    internal static async Task<T> RunAsync<T>(CoreWork kind, Func<Task<T>> work, CorePriority priority = CorePriority.User, CancellationToken ct = default)
    {
        using var lease = await EnterAsync(kind, priority, ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        return await work().ConfigureAwait(false);
    }
}
