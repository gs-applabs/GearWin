using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace OptimizerWpf.Services;

// Legacy backups are advisory migration evidence only. New privileged
// operations use protected typed journals; never rewrite or delete this file.
public static class TweakBackupService
{
    private static string StorePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "OptimizerWpf", "TweakBackup.json");

    internal static Dictionary<string, string> ParseLegacy(Stream stream)
    {
        using var doc = JsonDocument.Parse(stream);
        if (doc.RootElement.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Invalid legacy backup.");
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in doc.RootElement.EnumerateObject())
        {
            if (item.Value.ValueKind != JsonValueKind.String ||
                !result.TryAdd(item.Name, item.Value.GetString()!))
                throw new InvalidDataException("Invalid or duplicate legacy backup entry.");
        }
        return result;
    }

    public static string? GetBackup(string key)
    {
        var path = StorePath;
        try { _ = File.GetAttributes(path); }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
        if (!MaintenancePaths.IsOrdinaryLocalPath(path)) throw new IOException("Unsafe legacy backup path.");
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > 1024 * 1024) throw new InvalidDataException("Legacy backup is oversized.");
        return ParseLegacy(stream).TryGetValue(key, out var value) ? value : null;
    }
}
