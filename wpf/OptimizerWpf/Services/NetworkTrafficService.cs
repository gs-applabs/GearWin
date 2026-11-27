using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Session;

namespace OptimizerWpf.Services
{
    public record ProcessNetworkUsage(int Pid, string ProcessName, double DownloadKBps, double UploadKBps);

    // ΝΕΟ - roadmap "Κίνηση δικτύου ανά εφαρμογή" (v4.3.5). Δεν υπάρχει καμία WMI/perf-counter κλάση
    // στα Windows που να εκθέτει bytes-ανά-διεργασία (το ΙΔΙΟ API που χρησιμοποιεί εσωτερικά η στήλη
    // "Δίκτυο" της Διαχείρισης Εργασιών) - το μόνο επίσημα τεκμηριωμένο μονοπάτι είναι ETW (Event
    // Tracing for Windows), μέσω του "NT Kernel Logger"'s Network TCP/IP provider. Το TraceEvent
    // (Microsoft.Diagnostics.Tracing.TraceEvent, MIT, dotnet org) είναι η καθιερωμένη βιβλιοθήκη γι'
    // αυτό - καταναλώνει ΑΠΕΥΘΕΙΑΣ τα ίδια kernel events, καμία custom driver/hook.
    //
    // Never replace a kernel logger owned by another tool. Unavailable tracing is reported.
    public static class NetworkTrafficService
    {
        private sealed class Counter { public long Sent; public long Recv; }

        private static readonly object Lock = new();
        private static ConcurrentDictionary<int, Counter> Accum = new();
        private static long _sampleAt;
        private static TraceEventSession? _session;
        private static Thread? _thread;

        public static bool IsRunning { get; private set; }
        public static string? LastError { get; private set; }

        public static bool Start()
        {
            lock (Lock)
            {
                if (IsRunning) return true;
                Accum = new ConcurrentDictionary<int, Counter>();
                var counters = Accum;
                _sampleAt = Environment.TickCount64;
                try
                {
                    if (TraceEventSession.GetActiveSessionNames().Contains(KernelTraceEventParser.KernelSessionName, StringComparer.OrdinalIgnoreCase))
                        throw new InvalidOperationException("The kernel trace session is already owned by another tool.");
                    var session = new TraceEventSession(KernelTraceEventParser.KernelSessionName, TraceEventSessionOptions.Create | TraceEventSessionOptions.NoRestartOnCreate)
                    {
                        StopOnDispose = true,
                    };
                    _session = session;
                    session.EnableKernelProvider(KernelTraceEventParser.Keywords.NetworkTCPIP, KernelTraceEventParser.Keywords.None);

                    session.Source.Kernel.TcpIpSend += d => AddBytes(counters, d.ProcessID, d.size, sent: true);
                    session.Source.Kernel.TcpIpRecv += d => AddBytes(counters, d.ProcessID, d.size, sent: false);
                    session.Source.Kernel.TcpIpSendIPV6 += d => AddBytes(counters, d.ProcessID, d.size, sent: true);
                    session.Source.Kernel.TcpIpRecvIPV6 += d => AddBytes(counters, d.ProcessID, d.size, sent: false);
                    session.Source.Kernel.UdpIpSend += d => AddBytes(counters, d.ProcessID, d.size, sent: true);
                    session.Source.Kernel.UdpIpRecv += d => AddBytes(counters, d.ProcessID, d.size, sent: false);
                    session.Source.Kernel.UdpIpSendIPV6 += d => AddBytes(counters, d.ProcessID, d.size, sent: true);
                    session.Source.Kernel.UdpIpRecvIPV6 += d => AddBytes(counters, d.ProcessID, d.size, sent: false);

                    _session = session;
                    // Το Source.Process() μπλοκάρει μέχρι να σταματήσει το session - ΔΙΚΟ του, μη-
                    // threadpool νήμα (IsBackground ώστε να μην εμποδίζει το κλείσιμο της εφαρμογής
                    // αν το Stop() δεν προλάβει να κληθεί για οποιονδήποτε λόγο).
                    _thread = new Thread(() =>
                    {
                        string? error = null;
                        try { session.Source.Process(); } catch (Exception ex) { error = ex.Message; }
                        finally
                        {
                            ThreadPool.QueueUserWorkItem(_ =>
                            {
                                lock (Lock)
                                {
                                    if (!ReferenceEquals(_session, session)) return;
                                    LastError = error ?? "Trace session ended.";
                                    IsRunning = false;
                                    try { session.Dispose(); } catch { }
                                    _session = null;
                                    _thread = null;
                                }
                            });
                        }
                    })
                    {
                        IsBackground = true,
                        Name = "NetworkTrafficEtw",
                    };
                    IsRunning = true;
                    LastError = null;
                    _thread.Start();
                    return true;
                }
                catch (Exception ex)
                {
                    LastError = ex.Message;
                    try { _session?.Dispose(); } catch { /* best effort */ }
                    _session = null;
                    IsRunning = false;
                    return false;
                }
            }
        }

        public static void Stop()
        {
            lock (Lock)
            {
                if (!IsRunning) return;
                try { _session?.Stop(true); } catch { /* best effort */ }
                try { _session?.Dispose(); } catch { /* best effort */ }
                _session = null;
                _thread = null;
                IsRunning = false;
                Accum.Clear();
            }
        }

        private static void AddBytes(ConcurrentDictionary<int, Counter> counters, int pid, int size, bool sent)
        {
            if (size <= 0) return;
            if (!counters.TryGetValue(pid, out var counter))
            {
                if (counters.Count >= 8192) return;
                counter = counters.GetOrAdd(pid, _ => new Counter());
            }
            if (sent) Interlocked.Add(ref counter.Sent, size);
            else Interlocked.Add(ref counter.Recv, size);
        }

        // Καλείται περιοδικά από το UI (π.χ. κάθε 1s) - διαβάζει ΚΑΙ μηδενίζει τους μετρητές κάθε
        // διεργασίας, ώστε η επόμενη κλήση να αντιπροσωπεύει μόνο το επόμενο χρονικό παράθυρο
        // (ρυθμός, όχι αθροιστικό σύνολο από την αρχή της σάρωσης).
        public static System.Collections.Generic.IReadOnlyList<ProcessNetworkUsage> SampleAndReset(double windowSeconds)
        {
            var result = new System.Collections.Generic.List<ProcessNetworkUsage>();
            var now = Environment.TickCount64;
            var previous = Interlocked.Exchange(ref _sampleAt, now);
            windowSeconds = (now - previous) / 1000.0;
            if (previous == 0 || windowSeconds <= 0) return result;

            foreach (var pid in Accum.Keys.ToArray())
            {
                if (!Accum.TryGetValue(pid, out var counter)) continue;
                var sent = Interlocked.Exchange(ref counter.Sent, 0);
                var recv = Interlocked.Exchange(ref counter.Recv, 0);
                if (sent == 0 && recv == 0) continue;

                result.Add(new ProcessNetworkUsage(pid, ResolveProcessName(pid), BytesToKBps(recv, windowSeconds), BytesToKBps(sent, windowSeconds)));
            }

            return result.OrderByDescending(r => r.DownloadKBps + r.UploadKBps).ToList();
        }

        // Καθαρή, testable μαθηματική λογική - ξεχωρισμένη από το I/O (process lookup) παραπάνω.
        internal static double BytesToKBps(long bytes, double windowSeconds) =>
            windowSeconds <= 0 ? 0 : bytes / 1024.0 / windowSeconds;

        private static string ResolveProcessName(int pid)
        {
            if (pid == 0) return "System Idle Process";
            try { using var process = Process.GetProcessById(pid); return process.ProcessName; }
            catch { return $"PID {pid}"; } // η διεργασία μπορεί να έχει ήδη τερματίσει
        }
    }
}
