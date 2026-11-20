using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace GearWin.Migration;

public enum VerificationStatus { FoundInLocalMetadata, NotConfirmed, Unsupported, InstalledOnDestination }

public sealed record PackageVerification(VerificationStatus Status, string? Version, string? Architecture, string Detail);

public static partial class PackageVerifier
{
    private sealed record Query(string Executable, IReadOnlyList<string> Arguments);
    private sealed record QueryResult(string? Output, string? Error = null);
    private static readonly Query FlatpakList = new("/usr/bin/flatpak",
        ["list", "--app", "--columns=application:f,arch:f,version:f,origin:f"]);

    [GeneratedRegex(@"\A[0-9][A-Za-z0-9._+]*(?:-r[0-9]+)?\z", RegexOptions.CultureInvariant)]
    private static partial Regex ApkVersion();

    public static async Task<IReadOnlyList<ApplicationPlan>> VerifyAsync(IReadOnlyList<ApplicationPlan> plan)
    {
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var cache = new Dictionary<(PackageManager Manager, string PackageId, string? Source), PackageVerification>();
        var results = new List<ApplicationPlan>(plan.Count);
        Task<QueryResult>? flatpakInstalled = null;
        foreach (var entry in plan)
        {
            if (entry is not { Status: PlanStatus.CommandPreview, Candidate: { } candidate })
            {
                results.Add(entry);
                continue;
            }

            var key = (candidate.Manager, candidate.PackageId, candidate.Source);
            if (!cache.TryGetValue(key, out PackageVerification? result))
            {
                result = await VerifyPackageAsync(candidate, entry.Command?.Executable, budget.Token,
                    () => flatpakInstalled ??= RunQueryAsync(FlatpakList, budget.Token));
                cache.Add(key, result);
            }
            results.Add(entry with { Verification = result });
        }
        return results;
    }

    private static async Task<PackageVerification> VerifyPackageAsync(PackageCandidate candidate, string? command,
        CancellationToken budget, Func<Task<QueryResult>> flatpakInstalled)
    {
        Query? query = OperatingSystem.IsLinux() ? BuildQuery(candidate, command) : null;
        if (query is null)
            return new(VerificationStatus.Unsupported, null, null, "Local index verification is not implemented for this backend.");
        if (!PackagePlanner.IsLocalQueryIdentifier(candidate))
            return NotConfirmed("The package identifier cannot be verified as a literal name.");
        if (budget.IsCancellationRequested)
            return NotConfirmed("The verification time budget was reached.");

        var result = await (candidate.Manager == PackageManager.Flatpak ? flatpakInstalled() : RunQueryAsync(query, budget));
        string? nativeArchitecture = RuntimeInformation.OSArchitecture switch
        {
            Architecture.X64 => "x86_64",
            Architecture.Arm64 => "aarch64",
            Architecture.S390x => "s390x",
            Architecture.Ppc64le => "ppc64le",
            _ => null
        };
        return result.Output is null ? NotConfirmed(result.Error!) : ParseLocalMetadata(candidate, result.Output, nativeArchitecture);
    }

    private static Query? BuildQuery(PackageCandidate candidate, string? command)
    {
        string id = candidate.PackageId;
        return candidate.Manager switch
        {
            PackageManager.Apt => new("/usr/bin/apt-cache",
                ["-o", "Dir::Cache::pkgcache=", "-o", "Dir::Cache::srcpkgcache=", "--no-all-versions", "show", "--", id]),
            PackageManager.Pacman => new("/usr/bin/pacman", ["--color", "never", "-Si", "--", id]),
            PackageManager.Dnf => BuildDnfQuery(id, command),
            PackageManager.Zypper => new("/usr/bin/zypper",
                ["--non-interactive", "--no-refresh", "--xmlout", "search", "--match-exact", "--details", "--type", "package", "--", id]),
            PackageManager.Apk => new(FindApk(), ["--no-network", "list", "--available", "--", id]),
            PackageManager.Xbps => new("/usr/bin/xbps-query", ["-R", "--property=pkgver,architecture", "--", id]),
            PackageManager.Flatpak => FlatpakList,
            _ => null
        };
    }

    private static string FindApk() =>
        Array.Find(new[] { "/sbin/apk", "/usr/sbin/apk", "/usr/bin/apk", "/bin/apk" }, File.Exists) ?? "/sbin/apk";

    private static Query BuildDnfQuery(string id, string? command)
    {
        const string dnf = "/usr/bin/dnf";
        const string dnf5 = "/usr/bin/dnf5";
        bool newer = command == "dnf5";
        if (!newer && File.Exists(dnf5))
        {
            try { newer = File.ResolveLinkTarget(dnf, returnFinalTarget: true)?.Name == "dnf5"; }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        return new(newer ? dnf5 : dnf,
            ["--cacheonly", newer ? "--no-plugins" : "--noplugins", "repoquery", "--available", "--latest-limit=1",
                "--queryformat", "%{name}\t%{evr}\t%{arch}\n", "--", id]);
    }

    private static async Task<QueryResult> RunQueryAsync(Query query, CancellationToken budget)
    {
        var start = new ProcessStartInfo(query.Executable)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            WorkingDirectory = "/"
        };
        foreach (string argument in query.Arguments)
            start.ArgumentList.Add(argument);
        start.Environment.Clear();
        // Keep the user's package caches and default Flatpak installation visible.
        foreach (string key in new[] { "HOME", "XDG_CACHE_HOME", "XDG_CONFIG_HOME", "XDG_DATA_HOME", "XDG_RUNTIME_DIR" })
            if (Environment.GetEnvironmentVariable(key) is { Length: > 0 } value)
                start.Environment[key] = value;
        start.Environment["PYTHONNOUSERSITE"] = "1";
        start.Environment["LC_ALL"] = "C";
        start.Environment["PATH"] = "/usr/sbin:/usr/bin:/sbin:/bin";

        using var process = new Process { StartInfo = start };
        try
        {
            if (!process.Start())
                return new(null, "The local metadata query could not start.");
        }
        catch (Win32Exception)
        {
            return new(null, "The local metadata query executable is unavailable.");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(budget);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            process.StandardInput.Close();
            var stdout = ReadLimitedAsync(process.StandardOutput, timeout.Token);
            var stderr = ReadLimitedAsync(process.StandardError, timeout.Token);
            await Task.WhenAll(process.WaitForExitAsync(timeout.Token), stdout, stderr);
            if (process.ExitCode != 0 || stdout.Result is null || stderr.Result is null)
                return new(null, "The local package manager did not return usable metadata.");
            return new(stdout.Result);
        }
        catch (OperationCanceledException)
        {
            TryTerminate(process);
            return new(null, "The local metadata query timed out.");
        }
        catch (IOException)
        {
            TryTerminate(process);
            return new(null, "The local metadata query could not be read.");
        }
    }

    public static PackageVerification ParseLocalMetadata(PackageCandidate candidate, string output, string? nativeArchitecture = null) =>
        candidate.Manager switch
        {
            PackageManager.Apt or PackageManager.Pacman => ParseFields(candidate, output),
            PackageManager.Dnf => ParseDnf(candidate, output, nativeArchitecture),
            PackageManager.Zypper => ParseZypper(candidate, output, nativeArchitecture),
            PackageManager.Apk => ParseApk(candidate, output, nativeArchitecture),
            PackageManager.Xbps => ParseXbps(candidate, output, nativeArchitecture),
            PackageManager.Flatpak => ParseFlatpak(candidate, output, nativeArchitecture),
            _ => throw new ArgumentException("No local metadata parser for this backend.")
        };

    private static PackageVerification ParseFields(PackageCandidate candidate, string output)
    {
        string[] requested = candidate.PackageId.Split(':', 2);
        string nameKey = candidate.Manager == PackageManager.Apt ? "Package" : "Name";

        foreach (string record in output.Replace("\r\n", "\n", StringComparison.Ordinal).Split("\n\n"))
        {
            var fields = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string line in record.Split('\n'))
            {
                int colon = line.IndexOf(':');
                if (colon > 0 && !char.IsWhiteSpace(line[0]))
                    fields.TryAdd(line[..colon].TrimEnd(), line[(colon + 1)..].Trim());
            }

            string? version = fields.GetValueOrDefault("Version");
            string? arch = fields.GetValueOrDefault("Architecture");
            if (fields.GetValueOrDefault(nameKey) == requested[0] && !string.IsNullOrWhiteSpace(version) &&
                !string.IsNullOrWhiteSpace(arch) && (requested.Length == 1 || requested[1] == arch || arch == "all"))
                return Found(version, arch);
        }
        return Missing();
    }

    private static PackageVerification ParseApk(PackageCandidate candidate, string output, string? nativeArchitecture)
    {
        string prefix = candidate.PackageId + "-";
        using var reader = new StringReader(output);
        while (reader.ReadLine() is { } line)
        {
            string[] fields = line.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
            if (fields is not [var package, var arch, _] || !package.StartsWith(prefix, StringComparison.Ordinal))
                continue;

            string version = package[prefix.Length..];
            if (ApkVersion().IsMatch(version) && HasPackageVersion(version, arch, nativeArchitecture))
                return Found(version, arch);
        }
        return Missing();
    }

    private static PackageVerification ParseXbps(PackageCandidate candidate, string output, string? nativeArchitecture)
    {
        using var reader = new StringReader(output);
        string? package = reader.ReadLine();
        string? arch = reader.ReadLine();
        int separator = package?.LastIndexOf('-') ?? -1;
        if (separator < 1 || package![..separator] != candidate.PackageId || reader.ReadLine() is not null)
            return Missing();

        string version = package[(separator + 1)..];
        string? comparisonArchitecture = arch?.EndsWith("-musl", StringComparison.Ordinal) == true ? arch[..^5] : arch;
        return HasPackageVersion(version, comparisonArchitecture, nativeArchitecture) ? Found(version, arch!) : Missing();
    }

    private static PackageVerification ParseDnf(PackageCandidate candidate, string output, string? nativeArchitecture)
    {
        foreach (string line in output.Split('\n'))
        {
            string[] columns = line.TrimEnd('\r').Split('\t');
            if (columns is [var name, var version, var arch] && name == candidate.PackageId &&
                HasPackageVersion(version, arch, nativeArchitecture))
                return Found(version, arch);
        }
        return Missing();
    }

    private static PackageVerification ParseZypper(PackageCandidate candidate, string output, string? nativeArchitecture)
    {
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 128 * 1024 };
        try
        {
            using var reader = XmlReader.Create(new StringReader(output), settings);
            PackageVerification? found = null;
            while (reader.Read())
            {
                if (reader.NodeType != XmlNodeType.Element || reader.Name != "solvable" ||
                    reader.GetAttribute("kind") != "package" || reader.GetAttribute("name") != candidate.PackageId)
                    continue;

                string? version = reader.GetAttribute("edition");
                string? arch = reader.GetAttribute("arch");
                if (found is null && HasPackageVersion(version, arch, nativeArchitecture))
                    found = Found(version!, arch!);
            }
            return found ?? Missing();
        }
        catch (XmlException)
        {
            return NotConfirmed("Zypper did not return valid package metadata XML.");
        }
    }

    private static PackageVerification ParseFlatpak(PackageCandidate candidate, string output, string? nativeArchitecture)
    {
        foreach (string line in output.Split('\n'))
        {
            string[] columns = line.TrimEnd('\r').Split('\t');
            if (columns is [var id, var arch, var version, var origin] && id == candidate.PackageId &&
                origin == (candidate.Source ?? "flathub") && !string.IsNullOrWhiteSpace(arch) &&
                (nativeArchitecture is null || nativeArchitecture == arch))
                return new(VerificationStatus.InstalledOnDestination, string.IsNullOrWhiteSpace(version) || version == "-" ? null : version,
                    arch, "Exact installed Flatpak found with the requested origin.");
        }

        return NotConfirmed("No matching installed Flatpak was confirmed. Remote availability was not queried.");
    }

    private static bool HasPackageVersion(string? version, string? arch, string? nativeArchitecture) =>
        !string.IsNullOrWhiteSpace(version) && !string.IsNullOrWhiteSpace(arch) &&
        (nativeArchitecture is null || arch == nativeArchitecture || arch == "noarch");

    private static PackageVerification Found(string version, string arch) => new(VerificationStatus.FoundInLocalMetadata,
        version, arch, "Exact package record found in local metadata. Repository contents may have changed.");

    private static PackageVerification Missing() => NotConfirmed("No exact package record was confirmed in the local metadata.");

    private static PackageVerification NotConfirmed(string detail) => new(VerificationStatus.NotConfirmed, null, null, detail);

    private static async Task<string?> ReadLimitedAsync(TextReader stream, CancellationToken cancellationToken)
    {
        const int limit = 128 * 1024;
        var output = new StringBuilder();
        var buffer = new char[4096];
        bool truncated = false;
        int count;
        while ((count = await stream.ReadAsync(buffer.AsMemory(), cancellationToken)) != 0)
        {
            if (count > limit - output.Length)
                truncated = true;
            if (!truncated)
                output.Append(buffer, 0, count);
        }
        return truncated ? null : output.ToString();
    }

    private static void TryTerminate(Process process)
    {
        try { process.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { }
        catch (Win32Exception) { }
    }
}
