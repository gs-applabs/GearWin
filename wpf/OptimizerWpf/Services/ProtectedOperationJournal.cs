using System;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OptimizerWpf.Services;

// Callers serialize mutations with a resource-specific cross-process lease.
internal static class ProtectedOperationJournal
{
    private static readonly JsonSerializerOptions Options = new()
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    internal static string PathFor(string name)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(name, @"\A[A-Za-z0-9_-]{1,100}\z"))
            throw new ArgumentException("Invalid journal name.");
        return Path.Combine(SecureStorage.RootDir, "operation-journals-v1", name + ".json");
    }

    internal static T? Read<T>(string path, Func<T, bool> validate) where T : class
    {
        try { _ = File.GetAttributes(path); }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
        if (!SecureStorage.IsProtectedFile(path)) throw new IOException("Untrusted operation journal.");
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > 65536) throw new InvalidDataException("Oversized operation journal.");
        using var doc = JsonDocument.Parse(stream);
        if (doc.RootElement.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Expected operation journal object.");
        var keys = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
        foreach (var prop in doc.RootElement.EnumerateObject())
            if (!keys.Add(prop.Name)) throw new InvalidDataException("Duplicate journal field.");
        var value = doc.RootElement.Deserialize<T>(Options);
        if (value == null || !validate(value)) throw new InvalidDataException("Invalid operation journal.");
        return value;
    }

    internal static void Save<T>(string path, T value, Func<T, bool> validate, bool replace = false) where T : class
    {
        if (!validate(value)) throw new InvalidDataException("Invalid operation journal.");
        SecureStorage.SecureDirectory(Path.GetDirectoryName(path)!);
        if (replace && Read<T>(path, validate) == null)
            throw new IOException("Cannot replace a missing operation journal.");
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, Options);
        if (bytes.Length > 65536) throw new InvalidDataException("Oversized operation journal.");
        var tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        bool owned = false;
        try
        {
            using (var stream = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                owned = true;
                stream.Write(bytes);
                stream.Flush(true);
            }
            SetTrustedOwner(tmp);
            if (!SecureStorage.IsProtectedFile(tmp)) throw new IOException("Untrusted journal staging file.");
            File.Move(tmp, path, replace);
            owned = false;
            if (!Equals(value, Read<T>(path, validate))) throw new IOException("Journal readback failed.");
        }
        finally { if (owned) File.Delete(tmp); }
    }

    internal static void SetTrustedOwner(string path)
    {
        var info = new FileInfo(path);
        var security = info.GetAccessControl(AccessControlSections.Owner);
        security.SetOwner(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null));
        info.SetAccessControl(security);
    }
}
