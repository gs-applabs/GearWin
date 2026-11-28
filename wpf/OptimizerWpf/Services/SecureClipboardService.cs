using System;
using System.Windows;
using System.Windows.Threading;

namespace OptimizerWpf.Services
{
    // Αντιγραφή ευαίσθητου κειμένου (κωδικοί browser / Wi-Fi) στο πρόχειρο ΜΕ αυτόματο καθαρισμό: πριν, ο κωδικός
    // έμενε στο πρόχειρο επ' άπειρον (και στο ιστορικό Win+V) μέχρι να αντικατασταθεί από κάτι άλλο. Καθαρίζουμε
    // μετά από λίγα δευτερόλεπτα ΜΟΝΟ αν το πρόχειρο περιέχει ακόμα ΑΚΡΙΒΩΣ το δικό μας κείμενο (δεν πειράζουμε
    // κάτι που αντέγραψε ο χρήστης στο μεταξύ). Η εξαίρεση από το ιστορικό/cloud πρόχειρο γίνεται με τα επίσημα
    // formats "ExcludeClipboardContentFromMonitorProcessing" και "CanIncludeInClipboardHistory"=0.
    public static class SecureClipboardService
    {
        private const int ClearAfterSeconds = 30;
        private static DispatcherTimer? _timer;

        internal static bool ShouldClear(uint ownedSequence, uint currentSequence) =>
            ownedSequence != 0 && ownedSequence == currentSequence;

        public static void SetSensitiveText(string text)
        {
            var data = new DataObject();
            data.SetText(text);
            // 0 = μην συμπεριληφθεί στο ιστορικό (Win+V) / cloud clipboard.
            data.SetData("CanIncludeInClipboardHistory", new System.IO.MemoryStream(BitConverter.GetBytes(0)));
            data.SetData("CanUploadToCloudClipboard", new System.IO.MemoryStream(BitConverter.GetBytes(0)));
            data.SetData("ExcludeClipboardContentFromMonitorProcessing", new System.IO.MemoryStream(BitConverter.GetBytes(1)));
            Clipboard.SetDataObject(data, copy: true);

            // Track ownership by clipboard generation, never retain a password
            // in a timer closure or clear a later copy of the same text.
            _timer?.Stop();
            _timer = null;
            uint sequence = ClipboardManagerService.GetClipboardSequenceNumber();
            if (sequence == 0) return;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(ClearAfterSeconds) };
            _timer = timer;
            EventHandler? tick = null;
            tick = (_, _) =>
            {
                timer.Stop();
                timer.Tick -= tick;
                if (!ReferenceEquals(_timer, timer)) return;
                _timer = null;
                try
                {
                    if (ShouldClear(sequence, ClipboardManagerService.GetClipboardSequenceNumber()))
                        Clipboard.Clear();
                }
                catch { /* clipboard locked by another application */ }
            };
            timer.Tick += tick;
            timer.Start();
        }
    }
}
