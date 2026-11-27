using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;

namespace OptimizerWpf.Services;

// Only supported scalar kinds are accepted. Missing is an explicit state, never
// inferred from a missing/corrupt journal. No registry mutation in the constructor.
internal sealed record TweakValue(string Kind, string? Data)
{
    internal static TweakValue Missing { get; } = new("missing", null);

    internal static TweakValue Capture(object? value, RegistryValueKind kind)
        => value switch
        {
            int n when kind == RegistryValueKind.DWord => new("dword", n.ToString(CultureInfo.InvariantCulture)),
            string s when kind == RegistryValueKind.String => new("string", s),
            _ => throw new InvalidDataException("Unsupported registry value kind; no tweak was applied.")
        };

    internal bool IsValid => Kind switch
    {
        "missing" => Data == null,
        "dword" => int.TryParse(Data, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var n)
            && Data == n.ToString(CultureInfo.InvariantCulture),
        "string" => Data != null && Data.Length <= 16384 && !Data.Contains('\0'),
        _ => false
    };

    internal object ToRegistryValue() => Kind switch
    {
        "dword" when IsValid => int.Parse(Data!, CultureInfo.InvariantCulture),
        "string" when IsValid => Data!,
        _ => throw new InvalidDataException("Invalid registry backup value.")
    };

    internal RegistryValueKind RegistryKind => Kind switch
    {
        "dword" => RegistryValueKind.DWord,
        "string" => RegistryValueKind.String,
        _ => throw new InvalidDataException("Missing values cannot be written.")
    };
}

internal sealed record TweakRegistrySnapshot(TweakValue Original, TweakValue Applied)
{
    internal bool IsValid => Original is { IsValid: true } && Applied is { IsValid: true }
        && Applied != TweakValue.Missing && Original != Applied;

    internal bool CanRestore(TweakValue current) => IsValid && (current == Applied || current == Original);

    internal static TweakRegistrySnapshot Parse(Stream stream)
    {
        using var doc = JsonDocument.Parse(stream);
        static void Exact(JsonElement element, params string[] names)
        {
            if (element.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Invalid tweak journal object.");
            var remaining = new System.Collections.Generic.HashSet<string>(names, StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
                if (!remaining.Remove(property.Name)) throw new InvalidDataException("Duplicate or unknown tweak journal property.");
            if (remaining.Count != 0) throw new InvalidDataException("Incomplete tweak journal.");
        }
        Exact(doc.RootElement, "Original", "Applied");
        Exact(doc.RootElement.GetProperty("Original"), "Kind", "Data");
        Exact(doc.RootElement.GetProperty("Applied"), "Kind", "Data");
        var snapshot = doc.RootElement.Deserialize<TweakRegistrySnapshot>();
        if (snapshot is not { IsValid: true }) throw new InvalidDataException("Invalid tweak journal.");
        return snapshot;
    }
}

internal static class TweakRegistryJournal
{
    internal const int MaxBytes = 256 * 1024;
    private static string DirectoryPath => Path.Combine(SecureStorage.RootDir, "registry-tweaks-v1");

    internal static string Identity(RegistryHive hive, string path, string name, string? userSid)
    {
        if (hive != RegistryHive.LocalMachine && hive != RegistryHive.CurrentUser)
            throw new ArgumentException("Unsupported registry hive.");
        string scope = hive == RegistryHive.LocalMachine ? "machine" :
            userSid ?? throw new InvalidOperationException("Unknown registry user.");
        var identity = JsonSerializer.Serialize(new[] { scope, hive.ToString(), RegistryView.Default.ToString(),
            Environment.Is64BitProcess ? "64" : "32", path.ToUpperInvariant(), name.ToUpperInvariant() });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
    }

    private static string Identity(RegTweak tweak)
    {
        using var user = WindowsIdentity.GetCurrent();
        return Identity(tweak.Hive, tweak.Path, tweak.Name, user.User?.Value);
    }

    private static string JournalPath(string identity) => Path.Combine(DirectoryPath, identity + ".json");

    private static bool Exists(string path)
    {
        try { _ = File.GetAttributes(path); return true; }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
    }

    internal static TweakRegistrySnapshot? Read(string path)
    {
        if (!Exists(path)) return null;
        if (!SecureStorage.IsProtectedFile(path)) throw new IOException("Untrusted tweak journal.");
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        if (stream.Length > MaxBytes) throw new InvalidDataException("Oversized tweak journal.");
        return TweakRegistrySnapshot.Parse(stream);
    }

    private static void SaveNew(string path, TweakRegistrySnapshot snapshot)
    {
        if (!snapshot.IsValid) throw new InvalidDataException("Invalid tweak journal.");
        SecureStorage.SecureDirectory(DirectoryPath);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(snapshot);
        if (bytes.Length > MaxBytes) throw new InvalidDataException("Oversized tweak journal.");
        var stage = Path.Combine(DirectoryPath, Guid.NewGuid().ToString("N") + ".tmp");
        bool owned = false;
        try
        {
            using (var stream = new FileStream(stage, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                owned = true;
                stream.Write(bytes);
                stream.Flush(true);
            }
            if (!SecureStorage.IsProtectedFile(stage)) throw new IOException("Untrusted tweak journal staging file.");
            File.Move(stage, path, overwrite: false);
            owned = false;
            if (Read(path) != snapshot) throw new IOException("Tweak journal readback failed.");
        }
        finally { if (owned) File.Delete(stage); }
    }

    private static RegistryKey Hive(RegTweak tweak) => tweak.Hive switch
    {
        RegistryHive.LocalMachine => Registry.LocalMachine,
        RegistryHive.CurrentUser => Registry.CurrentUser,
        _ => throw new ArgumentException("Unsupported registry hive.")
    };

    private static TweakValue Current(RegTweak tweak)
    {
        using var key = Hive(tweak).OpenSubKey(tweak.Path);
        if (key == null) return TweakValue.Missing;
        // A missing value and an unsupported value must not be conflated.
        var value = key.GetValue(tweak.Name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        if (value == null)
        {
            if (Array.Exists(key.GetValueNames(), name => string.Equals(name, tweak.Name, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("Unsupported or unreadable registry value.");
            return TweakValue.Missing;
        }
        var result = TweakValue.Capture(value, key.GetValueKind(tweak.Name));
        if (!result.IsValid) throw new InvalidDataException("Unsupported registry value.");
        return result;
    }

    private static void Write(RegTweak tweak, TweakValue value)
    {
        if (value == TweakValue.Missing)
        {
            using var key = Hive(tweak).OpenSubKey(tweak.Path, writable: true);
            key?.DeleteValue(tweak.Name, throwOnMissingValue: false);
            key?.Flush();
        }
        else
        {
            using var key = Hive(tweak).CreateSubKey(tweak.Path, writable: true)
                ?? throw new IOException("Could not open registry key for write.");
            key.SetValue(tweak.Name, value.ToRegistryValue(), value.RegistryKind);
            key.Flush();
        }
        if (Current(tweak) != value) throw new IOException("Registry readback failed; journal retained.");
    }

    private static IDisposable Lease(string identity)
        => CoreProcessLease.TryAcquire(@"Global\GearWin.RegistryTweak.v1." + identity)
            ?? throw new IOException("Another GearWin process is changing this registry value.");

    internal static void Apply(RegTweak tweak)
    {
        var identity = Identity(tweak);
        using var lease = Lease(identity);
        var path = JournalPath(identity);
        var target = TweakValue.Capture(tweak.OnValue, tweak.Kind);
        var current = Current(tweak);
        var snapshot = Read(path);
        if (snapshot == null)
        {
            if (current == target) return; // already on, not owned by GearWin
            // Old string-only data is not proof of original kind or ownership.
            if (TweakBackupService.GetBackup(tweak.BackupKey) != null)
                throw new InvalidOperationException("Legacy tweak backup requires manual review before changing this setting.");
            snapshot = new(current, target);
            SaveNew(path, snapshot); // durable backup before any registry write
        }
        if (snapshot.Applied != target || !snapshot.CanRestore(current))
            throw new InvalidOperationException("Registry value or tweak definition changed externally; journal retained.");
        if (Current(tweak) != current) throw new IOException("Registry changed during backup.");
        if (current != target) Write(tweak, target);
    }

    internal static void Restore(RegTweak tweak)
    {
        var identity = Identity(tweak);
        using var lease = Lease(identity);
        var path = JournalPath(identity);
        var snapshot = Read(path);
        if (snapshot == null)
            throw new InvalidOperationException("No verified original value; registry left unchanged.");
        var current = Current(tweak);
        if (snapshot.Applied != TweakValue.Capture(tweak.OnValue, tweak.Kind) || !snapshot.CanRestore(current))
            throw new InvalidOperationException("Registry value or tweak definition changed externally; journal retained.");
        if (Current(tweak) != current) throw new IOException("Registry changed during restoration.");
        if (current != snapshot.Original) Write(tweak, snapshot.Original);
        File.Delete(path); // only after verified restoration, including interrupted retries
    }

    internal static bool CanRestore(RegTweak tweak)
    {
        try
        {
            var snapshot = Read(JournalPath(Identity(tweak)));
            return snapshot != null && snapshot.Applied == TweakValue.Capture(tweak.OnValue, tweak.Kind)
                && snapshot.CanRestore(Current(tweak));
        }
        catch { return false; } // no claim of restorable state when read is uncertain
    }
}
