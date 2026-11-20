using System.Runtime.InteropServices;

namespace GearWin.Migration;

public enum PackageManager
{
    Winget, Apt, Dnf, Pacman, Zypper, Apk, Xbps, Emerge, RpmOstree, NixOs, Flatpak
}

public sealed record PackageBackend(PackageManager Manager, string Executable);

public sealed record PackageProfile(
    string OperatingSystem,
    string Architecture,
    string? DistributionId,
    string? DistributionVersion,
    string? VariantId,
    IReadOnlyList<PackageBackend> Backends,
    string? NativeBackendNote);

public static class PackageDiscovery
{
    private static readonly PackageBackend[] LinuxNative =
    [
        new(PackageManager.Apt, "apt-get"),
        new(PackageManager.Dnf, "dnf"),
        new(PackageManager.Pacman, "pacman"),
        new(PackageManager.Zypper, "zypper"),
        new(PackageManager.Apk, "apk"),
        new(PackageManager.Xbps, "xbps-install"),
        new(PackageManager.Emerge, "emerge")
    ];

    public static PackageProfile InspectHost()
    {
        string architecture = RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant();
        if (OperatingSystem.IsWindows())
        {
            bool hasWinget = IsOnPath("winget.exe");
            return new PackageProfile("windows", architecture, null, Environment.OSVersion.Version.ToString(), null,
                hasWinget ? [new(PackageManager.Winget, "winget.exe")] : [],
                hasWinget ? null : "WinGet was not found on PATH.");
        }

        if (!OperatingSystem.IsLinux())
            return new PackageProfile("other", architecture, null, null, null, [], "No package backend for this OS.");

        string osRelease = File.Exists("/etc/os-release") ? "/etc/os-release" : "/usr/lib/os-release";
        return InspectLinux(
            File.Exists(osRelease) ? File.ReadLines(osRelease) : [], architecture, IsOnPath,
            File.Exists("/run/ostree-booted"));
    }

    public static PackageProfile InspectLinux(IEnumerable<string> osRelease, string architecture,
        Func<string, bool> hasCommand, bool isOstree)
    {
        var fields = ParseOsRelease(osRelease);
        string? id = fields.GetValueOrDefault("ID");
        string? variant = fields.GetValueOrDefault("VARIANT_ID");
        var backends = new List<PackageBackend>();
        string? note = null;

        if (id == "nixos")
            backends.Add(new(PackageManager.NixOs, ""));
        else if (isOstree)
        {
            if (hasCommand("rpm-ostree"))
                backends.Add(new(PackageManager.RpmOstree, "rpm-ostree"));
            else
                note = "OSTree deployment detected. Native packages need a verified deployment-specific workflow.";
        }
        else if (id is "opensuse-microos" or "opensuse-aeon" or "opensuse-kalpa" or "slemicro" or "sle-micro"
                 || variant is "microos" or "aeon" or "kalpa")
            note = "Transactional deployment detected. Native packages need a verified deployment-specific workflow.";
        else
        {
            PackageBackend[] detected = LinuxNative
                .Select(backend => backend.Manager == PackageManager.Dnf && !hasCommand("dnf")
                    ? backend with { Executable = "dnf5" }
                    : backend)
                .Where(backend => hasCommand(backend.Executable))
                .ToArray();
            var family = FamilyFor(id);
            if (family is null && fields.TryGetValue("ID_LIKE", out string? related))
                family = related.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                    .Select(FamilyFor).FirstOrDefault(match => match is not null);

            PackageBackend? native = family is null
                ? detected.Length == 1 ? detected[0] : null
                : detected.FirstOrDefault(backend => backend.Manager == family);
            if (native is not null)
                backends.Add(native);
            else
                note = "No unambiguous native package backend was detected.";
        }

        if (hasCommand("flatpak"))
            backends.Add(new(PackageManager.Flatpak, "flatpak"));

        return new PackageProfile("linux", architecture, id, fields.GetValueOrDefault("VERSION_ID"), variant, backends, note);
    }

    private static PackageManager? FamilyFor(string? id) => id switch
    {
        "debian" or "ubuntu" or "linuxmint" or "pop" or "raspbian" or "zorin" or "kali" => PackageManager.Apt,
        "fedora" or "rhel" or "centos" or "rocky" or "almalinux" => PackageManager.Dnf,
        "arch" or "manjaro" or "endeavouros" or "cachyos" => PackageManager.Pacman,
        "suse" or "opensuse" or "opensuse-leap" or "opensuse-tumbleweed" or "sles" => PackageManager.Zypper,
        "alpine" => PackageManager.Apk,
        "void" => PackageManager.Xbps,
        "gentoo" => PackageManager.Emerge,
        _ => null
    };

    private static Dictionary<string, string> ParseOsRelease(IEnumerable<string> lines)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string rawLine in lines)
        {
            string line = rawLine.Trim();
            int separator = line.IndexOf('=');
            if (separator < 1 || line.StartsWith('#'))
                continue;

            string value = line[(separator + 1)..].Trim();
            if (value.Length >= 2 && value[0] is '\'' or '"' && value[^1] == value[0])
                value = value[1..^1];
            fields[line[..separator]] = value;
        }
        return fields;
    }

    private static bool IsOnPath(string command)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (string entry in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            string directory = entry.Trim('"');
            if (!Path.IsPathFullyQualified(directory))
                continue;
            string file = Path.Combine(directory, command);
            if (!File.Exists(file))
                continue;
            if (OperatingSystem.IsWindows())
                return true;
            try
            {
                const UnixFileMode execute = UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
                if ((File.GetUnixFileMode(file) & execute) != 0)
                    return true;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return false;
    }
}
