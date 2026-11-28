using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace OptimizerWpf.Services
{
    public record DiskHealthSnapshot(string Date, string Drive, string Health, double FreeGb, double TotalGb);

    public static class DiskHealthHistoryService
    {
        private static readonly object Gate = new();
        private static string? _cacheDate;
        private static readonly HashSet<string> RecordedToday = new(StringComparer.OrdinalIgnoreCase);
        private static readonly string StorePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OptimizerWpf", "DiskHealthHistory.json");

        private static bool ValidDrive(string? drive) => !string.IsNullOrWhiteSpace(drive) && drive.Length <= 128;
        internal static bool Valid(DiskHealthSnapshot? row) =>
            row != null && DailyHistoryStorage.ValidDate(row.Date) && ValidDrive(row.Drive) &&
            row.Health != null && row.Health.Length <= 256 && double.IsFinite(row.FreeGb) &&
            double.IsFinite(row.TotalGb) && row.FreeGb >= 0 && row.TotalGb >= row.FreeGb;

        private static Dictionary<string, List<DiskHealthSnapshot>> Load()
        {
            var raw = DailyHistoryStorage.Read(StorePath, () => new Dictionary<string, List<DiskHealthSnapshot>>());
            if (raw.Count > DailyHistoryStorage.MaxDrives) throw new InvalidDataException("Too many disk histories; original retained.");
            var result = new Dictionary<string, List<DiskHealthSnapshot>>(StringComparer.OrdinalIgnoreCase);
            foreach (var (key, rows) in raw)
            {
                if (!ValidDrive(key) || rows == null || rows.Any(row => !Valid(row) ||
                    !string.Equals(row.Drive, key, StringComparison.OrdinalIgnoreCase)) || !result.TryAdd(key, rows))
                    throw new InvalidDataException("Invalid or ambiguous disk history; original retained.");
            }
            return result;
        }

        public static void RecordIfNeeded(IEnumerable<(string Drive, string Health, double FreeGb, double TotalGb)> drives)
        {
            lock (Gate)
            {
                string today = DailyHistoryStorage.Today;
                if (_cacheDate != today) { _cacheDate = today; RecordedToday.Clear(); }
                try
                {
                    var requested = drives.Take(DailyHistoryStorage.MaxDrives + 1)
                        .Select(d => new DiskHealthSnapshot(today, d.Drive, d.Health, d.FreeGb, d.TotalGb)).ToList();
                    if (requested.Count > DailyHistoryStorage.MaxDrives || requested.Any(r => !Valid(r))) return;
                    var pending = requested.Where(r => !RecordedToday.Contains(r.Drive))
                        .DistinctBy(r => r.Drive, StringComparer.OrdinalIgnoreCase).ToList();
                    if (pending.Count == 0) return;
                    using var lease = DailyHistoryStorage.TryLease("Disk");
                    if (lease == null) return;
                    var all = Load();
                    if (all.Count + pending.Count(r => !all.ContainsKey(r.Drive)) > DailyHistoryStorage.MaxDrives)
                        throw new InvalidDataException("Disk history limit reached; original retained.");
                    bool changed = false;
                    foreach (var entry in pending)
                    {
                        if (!all.TryGetValue(entry.Drive, out var rows))
                            all[entry.Drive] = rows = new List<DiskHealthSnapshot>();
                        if (rows.Any(r => r.Date == today)) continue;
                        rows.Add(entry);
                        all[entry.Drive] = DailyHistoryStorage.Retain(rows, r => r.Date);
                        changed = true;
                    }
                    if (changed) DailyHistoryStorage.Write(StorePath, all);
                    foreach (var entry in pending) RecordedToday.Add(entry.Drive);
                }
                catch (Exception ex) { Trace.TraceWarning("Disk history not saved: " + ex.Message); }
            }
        }

        public static IReadOnlyList<DiskHealthSnapshot> GetHistory(string drive) =>
            GetAll().TryGetValue(drive, out var rows) ? rows : Array.Empty<DiskHealthSnapshot>();

        public static IReadOnlyDictionary<string, List<DiskHealthSnapshot>> GetAll()
        {
            lock (Gate)
            {
                try
                {
                    return Load().ToDictionary(x => x.Key, x => DailyHistoryStorage.Retain(x.Value, r => r.Date), StringComparer.OrdinalIgnoreCase);
                }
                catch (Exception ex)
                {
                    Trace.TraceWarning("Disk history unavailable: " + ex.Message);
                    return new Dictionary<string, List<DiskHealthSnapshot>>();
                }
            }
        }
    }
}
