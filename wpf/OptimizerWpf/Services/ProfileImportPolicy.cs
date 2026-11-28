using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace OptimizerWpf.Services;

internal static class ProfileImportPolicy
{
    internal const int MaxBytes = 256 * 1024;
    internal const int MaxKeys = 512;

    // Hibernation is an explicit action, not an automatically replayable
    // profile setting: disabling it removes hiberfil.sys and affects Fast Startup.
    internal static bool CanReplay(SimpleTweak tweak) => tweak.Id != "Hibernate";
    internal static bool RequiresOffConfirmation(SimpleTweak tweak, bool desired) =>
        tweak.Id == "Hibernate" && !desired;

    internal static bool ValidName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 80 || name != name.Trim() ||
            name.EndsWith(".") || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            name is "." or ".." || name.Any(char.IsControl)) return false;
        string stem = name.Split('.')[0].ToUpperInvariant();
        return stem is not ("CON" or "PRN" or "AUX" or "NUL" or "CONIN$" or "CONOUT$") &&
            !(stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")) &&
              "123456789¹²³".Contains(stem[3]));
    }

    private static bool ValidKeys(IEnumerable<string>? keys) =>
        keys == null || keys.Take(MaxKeys + 1).Count() <= MaxKeys &&
        keys.All(k => !string.IsNullOrWhiteSpace(k) && k.Length <= 256 && !k.Any(char.IsControl));

    internal static bool Valid(FullProfileData? data) =>
        data != null && data.Tweaks != null && ValidKeys(data.Tweaks.Keys) && ValidKeys(data.PinnedTweakKeys) &&
        (data.ThemeName == null || data.ThemeName.Length <= 128) &&
        (data.Language == null || new[] { "el", "en", "de", "fr", "es", "it", "ru", "zh", "ja", "pt", "ko", "tr", "ar", "hi" }.Contains(data.Language)) &&
        (data.SidebarPosition == null || data.SidebarPosition is "Left" or "Right") &&
        (data.MenuMode == null || data.MenuMode is "Classic" or "Sidebar" or "HorizontalModern");

    private static void RejectDuplicateKeys(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object) return;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var member in element.EnumerateObject())
        {
            if (!seen.Add(member.Name)) throw new InvalidDataException("Duplicate profile property.");
            RejectDuplicateKeys(member.Value);
        }
    }

    internal static FullProfileData Parse(byte[] bytes)
    {
        if (bytes.Length > MaxBytes) throw new InvalidDataException("Profile exceeds size limit.");
        // Accept UTF-8 BOM from older Windows editors.
        ReadOnlyMemory<byte> payload = bytes;
        if (bytes.Length >= 3 && bytes[0] == 0xef && bytes[1] == 0xbb && bytes[2] == 0xbf)
            payload = payload[3..];
        using var doc = JsonDocument.Parse(payload, new JsonDocumentOptions { MaxDepth = 8 });
        if (doc.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Invalid profile object.");
        RejectDuplicateKeys(doc.RootElement);
        FullProfileData? profile = doc.RootElement.TryGetProperty("Tweaks", out _)
            ? doc.RootElement.Deserialize<FullProfileData>()
            : new FullProfileData { Tweaks = doc.RootElement.Deserialize<Dictionary<string, bool>>()! };
        if (!Valid(profile)) throw new InvalidDataException("Invalid profile settings.");
        return profile!;
    }

    internal static FullProfileData Read(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > MaxBytes) throw new InvalidDataException("Profile exceeds size limit.");
        using var reader = new BinaryReader(stream);
        return Parse(reader.ReadBytes(MaxBytes + 1));
    }

    // Before creating a named-profile file, verify all existing ancestors.
    // These are defensive pathname checks, not a handle-level race guarantee.
    internal static void ValidateNamedPath(string path)
    {
        for (var dir = new DirectoryInfo(Path.GetDirectoryName(path)!); dir != null; dir = dir.Parent)
        {
            try
            {
                _ = File.GetAttributes(dir.FullName);
                if (!Directory.Exists(dir.FullName) || !MaintenancePaths.IsOrdinaryLocalPath(dir.FullName))
                    throw new IOException("Unsafe profile directory.");
                break;
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
        try
        {
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.Directory) != 0 || !MaintenancePaths.IsOrdinaryLocalPath(path))
                throw new IOException("Unsafe profile file.");
        }
        catch (FileNotFoundException) { }
        catch (DirectoryNotFoundException) { }
    }
}
