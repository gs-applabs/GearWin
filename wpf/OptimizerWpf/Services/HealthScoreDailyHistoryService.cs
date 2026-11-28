using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace OptimizerWpf.Services
{
    public record HealthScoreDailySnapshot(string Date, int Score, int IssueCount);

    // Persisted daily trend, distinct from the in-session sparkline.
    public static class HealthScoreDailyHistoryService
    {
        private static readonly object Gate = new();
        private static string? _recordedDate;
        private static readonly string StorePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OptimizerWpf", "HealthScoreDailyHistory.json");

        internal static bool Valid(HealthScoreDailySnapshot? row) =>
            row != null && DailyHistoryStorage.ValidDate(row.Date) && row.Score is >= 0 and <= 100 && row.IssueCount >= 0;

        private static List<HealthScoreDailySnapshot> Load()
        {
            var rows = DailyHistoryStorage.Read(StorePath, () => new List<HealthScoreDailySnapshot>());
            if (rows.Any(x => !Valid(x))) throw new InvalidDataException("Invalid health history; original retained.");
            return rows;
        }

        public static void RecordIfNeeded(int score, int issueCount)
        {
            var today = DailyHistoryStorage.Today;
            lock (Gate)
            {
                if (_recordedDate == today) return;
                var entry = new HealthScoreDailySnapshot(today, score, issueCount);
                if (!Valid(entry)) return;
                try
                {
                    using var lease = DailyHistoryStorage.TryLease("Score");
                    if (lease == null) return;
                    var rows = Load();
                    if (!rows.Any(r => r.Date == today))
                    {
                        rows.Add(entry);
                        DailyHistoryStorage.Write(StorePath, DailyHistoryStorage.Retain(rows, r => r.Date));
                    }
                    _recordedDate = today; // only after successful read/write
                }
                catch (Exception ex) { Trace.TraceWarning("Health history not saved: " + ex.Message); }
            }
        }

        public static IReadOnlyList<HealthScoreDailySnapshot> GetHistory()
        {
            lock (Gate)
            {
                try { return DailyHistoryStorage.Retain(Load(), r => r.Date); }
                catch (Exception ex) { Trace.TraceWarning("Health history unavailable: " + ex.Message); return Array.Empty<HealthScoreDailySnapshot>(); }
            }
        }
    }
}
