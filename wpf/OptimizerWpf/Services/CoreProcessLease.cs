using System;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;

namespace OptimizerWpf.Services;

// Windows mutex ownership is thread-affine. Async callers get a short-lived
// owner thread ONLY while a heavy operation waits/runs; no idle thread, polling,
// or lock-file writes. The OS releases ownership on process death.
internal static class CoreProcessLease
{
    internal static string Name
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();
            var sid = identity.User?.Value ?? throw new InvalidOperationException("Unknown maintenance user.");
            return @"Global\GearWin.CoreHeavy.v1." + sid;
        }
    }

    private sealed class ReleaseSignal : IDisposable
    {
        private TaskCompletionSource<bool>? _signal;
        internal ReleaseSignal(TaskCompletionSource<bool> signal) => _signal = signal;
        public void Dispose() => Interlocked.Exchange(ref _signal, null)?.TrySetResult(true);
    }

    internal static Task<IDisposable> AcquireAsync(string name, TimeSpan timeout, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var ready = new TaskCompletionSource<IDisposable>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = Task.Factory.StartNew(() =>
        {
            try
            {
                using var mutex = new Mutex(false, name);
                bool owned = false;
                try
                {
                    int result;
                    try { result = WaitHandle.WaitAny(new WaitHandle[] { mutex, ct.WaitHandle }, timeout); }
                    catch (AbandonedMutexException ex) when (ex.MutexIndex == 0) { result = 0; }
                    if (result == 0) owned = true;
                    ct.ThrowIfCancellationRequested();
                    if (!owned) throw new TimeoutException("Another GearWin process is running maintenance. Retry when it finishes.");
                    ready.TrySetResult(new ReleaseSignal(release));
                    // A long-running thread owns the kernel mutex across async continuations.
                    release.Task.GetAwaiter().GetResult();
                }
                finally { if (owned) mutex.ReleaseMutex(); }
            }
            catch (OperationCanceledException) { ready.TrySetCanceled(ct); }
            catch (Exception ex) { ready.TrySetException(ex); }
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        return ready.Task;
    }

    // Only for synchronous optional callbacks. Caller must dispose on the same thread.
    internal static IDisposable? TryAcquire(string name)
    {
        var mutex = new Mutex(false, name);
        try
        {
            bool owned;
            try { owned = mutex.WaitOne(0); }
            catch (AbandonedMutexException) { owned = true; }
            if (!owned) { mutex.Dispose(); return null; }
            return new SynchronousLease(mutex);
        }
        catch { mutex.Dispose(); throw; }
    }

    private sealed class SynchronousLease : IDisposable
    {
        private Mutex? _mutex;
        internal SynchronousLease(Mutex mutex) => _mutex = mutex;
        public void Dispose()
        {
            var mutex = Interlocked.Exchange(ref _mutex, null);
            if (mutex == null) return;
            try { mutex.ReleaseMutex(); }
            finally { mutex.Dispose(); }
        }
    }
}
