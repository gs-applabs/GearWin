using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace OptimizerWpf.Services;

// No timers, idle I/O, or persistent open handles. Existing history filenames
// and JSON schemas remain compatible. Invalid/oversized files are never reset.
internal static class DailyHistoryStorage
{
    internal const int MaxBytes = 2 * 1024 * 1024;
    internal const int MaxDays = 60;
    internal const int MaxDrives = 128;

    internal static string Today => DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    internal static bool ValidDate(string? date) => DateOnly.TryParseExact(date, "yyyy-MM-dd",
        CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

    internal static T Read<T>(string path, Func<T> empty)
    {
        ValidatePath(path);
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length > MaxBytes) throw new InvalidDataException("History exceeds size limit.");
            // Bound actual bytes even if an external writer changes the file.
            using var reader = new BinaryReader(stream);
            var bytes = reader.ReadBytes(MaxBytes + 1);
            if (bytes.Length > MaxBytes) throw new InvalidDataException("History exceeds size limit.");
            return JsonSerializer.Deserialize<T>(bytes) ?? throw new InvalidDataException("Invalid history.");
        }
        catch (FileNotFoundException) { return empty(); }
        catch (DirectoryNotFoundException) { return empty(); }
    }

    private static void ValidatePath(string path)
    {
        if (!Path.IsPathFullyQualified(path)) throw new IOException("Relative history path.");
        for (var dir = new DirectoryInfo(Path.GetDirectoryName(path)!); dir != null; dir = dir.Parent)
        {
            try
            {
                _ = File.GetAttributes(dir.FullName);
                if (!Directory.Exists(dir.FullName) || !MaintenancePaths.IsOrdinaryLocalPath(dir.FullName))
                    throw new IOException("Unsafe history directory.");
                break;
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
        try
        {
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.Directory) != 0 || !MaintenancePaths.IsOrdinaryLocalPath(path))
                throw new IOException("Unsafe history file.");
        }
        catch (FileNotFoundException) { }
        catch (DirectoryNotFoundException) { }
    }

    internal static void Write<T>(string path, T data)
    {
        string json = JsonSerializer.Serialize(data);
        if (System.Text.Encoding.UTF8.GetByteCount(json) > MaxBytes)
            throw new InvalidDataException("History exceeds size limit.");
        ValidatePath(path);
        AppSettingsService.WriteIfChanged(path, json, null);
    }

    internal static IDisposable? TryLease(string id) =>
        CoreProcessLease.TryAcquire(CoreProcessLease.Name + ".DailyHistory." + id);

    internal static List<T> Retain<T>(IEnumerable<T> rows, Func<T, string> date) =>
        rows.GroupBy(date, StringComparer.Ordinal).Select(g => g.First())
            .OrderBy(date, StringComparer.Ordinal).TakeLast(MaxDays).ToList();
}
