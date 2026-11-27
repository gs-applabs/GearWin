using System;
using System.Collections.Generic;

namespace OptimizerWpf.Services
{
    // Port του Show-ActionLogWindow / $global:actionLogHistory (Optimizer.ps1 ~9010) - αντί να
    // προστεθεί μια ξεχωριστή κλήση καταγραφής σε ΚΑΘΕ μέθοδο κάθε service (μεγάλη, επαναλαμβανόμενη
    // αλλαγή σε όλο τον κώδικα), γίνεται hook στο ήδη υπάρχον StatusService.Changed - κάθε SetBusy
    // κλήση σε ΟΛΗ την εφαρμογή καταγράφεται αυτόματα με timestamp. Οι SetIdle/"Έτοιμο για χρήση"
    // μεταβάσεις ΔΕΝ καταγράφονται (θόρυβος, όχι ουσιαστικές ενέργειες).
    public static class ActionLogService
    {
        // ΔΙΟΡΘΩΣΗ (έλεγχος RAM/σφαλμάτων): η λίστα ήταν απεριόριστη (μεγάλες συνεδρίες → συνεχής αύξηση μνήμης) και
        // ΧΩΡΙΣ κλείδωμα (το StatusService.Changed μπορεί να έρθει από νήμα παρασκηνίου ενώ το UI διαβάζει τη λίστα →
        // InvalidOperationException "Collection was modified"). Τώρα: κλείδωμα, όριο 500 εγγραφών, snapshot στην ανάγνωση,
        // και το Changed φτάνει πάντα στο UI νήμα (οι συνδρομητές ενημερώνουν στοιχεία διεπαφής).
        private const int MaxEntries = 500;
        internal const int MaxMessageChars = 2048;
        internal static string BoundMessage(string message) => message.Length <= MaxMessageChars ? message : message[..(MaxMessageChars - 1)] + "…";
        private static readonly object Lock = new();
        private static readonly List<string> _entries = new();
        private static string? _lastMessage;
        private static int _refreshQueued;
        public static IReadOnlyList<string> Entries { get { lock (Lock) return _entries.ToArray(); } }
        public static event Action? Changed;

        static ActionLogService()
        {
            StatusService.Changed += (message, isBusy) =>
            {
                lock (Lock)
                {
                    if (!isBusy) { _lastMessage = null; return; }
                    message = BoundMessage(message);
                    if (string.Equals(_lastMessage, message, StringComparison.Ordinal)) return;
                    _lastMessage = message;
                    _entries.Add($"[{DateTime.Now:HH:mm:ss}] {message}");
                    if (_entries.Count > MaxEntries) _entries.RemoveRange(0, _entries.Count - MaxEntries);
                }
                if (Changed == null) return; // history is retained; no UI dispatch without a consumer
                var d = System.Windows.Application.Current?.Dispatcher;
                if (d == null) { Changed?.Invoke(); return; }
                if (d.HasShutdownStarted || d.HasShutdownFinished) return;
                if (System.Threading.Interlocked.Exchange(ref _refreshQueued, 1) != 0) return;
                d.BeginInvoke(new Action(() =>
                {
                    System.Threading.Interlocked.Exchange(ref _refreshQueued, 0);
                    Changed?.Invoke();
                }), System.Windows.Threading.DispatcherPriority.Background);
            };
        }

        // Καλείται μία φορά από το App.xaml.cs OnStartup ώστε ο static constructor να τρέξει από την
        // αρχή της εκκίνησης (χωρίς αυτό, η καταγραφή θα ξεκινούσε μόνο μόλις κάποιος άνοιγε πρώτη
        // φορά το παράθυρο Ιστορικού Ενεργειών - θα έχαναν όλες τις ενέργειες πριν από εκείνη τη στιγμή).
        public static void EnsureStarted() { }
    }
}
