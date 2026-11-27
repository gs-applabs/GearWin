using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace OptimizerWpf.Services
{
    // One identity per operation. Legacy calls are isolated by async execution flow;
    // new code should use BeginScope with deterministic disposal.
    public static class StatusService
    {
        public static event Action<string, bool>? Changed;

        public sealed class Entry
        {
            public string Message = "";
            public readonly DateTime Started = DateTime.UtcNow;
            internal bool Legacy;
        }

        public static readonly TimeSpan StaleAfter = TimeSpan.FromHours(3);
        private static readonly List<Entry> _active = new();
        private static readonly object _lock = new();
        private sealed record LegacyNode(Entry Entry, LegacyNode? Parent);
        private static readonly AsyncLocal<LegacyNode?> Legacy = new();
        private static string _idleMessage = "";
        private static string _notice = "";

        // Only abandoned legacy entries can expire. A tracked backup may run for hours.
        private static void Prune() => _active.RemoveAll(e => e.Legacy && DateTime.UtcNow - e.Started > StaleAfter);

        public static (string Message, bool Busy) Snapshot()
        {
            lock (_lock)
            {
                Prune();
                bool busy = _active.Count != 0;
                var text = busy ? Combine() : _idleMessage;
                if (busy && !string.IsNullOrWhiteSpace(_notice)) text += "  •  " + _notice;
                return (text, busy);
            }
        }

        private static void Publish()
        {
            var (message, busy) = Snapshot();
            // A faulty or closing view must not prevent service work or lease release.
            var subscribers = Changed;
            if (subscribers == null) return;
            foreach (Action<string, bool> subscriber in subscribers.GetInvocationList())
            {
                try { subscriber(message, busy); }
                catch (Exception ex) { System.Diagnostics.Trace.TraceError(ex.Message); }
            }
        }

        public static void SetBusy(string message)
        {
            var entry = new Entry { Message = message, Legacy = true };
            lock (_lock) { Prune(); _active.Add(entry); _notice = ""; }
            Legacy.Value = new LegacyNode(entry, Legacy.Value);
            Publish();
        }

        public static void SetIdle(string message)
        {
            var node = Legacy.Value;
            lock (_lock)
            {
                Prune();
                // Never complete somebody else's scope, even for an unmatched legacy call.
                if (node != null) { _active.Remove(node.Entry); Legacy.Value = node.Parent; }
                _idleMessage = message;
                _notice = "";
            }
            Publish();
        }

        public static void Report(string message)
        {
            lock (_lock)
            {
                Prune();
                if (_active.Count == 0) _idleMessage = message;
                else _notice = message;
            }
            Publish();
        }

        public static int ActiveCount { get { lock (_lock) { Prune(); return _active.Count; } } }

        public sealed class Scope : IDisposable
        {
            private readonly Entry _entry;
            private bool _ended;
            internal Scope(Entry e) { _entry = e; }

            public void Update(string message)
            {
                lock (_lock)
                {
                    if (_ended || (_entry.Message == message && _notice.Length == 0)) return;
                    _entry.Message = message;
                    _notice = "";
                }
                Publish();
            }

            public void End(string finalMessage)
            {
                lock (_lock)
                {
                    if (_ended) return;
                    _ended = true;
                    _active.Remove(_entry);
                    Prune();
                    if (!string.IsNullOrWhiteSpace(finalMessage)) _idleMessage = finalMessage;
                    else if (_active.Count == 0) _idleMessage = "";
                    _notice = "";
                }
                Publish();
            }

            public void Dispose() => End("");
        }

        public static Scope BeginScope(string message)
        {
            var entry = new Entry { Message = message };
            lock (_lock) { Prune(); _active.Add(entry); _notice = ""; }
            Publish();
            return new Scope(entry);
        }

        private static string Combine() => string.Join("  •  ", _active.Select(e => e.Message).Where(m => !string.IsNullOrWhiteSpace(m)).Distinct());
    }
}
