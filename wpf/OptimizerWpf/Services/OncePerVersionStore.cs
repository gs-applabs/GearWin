using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace OptimizerWpf.Services
{
    // One small existing store shared by onboarding, suite offers and release notifications.
    // Read the latest state under a process-wide gate, not a stale instance snapshot.
    // No timer, persistent handle or unbounded path cache.
    public sealed class OncePerVersionStore
    {
        private const int MaxBytes = 64 * 1024;
        private static readonly object Gate = new();
        private readonly string _path;

        public OncePerVersionStore(string path) => _path = Path.GetFullPath(path);

        public static string DefaultPath() => Path.Combine(AppDataDir(), "seen-versions.json");

        public static string AppDataDir() =>
            Environment.GetEnvironmentVariable("GEARWIN_DATA_DIR") is { Length: > 0 } c
                ? c
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OptimizerWpf");

        private Dictionary<string, string> Load()
        {
            try
            {
                using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
                if (stream.Length > MaxBytes) throw new InvalidDataException("Seen-version store exceeds size limit.");
                using var reader = new BinaryReader(stream);
                var bytes = reader.ReadBytes(MaxBytes + 1);
                if (bytes.Length > MaxBytes) throw new InvalidDataException("Seen-version store exceeds size limit.");
                // Older files may contain a UTF-8 BOM.
                var offset = bytes.Length >= 3 && bytes[0] == 0xef && bytes[1] == 0xbb && bytes[2] == 0xbf ? 3 : 0;
                return JsonSerializer.Deserialize<Dictionary<string, string>>(bytes.AsSpan(offset)) ?? new();
            }
            catch (FileNotFoundException) { return new(); }
            catch (DirectoryNotFoundException) { return new(); }
            catch (JsonException) { return new(); } // retain existing malformed-JSON recovery policy
        }

        internal Dictionary<string, string> Snapshot()
        {
            lock (Gate)
            {
                try { return Load(); }
                catch { return new(); }
            }
        }

        public string? Get(string key) => Snapshot().TryGetValue(key, out var value) ? value : null;

        public bool ShouldShow(string key, ReleaseVersion current, bool majorMinorOnly = true)
        {
            var seen = ReleaseVersion.ParseOrNull(Get(key));
            if (seen == null) return true;
            return majorMinorOnly ? current.MajorMinor > seen.MajorMinor : current > seen;
        }

        private void Mutate(Func<Dictionary<string, string>, bool> change)
        {
            lock (Gate)
            {
                try
                {
                    var data = Load(); // inaccessible or oversized files are not replaced
                    if (!change(data)) return;
                    var json = JsonSerializer.Serialize(data);
                    if (Encoding.UTF8.GetByteCount(json) > MaxBytes) return;
                    AppSettingsService.WriteIfChanged(_path, json, null);
                }
                catch { /* Optional notifications and onboarding must not crash the app. */ }
            }
        }

        public void MarkSeen(string key, ReleaseVersion version) => Mutate(data =>
        {
            var old = ReleaseVersion.ParseOrNull(data.TryGetValue(key, out var o) ? o : null);
            if (old != null && old >= version) return false;
            data[key] = version.ToString();
            return true;
        });

        public void SetRaw(string key, string? value) => SetRawBatch((key, value));

        // One atomic replacement for related release cache fields, only when changed.
        internal void SetRawBatch(params (string Key, string? Value)[] values) => Mutate(data =>
        {
            bool changed = false;
            foreach (var (key, value) in values)
            {
                if (value == null) { changed |= data.Remove(key); continue; }
                if (data.TryGetValue(key, out var old) && old == value) continue;
                data[key] = value;
                changed = true;
            }
            return changed;
        });

        public void Forget(string key) => SetRaw(key, null);
    }
}
