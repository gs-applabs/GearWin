using System.Runtime.Versioning;
using Microsoft.Win32;

namespace GearWin.Migration;

public enum InventorySource { LinuxDesktop, WindowsRegistry, WindowsStorePackage, WindowsSelectedExecutable, WindowsPackagedApp }

public sealed record InstalledApplication(InventorySource Kind, string Id, string Name,
    string? Version = null, string? Publisher = null);

public sealed record ApplicationInventory(int SchemaVersion, string OperatingSystem,
    IReadOnlyList<InstalledApplication> Applications, IReadOnlyList<string> Warnings);

public static class ApplicationDiscovery
{
    public static ApplicationInventory InspectHost()
    {
        if (OperatingSystem.IsWindows())
            return InspectWindows();
        if (OperatingSystem.IsLinux())
            return InspectLinux(LinuxDataDirectories());
        return new(PackagePlanner.SchemaVersion, "other", [], ["No application inventory reader for this OS."]);
    }

    public static ApplicationInventory InspectLinux(IEnumerable<string> dataDirectories)
    {
        var applications = new List<InstalledApplication>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var warnings = new List<string>();
        bool incomplete = false;

        foreach (string dataDirectory in dataDirectories.Distinct(StringComparer.Ordinal))
        {
            string root = Path.Combine(dataDirectory, "applications");
            if (!Directory.Exists(root))
                continue;

            var pending = new Stack<DirectoryInfo>();
            pending.Push(new DirectoryInfo(root));
            while (pending.TryPop(out DirectoryInfo? directory))
            {
                try
                {
                    foreach (var file in directory.GetFiles("*.desktop").OrderBy(file => file.Name, StringComparer.Ordinal))
                    {
                        string id = Path.GetRelativePath(root, file.FullName).Replace(Path.DirectorySeparatorChar, '-');
                        if (!seen.Add(id))
                            continue;
                        try
                        {
                            if (ReadDesktopEntry(BoundedText.ReadFile(file.FullName, 64 * 1024).Split('\n'), id) is { } application)
                                applications.Add(application);
                        }
                        catch (IOException) { incomplete = true; }
                        catch (UnauthorizedAccessException) { incomplete = true; }
                    }

                    foreach (var child in directory.GetDirectories())
                    {
                        // Flatpak exports can be file symlinks; skip only linked directories to avoid loops.
                        if (!child.Attributes.HasFlag(FileAttributes.ReparsePoint))
                            pending.Push(child);
                    }
                }
                catch (IOException) { incomplete = true; }
                catch (UnauthorizedAccessException) { incomplete = true; }
            }
        }

        if (incomplete)
            warnings.Add("Some desktop entries or directories could not be read.");
        return new(PackagePlanner.SchemaVersion, "linux", Sort(applications), warnings);
    }

    private static IEnumerable<string> LinuxDataDirectories()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string? dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        string? flatpakUser = Environment.GetEnvironmentVariable("FLATPAK_USER_DIR");
        string? flatpakSystem = Environment.GetEnvironmentVariable("FLATPAK_SYSTEM_DIR");
        string? userData = IsAbsolute(dataHome) ? dataHome :
            IsAbsolute(home) ? Path.Combine(home, ".local", "share") : null;
        if (userData is not null)
            yield return userData;

        string? dataDirs = Environment.GetEnvironmentVariable("XDG_DATA_DIRS");
        var systemDirs = string.IsNullOrEmpty(dataDirs) ? "/usr/local/share:/usr/share" : dataDirs;
        foreach (string directory in systemDirs.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            if (IsAbsolute(directory))
                yield return directory;
        }

        if (IsAbsolute(flatpakUser))
            yield return Path.Combine(flatpakUser!, "exports", "share");
        else if (userData is not null)
            yield return Path.Combine(userData, "flatpak", "exports", "share");
        yield return Path.Combine(IsAbsolute(flatpakSystem) ? flatpakSystem! : "/var/lib/flatpak", "exports", "share");
        yield return "/var/lib/snapd/desktop";
        if (IsAbsolute(home))
        {
            yield return Path.Combine(home, ".nix-profile", "share");
            yield return Path.Combine(home, ".guix-profile", "share");
        }
        yield return "/run/current-system/sw/share";
        yield return "/run/current-system/profile/share";
    }

    private static bool IsAbsolute(string? path) => !string.IsNullOrEmpty(path) && Path.IsPathFullyQualified(path);

    private static InstalledApplication? ReadDesktopEntry(IEnumerable<string> lines, string id)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        bool inEntry = false;
        foreach (string rawLine in lines)
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;
            if (line.StartsWith('['))
            {
                if (inEntry)
                    break;
                inEntry = line == "[Desktop Entry]";
                continue;
            }
            int equals = line.IndexOf('=');
            if (inEntry && equals > 0)
                fields[line[..equals]] = line[(equals + 1)..];
        }

        if (fields.GetValueOrDefault("Type") != "Application" || fields.GetValueOrDefault("Hidden") == "true" ||
            fields.GetValueOrDefault("NoDisplay") == "true" ||
            !fields.TryGetValue("Name", out string? name) || string.IsNullOrWhiteSpace(name))
            return null;

        return new(InventorySource.LinuxDesktop, id, name, fields.GetValueOrDefault("X-AppVersion"));
    }

    [SupportedOSPlatform("windows")]
    private static ApplicationInventory InspectWindows()
    {
        const string path = @"Software\Microsoft\Windows\CurrentVersion\Uninstall";
        RegistryView[] views = Environment.Is64BitOperatingSystem
            ? [RegistryView.Registry64, RegistryView.Registry32] : [RegistryView.Default];
        var applications = new List<InstalledApplication>();
        var seen = new HashSet<(string Id, string Name, string Publisher, string Version)>();
        bool incomplete = false;

        foreach (RegistryHive hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        foreach (RegistryView view in views)
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                using var root = baseKey.OpenSubKey(path);
                if (root is null)
                    continue;
                foreach (string id in root.GetSubKeyNames())
                {
                    try
                    {
                        using var app = root.OpenSubKey(id);
                        if (app?.GetValue("DisplayName") is not string name || string.IsNullOrWhiteSpace(name) ||
                            app.GetValue("SystemComponent") is 1 || app.GetValue("ParentKeyName") is string)
                            continue;

                        var version = app.GetValue("DisplayVersion") as string;
                        var publisher = app.GetValue("Publisher") as string;
                        if (!seen.Add(RegistrationKey(id, name, publisher, version))) continue;
                        applications.Add(new(InventorySource.WindowsRegistry, id, name.Trim(), version, publisher));
                    }
                    catch (IOException) { incomplete = true; }
                    catch (UnauthorizedAccessException) { incomplete = true; }
                    catch (System.Security.SecurityException) { incomplete = true; }
                }
            }
            catch (IOException) { incomplete = true; }
            catch (UnauthorizedAccessException) { incomplete = true; }
            catch (System.Security.SecurityException) { incomplete = true; }
        }

        return new(PackagePlanner.SchemaVersion, "windows", Sort(applications),
            incomplete
                ? ["Some Windows application registrations could not be read.", WindowsRegistryScope]
                : [WindowsRegistryScope]);
    }

    private const string WindowsRegistryScope = "Registry inventory does not include all Store/MSIX or standalone portable applications.";

    private static (string Id, string Name, string Publisher, string Version) RegistrationKey(
        string id, string name, string? publisher, string? version) =>
        (id.ToUpperInvariant(), name.Trim().ToUpperInvariant(),
            publisher?.Trim().ToUpperInvariant() ?? "", version?.Trim().ToUpperInvariant() ?? "");

    // The Windows UI supplies already validated current-user Store package records
    // from its existing UWP scanner. Never infer a WinGet ID from a package family.
    public static ApplicationInventory AddStorePackages(ApplicationInventory inventory,
        IEnumerable<(string Name, string FullName)> packages) =>
        AddPackagedAppsCore(inventory, packages.Select(p => (p.Name, p.FullName, true)), false, true);

    public static ApplicationInventory AddPackagedApps(ApplicationInventory inventory,
        IEnumerable<(string Name, string FullName, bool IsStore)> packages, bool incomplete = false) =>
        AddPackagedAppsCore(inventory, packages, incomplete, false);

    private static ApplicationInventory AddPackagedAppsCore(ApplicationInventory inventory,
        IEnumerable<(string Name, string FullName, bool IsStore)> packages, bool incomplete, bool removableStoreOnly)
    {
        if (inventory.OperatingSystem != "windows" || inventory.SchemaVersion != PackagePlanner.SchemaVersion)
            throw new ArgumentException("Expected a Windows inventory.");
        var apps = inventory.Applications.ToList();
        var seen = new HashSet<string>(apps.Where(a => a.Kind is InventorySource.WindowsStorePackage or InventorySource.WindowsPackagedApp)
            .Select(a => a.Kind + "|" + a.Id + "|" + a.Version), StringComparer.OrdinalIgnoreCase);
        var warnings = inventory.Warnings.ToList();
        bool invalid = incomplete;
        foreach (var package in packages)
        {
            var parts = package.FullName.Split('_');
            if (parts.Length != 5 || string.IsNullOrWhiteSpace(package.Name) ||
                string.IsNullOrWhiteSpace(parts[0]) || !Version.TryParse(parts[1], out _) ||
                parts[4].Length != 13 || !parts[4].All(char.IsAsciiLetterOrDigit) ||
                !string.Equals(parts[0], package.Name, StringComparison.OrdinalIgnoreCase))
            { invalid = true; continue; }

            string family = parts[0] + "_" + parts[4];
            var kind = package.IsStore ? InventorySource.WindowsStorePackage : InventorySource.WindowsPackagedApp;
            if (seen.Add(kind + "|" + family + "|" + parts[1]))
                apps.Add(new(kind, family, package.Name, parts[1]));
        }
        if (invalid) warnings.Add("Some packaged application identities could not be interpreted.");
        warnings.Add(removableStoreOnly
            ? "Store supplement covers current-user removable Store apps only; other MSIX, system apps and portable apps are not fully inventoried."
            : "Packaged app supplement covers current-user non-framework, non-resource package registrations; other users, provisioning, optional package types and portable applications are not fully inventoried.");
        return inventory with { Applications = Sort(apps), Warnings = warnings.ToArray() };
    }

    // Explicit user selections, not a discovery scan or an authenticity claim.
    public static ApplicationInventory AddSelectedExecutables(ApplicationInventory inventory,
        IEnumerable<string> selectedNames)
    {
        if (inventory.OperatingSystem != "windows" || inventory.SchemaVersion != PackagePlanner.SchemaVersion)
            throw new ArgumentException("Expected a Windows inventory.");
        var apps = inventory.Applications.ToList();
        var seen = new HashSet<string>(apps.Where(a => a.Kind == InventorySource.WindowsSelectedExecutable)
            .Select(a => a.Id), StringComparer.OrdinalIgnoreCase);
        var warnings = inventory.Warnings.ToList();
        bool invalid = false;
        int count = 0;
        foreach (var name in selectedNames)
        {
            if (++count > 100) { invalid = true; break; }
            if (string.IsNullOrWhiteSpace(name) || name.Length > 255 ||
                name.IndexOfAny(['/', '\\', ':']) >= 0 || name.Any(char.IsControl) ||
                !name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || name.Length <= 4)
            { invalid = true; continue; }
            if (seen.Add(name))
                apps.Add(new(InventorySource.WindowsSelectedExecutable, name, name));
        }
        if (invalid) warnings.Add("Some selected executable names were invalid or exceeded the selection limit.");
        warnings.Add("User-selected executable names are advisory only. Paths and file contents were not exported; portable status, authenticity and compatibility are not verified.");
        return inventory with { Applications = Sort(apps), Warnings = warnings.ToArray() };
    }

    private static InstalledApplication[] Sort(IEnumerable<InstalledApplication> applications) => applications
        .OrderBy(application => application.Name, StringComparer.OrdinalIgnoreCase)
        .ThenBy(application => application.Id, StringComparer.Ordinal).ToArray();
}
