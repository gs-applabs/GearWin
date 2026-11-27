using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text.Json;
using Microsoft.Win32;

namespace OptimizerWpf.Services
{
    // A durable, allowlisted snapshot is written before the first mutation.
    // Restore only values still owned by the profile; never reset unrelated user changes.
    public static class PowerModeService
    {
        private static readonly object Gate = new();
        private static readonly Guid Balanced = new("381b4222-f694-41f0-9685-ff5bb260df2e");
        private static readonly Guid High = new("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");

        private sealed record Setting(bool Machine, string Path, string Name, int Applied);
        private sealed record Previous(bool Exists, int Value);
        private sealed record Snapshot(string Mode, Guid OriginalPlan, Previous[] Values);

        // Security policy, desktop appearance, menu delays, HAGS and capture settings
        // are independent user choices, not prerequisites for a performance profile.
        private static readonly Setting[] Office = Array.Empty<Setting>();
        private static readonly Setting[] Gaming =
        {
            new(false, @"Software\Microsoft\GameBar", "AllowAutoGameMode", 1),
            new(false, @"Software\Microsoft\GameBar", "AutoGameModeEnabled", 1),
        };

        [DllImport("powrprof.dll")] private static extern uint PowerGetActiveScheme(IntPtr root, out IntPtr scheme);
        [DllImport("powrprof.dll")] private static extern uint PowerSetActiveScheme(IntPtr root, ref Guid scheme);
        [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);

        private static Guid GetPlan()
        {
            if (PowerGetActiveScheme(IntPtr.Zero, out var ptr) != 0 || ptr == IntPtr.Zero)
                throw new IOException("Cannot read the active power plan.");
            try { return Marshal.PtrToStructure<Guid>(ptr); }
            finally { LocalFree(ptr); }
        }

        private static void SetPlan(Guid plan)
        {
            if (GetPlan() == plan) return;
            if (PowerSetActiveScheme(IntPtr.Zero, ref plan) != 0 || GetPlan() != plan)
                throw new IOException("Cannot apply the requested power plan.");
        }

        public static bool SetBalancedPlan() { lock (Gate) { try { SetPlan(Balanced); return true; } catch { return false; } } }
        public static bool SetHighPerformancePlan() { lock (Gate) { try { SetPlan(High); return true; } catch { return false; } } }

        private static string SnapshotPath()
        {
            var root = SecureStorage.RootDir;
            if (Directory.Exists(root) && (File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Unsafe profile storage.");
            SecureStorage.EnsureRoot();
            if (!SecureStorage.IsDirectoryAdminOnlyWritable(root))
                throw new IOException("Profile storage is not protected.");
            using var identity = WindowsIdentity.GetCurrent();
            var sid = identity.User?.Value ?? throw new IOException("Cannot identify the Windows user.");
            var file = Path.Combine(root, "PowerProfile-" + sid + ".json");
            if (File.Exists(file) && (File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Unsafe profile snapshot.");
            return file;
        }

        private static Setting[] Settings(string mode) => mode switch
        {
            "Office" => Office,
            "Gaming" => Gaming,
            _ => throw new InvalidDataException("Unknown profile.")
        };

        private static Guid Target(string mode) => mode == "Office" ? Balanced : High;
        private static RegistryKey Hive(Setting s) => s.Machine ? Registry.LocalMachine : Registry.CurrentUser;

        private static Previous Read(Setting setting)
        {
            using var key = Hive(setting).OpenSubKey(setting.Path);
            var value = key?.GetValue(setting.Name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            if (value == null) return new(false, 0);
            if (value is not int number || key!.GetValueKind(setting.Name) != RegistryValueKind.DWord)
                throw new IOException("Unexpected existing registry value type.");
            return new(true, number);
        }

        private static void Write(Setting setting, Previous value)
        {
            using var key = value.Exists ? Hive(setting).CreateSubKey(setting.Path) : Hive(setting).OpenSubKey(setting.Path, writable: true);
            if (value.Exists)
            {
                if (key == null) throw new IOException("Cannot open profile registry key.");
                key.SetValue(setting.Name, value.Value, RegistryValueKind.DWord);
            }
            else key?.DeleteValue(setting.Name, false);
            if (Read(setting) != value) throw new IOException("Profile registry change could not be verified.");
        }

        internal static bool StillOwned(bool exists, int value, int applied) => exists && value == applied;

        private static Snapshot? Load(string path)
        {
            if (!File.Exists(path)) return null;
            var state = JsonSerializer.Deserialize<Snapshot>(File.ReadAllText(path)) ?? throw new InvalidDataException("Invalid profile snapshot.");
            if (state.OriginalPlan == Guid.Empty || state.Values == null || state.Values.Length != Settings(state.Mode).Length)
                throw new InvalidDataException("Invalid profile snapshot.");
            return state;
        }

        private static void Persist(string path, Snapshot state)
        {
            var tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    JsonSerializer.Serialize(stream, state);
                    stream.Flush(flushToDisk: true);
                }
                File.Move(tmp, path, overwrite: false);
            }
            finally { if (File.Exists(tmp)) File.Delete(tmp); }
        }

        private static void Restore(string path, Snapshot state)
        {
            var settings = Settings(state.Mode);
            // Do not overwrite any value changed since activation by another tool or the user.
            for (var i = 0; i < settings.Length; i++)
            {
                var current = Read(settings[i]);
                if (StillOwned(current.Exists, current.Value, settings[i].Applied) && current != state.Values[i])
                    Write(settings[i], state.Values[i]);
            }
            if (GetPlan() == Target(state.Mode)) SetPlan(state.OriginalPlan);
            File.Delete(path);
        }

        private static void Enable(string mode)
        {
            lock (Gate)
            {
                var path = SnapshotPath();
                var existing = Load(path);
                if (existing != null)
                {
                    if (existing.Mode == mode) return;
                    Restore(path, existing);
                }
                var settings = Settings(mode);
                var state = new Snapshot(mode, GetPlan(), settings.Select(Read).ToArray());
                Persist(path, state);
                try
                {
                    SetPlan(Target(mode));
                    for (var i = 0; i < settings.Length; i++)
                        if (state.Values[i] != new Previous(true, settings[i].Applied))
                            Write(settings[i], new Previous(true, settings[i].Applied));
                }
                catch
                {
                    Restore(path, state); // if rollback fails, the durable snapshot stays available for retry
                    throw;
                }
            }
        }

        private static void Disable(string mode)
        {
            lock (Gate)
            {
                var path = SnapshotPath();
                var state = Load(path);
                // Old releases have no trustworthy snapshot: never guess original values.
                if (state?.Mode == mode) Restore(path, state);
            }
        }

        public static void SetOfficeMode() => Enable("Office");
        public static void SetGamingMode() => Enable("Gaming");
        // Preserve the old call signature. Never terminate Explorer.
        public static void DisableOfficeMode(bool restartExplorer = true) => Disable("Office");
        public static void DisableGamingMode(bool restartExplorer = true) => Disable("Gaming");
    }
}
