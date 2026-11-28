using System;
using System.Threading;

namespace OptimizerWpf.Services;

// No timer and no worker of its own. The owner chooses how to release
// the resource; the callback is deliberately outside the counter lock.
internal sealed class ResourceDemand
{
    private readonly object _gate = new();
    private readonly Action _onIdle;
    private int _count;
    private bool _stopped;

    internal ResourceDemand(Action onIdle) => _onIdle = onIdle;

    internal bool IsIdle { get { lock (_gate) return _count == 0; } }

    internal IDisposable? Acquire()
    {
        lock (_gate)
        {
            if (_stopped) return null;
            checked { _count++; }
            return new Lease(this);
        }
    }

    private void Release()
    {
        bool idle;
        lock (_gate) idle = --_count == 0;
        if (idle) _onIdle();
    }

    internal void Stop()
    {
        lock (_gate) _stopped = true;
    }

    private sealed class Lease : IDisposable
    {
        private ResourceDemand? _owner;
        internal Lease(ResourceDemand owner) => _owner = owner;
        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.Release();
    }
}
