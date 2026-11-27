using System.Collections.Generic;
using System.Linq;

namespace OptimizerWpf.Services
{
    // ΝΕΟ - roadmap "Ιστορικό χρήσης πόρων" - απλό, στη μνήμη (ΟΧΙ persisted σε δίσκο - δεν χρειάζεται
    // να επιβιώνει σε επανεκκίνηση, μόνο "στον χρόνο μέσα σε αυτή τη συνεδρία" όπως ζητά το roadmap
    // item) ιστορικό μνήμης ανά όνομα διεργασίας, ενημερώνεται σε κάθε "Ανανέωση" της λίστας
    // Διεργασιών (SystemView) - static ώστε να επιβιώνει σε αλλαγές καρτέλας (κάθε SystemView instance
    // είναι καινούριο, βλ. ήδη καθιερωμένη σύμβαση "νέο view ανά επίσκεψη καρτέλας" αλλού στο project).
    public static class ProcessHistoryService
    {
        private const int MaxSamples = 20;
        internal const int MaxProcesses = 512;
        private static readonly object Gate = new();
        private static readonly LinkedList<string> Recency = new();
        private static readonly Dictionary<string, (List<double> Samples, LinkedListNode<string> Node)> History = new(System.StringComparer.OrdinalIgnoreCase);
        internal static int TrackedCount { get { lock (Gate) return History.Count; } }

        public static void RecordSample(string processName, double workingSetMb)
        {
            if (string.IsNullOrWhiteSpace(processName) || !double.IsFinite(workingSetMb) || workingSetMb < 0) return;
            lock (Gate)
            {
                if (!History.TryGetValue(processName, out var item))
                {
                    if (History.Count >= MaxProcesses && Recency.First is { } oldest)
                    {
                        History.Remove(oldest.Value);
                        Recency.RemoveFirst();
                    }
                    item = (new List<double>(MaxSamples), Recency.AddLast(processName));
                    History.Add(processName, item);
                }
                else { Recency.Remove(item.Node); Recency.AddLast(item.Node); }
                if (item.Samples.Count >= MaxSamples) item.Samples.RemoveAt(0);
                item.Samples.Add(workingSetMb);
            }
        }

        // Unicode block sparkline (▁▂▃▄▅▆▇█) - καμία εξάρτηση σε γραφική βιβλιοθήκη, διαβάζεται
        // απευθείας μέσα σε ένα απλό TextBlock δίπλα στο τρέχον MB της κάθε διεργασίας.
        private static readonly char[] Blocks = { '▁', '▂', '▃', '▄', '▅', '▆', '▇', '█' };

        public static string GetSparkline(string processName)
        {
            double[] list;
            lock (Gate)
            {
                if (!History.TryGetValue(processName, out var item) || item.Samples.Count < 2) return "";
                list = item.Samples.ToArray();
            }
            var min = list.Min(); var max = list.Max();
            if (max - min < 0.01) return new string(Blocks[3], list.Length);
            return new string(list.Select(v => Blocks[System.Math.Clamp((int)((v - min) / (max - min) * (Blocks.Length - 1)), 0, Blocks.Length - 1)]).ToArray());
        }
    }
}
