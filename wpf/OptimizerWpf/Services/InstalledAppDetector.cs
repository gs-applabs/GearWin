using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace OptimizerWpf.Services
{
    public sealed record UninstallEntry(string DisplayName, string? DisplayVersion, string? InstallLocation);

    // Ό,τι χρειάζεται ο ανιχνευτής από το σύστημα - υλοποιείται από το SystemInstallProbe (πραγματικό) και από ψεύτικο στα tests.
    public interface IInstallProbe
    {
        string Expand(string path);
        bool FileExists(string path);
        string? FileVersion(string path);
        IEnumerable<UninstallEntry> UninstallEntries();
    }

    public enum SuiteInstallState { NotInstalled, Installed, UpdateAvailable, ThisApp }

    public sealed record InstalledInfo(bool Installed, ReleaseVersion? Version, string? ExePath);

    // Ανίχνευση ΜΟΝΟ-ΑΝΑΓΝΩΣΗΣ (κλειδιά Uninstall του μητρώου + γνωστές διαδρομές) - δεν γράφει τίποτα, δεν εκτελεί τίποτα.
    public static class InstalledAppDetector
    {
        public static InstalledInfo Detect(SuiteApp app, IInstallProbe probe)
        {
            // 1) γνωστές διαδρομές (δίνουν και διαδρομή για το κουμπί "Άνοιγμα")
            foreach (var raw in app.Paths)
            {
                string path;
                try { path = probe.Expand(raw); } catch { continue; }
                if (string.IsNullOrWhiteSpace(path) || path.Contains('%')) continue;
                if (!probe.FileExists(path)) continue;
                var v = ReleaseVersion.ParseOrNull(probe.FileVersion(path));
                return new InstalledInfo(true, v ?? UninstallVersion(app, probe), path);
            }
            // 2) μητρώο Uninstall (ακριβές όνομα· ΟΧΙ προθεματικό ταίριασμα - το "Opsis" δεν πρέπει να ταιριάζει με το "Opsis News Crawler")
            var entry = FindEntry(app, probe);
            if (entry != null)
            {
                string? exe = null;
                if (!string.IsNullOrWhiteSpace(entry.InstallLocation))
                {
                    foreach (var p in app.Paths)
                    {
                        var file = Path.GetFileName(p);
                        var candidate = Path.Combine(entry.InstallLocation.Trim('"'), file);
                        if (probe.FileExists(candidate)) { exe = candidate; break; }
                    }
                }
                return new InstalledInfo(true, ReleaseVersion.ParseOrNull(entry.DisplayVersion), exe);
            }
            return new InstalledInfo(false, null, null);
        }

        private static UninstallEntry? FindEntry(SuiteApp app, IInstallProbe probe) =>
            probe.UninstallEntries().FirstOrDefault(e => string.Equals(e.DisplayName?.Trim(), app.UninstallDisplayName, StringComparison.OrdinalIgnoreCase));

        private static ReleaseVersion? UninstallVersion(SuiteApp app, IInstallProbe probe) => ReleaseVersion.ParseOrNull(FindEntry(app, probe)?.DisplayVersion);

        // Κατάσταση για την κάρτα: η ίδια η εφαρμογή (isSelf) δεν ανιχνεύεται - είναι ΠΑΝΤΑ "εδώ είστε".
        public static SuiteInstallState StateFor(bool isSelf, InstalledInfo info, ReleaseVersion? latest)
        {
            if (isSelf) return SuiteInstallState.ThisApp;
            if (!info.Installed) return SuiteInstallState.NotInstalled;
            return latest != null && info.Version != null && latest > info.Version ? SuiteInstallState.UpdateAvailable : SuiteInstallState.Installed;
        }
    }

    public sealed class SystemInstallProbe : IInstallProbe
    {
        public string Expand(string path) => Environment.ExpandEnvironmentVariables(path);
        public bool FileExists(string path) { try { return File.Exists(path); } catch { return false; } }

        public string? FileVersion(string path)
        {
            try
            {
                var fvi = FileVersionInfo.GetVersionInfo(path);
                var v = fvi.ProductVersion ?? fvi.FileVersion;
                return string.IsNullOrWhiteSpace(v) ? null : v.Split(' ')[0];
            }
            catch { return null; }
        }

        public IEnumerable<UninstallEntry> UninstallEntries() =>
            ReadRegistrations((hive, name, key) =>
                key.GetValue("DisplayName") is string dn && dn.Length > 0
                    ? new UninstallEntry(dn, key.GetValue("DisplayVersion") as string, key.GetValue("InstallLocation") as string)
                    : null, userFirst: true, strict: false);

        // Only registry traversal is shared. Each consumer chooses its own
        // evidence and trust policy. No registry handles escape the projection.
        internal static (RegistryHive Hive, RegistryView View)[] RegistryLocations(bool is64Bit, bool userFirst)
        {
            var machine = is64Bit
                ? new[] { (RegistryHive.LocalMachine, RegistryView.Registry64), (RegistryHive.LocalMachine, RegistryView.Registry32) }
                : new[] { (RegistryHive.LocalMachine, RegistryView.Default) };
            var user = (RegistryHive.CurrentUser, RegistryView.Default);
            return userFirst ? machine.Prepend(user).ToArray() : machine.Append(user).ToArray();
        }

        internal static IEnumerable<T> ReadRegistrations<T>(Func<RegistryHive, string, RegistryKey, T?> project,
            bool userFirst = false, bool strict = false) where T : class
        {
            const string sub = @"Software\Microsoft\Windows\CurrentVersion\Uninstall";
            foreach (var (hive, view) in RegistryLocations(Environment.Is64BitOperatingSystem, userFirst))
            {
                RegistryKey? baseKey = null, root = null;
                try
                {
                    string[] names;
                    try
                    {
                        baseKey = RegistryKey.OpenBaseKey(hive, view);
                        root = baseKey.OpenSubKey(sub, writable: false);
                        if (root == null) continue;
                        names = root.GetSubKeyNames();
                    }
                    catch (Exception ex) when (!strict && IsAccessFailure(ex)) { continue; }
                    foreach (var name in names)
                    {
                        T? entry = null;
                        try
                        {
                            using var key = root.OpenSubKey(name, writable: false);
                            if (key != null) entry = project(hive, name, key);
                        }
                        catch (Exception ex) when (!strict && IsAccessFailure(ex)) { }
                        if (entry != null) yield return entry;
                    }
                }
                finally { root?.Dispose(); baseKey?.Dispose(); }
            }
        }

        private static bool IsAccessFailure(Exception ex) =>
            ex is IOException or UnauthorizedAccessException or System.Security.SecurityException;
    }
}
