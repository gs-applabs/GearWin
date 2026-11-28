using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

namespace OptimizerWpf.Services
{
    public class GameProfileData
    {
        public GpuPreference? GpuPreference { get; set; }
        public bool? DisableFullscreenOptimizations { get; set; }
        public Dictionary<string, bool> GlobalTweaks { get; set; } = new();
    }

    internal sealed record GameProfileEnvelope(string Executable, string Json);

    public static class GameProfileService
    {
        private static readonly string ProfilesDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OptimizerWpf", "GameProfiles");
        private static readonly HashSet<string> Allowed = new(StringComparer.Ordinal)
            { "GameMode", "Hags", "WindowedOpt", "VrrOptimize" };

        internal static string CanonicalExe(string input)
        {
            if (string.IsNullOrWhiteSpace(input) || input.Length > 1024 || input.Any(char.IsControl) ||
                input.Length < 4 || !char.IsAsciiLetter(input[0]) || input[1] != ':' || input[2] != '\\' ||
                input[2..].Contains(':') || !Path.IsPathFullyQualified(input) ||
                !string.Equals(Path.GetExtension(input), ".exe", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(Path.GetFullPath(input), input, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Choose a canonical local executable path.");
            return input.ToUpperInvariant();
        }

        internal static string Identity(string exePath) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(CanonicalExe(exePath))));

        private static string UserId()
        {
            using var user = WindowsIdentity.GetCurrent();
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(user.User?.Value ??
                throw new IOException("Unknown Windows user."))));
        }

        private static string PathFor(string exePath) =>
            Path.Combine(SecureStorage.RootDir, "game-profiles-v2", UserId(), Identity(exePath) + ".json");

        internal static bool Valid(GameProfileData? data) =>
            data != null && (data.GpuPreference == null || Enum.IsDefined(data.GpuPreference.Value)) &&
            data.GlobalTweaks != null && data.GlobalTweaks.Count <= Allowed.Count &&
            data.GlobalTweaks.Keys.All(Allowed.Contains);

        internal static GameProfileData Parse(string json)
        {
            if (json.Length > 8192) throw new InvalidDataException("Game profile is too large.");
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 5 });
            void Check(JsonElement e, bool root)
            {
                if (e.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Invalid game profile.");
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var p in e.EnumerateObject())
                {
                    if (!names.Add(p.Name) || (root && p.Name is not ("GpuPreference" or "DisableFullscreenOptimizations" or "GlobalTweaks")))
                        throw new InvalidDataException("Unknown or duplicate game profile field.");
                    if (root && p.Name == "GlobalTweaks") Check(p.Value, false);
                }
            }
            Check(doc.RootElement, true);
            var data = doc.RootElement.Deserialize<GameProfileData>();
            return Valid(data) ? data! : throw new InvalidDataException("Invalid game profile settings.");
        }

        private static bool ValidEnvelope(GameProfileEnvelope e, string canonical)
        {
            try { return e.Executable == canonical && Valid(Parse(e.Json)); }
            catch { return false; }
        }

        public static bool HasProfile(string exePath) => Load(exePath) != null;

        public static bool HasLegacyProfile(string exePath)
        {
            try { _ = CanonicalExe(exePath); return File.Exists(Path.Combine(ProfilesDir, SafeFileName(exePath) + ".json")); }
            catch { return false; }
        }

        public static bool Save(string exePath, GameProfileData data)
        {
            try
            {
                string canonical = CanonicalExe(exePath);
                if (!Valid(data) || !File.Exists(exePath) || !MaintenancePaths.IsOrdinaryLocalPath(exePath)) return false;
                string path = PathFor(exePath);
                using var lease = CoreProcessLease.TryAcquire(@"Global\GearWin.GameProfile.v2." + UserId() + "." + Identity(exePath));
                if (lease == null) return false;
                var old = ProtectedOperationJournal.Read<GameProfileEnvelope>(path, e => ValidEnvelope(e, canonical));
                var next = new GameProfileEnvelope(canonical, JsonSerializer.Serialize(data));
                if (old == next) return true;
                ProtectedOperationJournal.Save(path, next, e => ValidEnvelope(e, canonical), replace: old != null);
                return true;
            }
            catch (Exception ex) { System.Diagnostics.Trace.TraceWarning("Game profile save: " + ex.Message); return false; }
        }

        public static GameProfileData? Load(string exePath)
        {
            try
            {
                string canonical = CanonicalExe(exePath);
                var envelope = ProtectedOperationJournal.Read<GameProfileEnvelope>(PathFor(exePath), e => ValidEnvelope(e, canonical));
                return envelope == null ? null : Parse(envelope.Json);
            }
            catch (Exception ex) { System.Diagnostics.Trace.TraceWarning("Game profile load: " + ex.Message); return null; }
        }

        // Legacy lossy names are not evidence of executable identity. Preserve
        // their files, but never use them for automatic elevated actions.
        public static string SafeFileName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            return new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        }
    }
}
