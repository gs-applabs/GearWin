using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace GearWin.Migration;

public enum VerificationStatus { FoundInLocalMetadata, NotConfirmed, Unsupported }

public sealed record PackageVerification(VerificationStatus Status, string? Version, string? Architecture, string Detail);

public static class PackageVerifier
{
    public static async Task<IReadOnlyList<ApplicationPlan>> VerifyAsync(IReadOnlyList<ApplicationPlan> plan)
    {
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var cache = new Dictionary<(PackageManager Manager, string PackageId), PackageVerification>();
        var results = new List<ApplicationPlan>(plan.Count);
        foreach (var entry in plan)
        {
            if (entry is not { Status: PlanStatus.CommandPreview, Candidate: { } candidate })
            {
                results.Add(entry);
                continue;
            }

            var key = (candidate.Manager, candidate.PackageId);
            if (!cache.TryGetValue(key, out PackageVerification? result))
            {
                result = await VerifyPackageAsync(candidate, budget.Token);
                cache.Add(key, result);
            }
            results.Add(entry with { Verification = result });
        }
        return results;
    }

    private static async Task<PackageVerification> VerifyPackageAsync(PackageCandidate candidate, CancellationToken budget)
    {
        if (!OperatingSystem.IsLinux() || candidate.Manager is not (PackageManager.Apt or PackageManager.Pacman))
            return new(VerificationStatus.Unsupported, null, null, "Local index verification is not implemented for this backend.");
        if (!PackagePlanner.IsLocalQueryIdentifier(candidate))
            return NotConfirmed("The package identifier cannot be verified as a literal name.");
        if (budget.IsCancellationRequested)
            return NotConfirmed("The verification time budget was reached.");

        string executable = candidate.Manager == PackageManager.Apt ? "/usr/bin/apt-cache" : "/usr/bin/pacman";
        string[] arguments = candidate.Manager == PackageManager.Apt
            ? ["-o", "Dir::Cache::pkgcache=", "-o", "Dir::Cache::srcpkgcache=", "--no-all-versions", "show", "--", candidate.PackageId]
            : ["--color", "never", "-Si", "--", candidate.PackageId];
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            WorkingDirectory = "/"
        };
        foreach (string argument in arguments)
            start.ArgumentList.Add(argument);
        start.Environment.Remove("APT_CONFIG");
        start.Environment.Remove("LD_PRELOAD");
        start.Environment.Remove("LD_LIBRARY_PATH");
        start.Environment["LC_ALL"] = "C";
        start.Environment["PATH"] = "/usr/sbin:/usr/bin:/sbin:/bin";

        using var process = new Process { StartInfo = start };
        try
        {
            if (!process.Start())
                return NotConfirmed("The package index query could not start.");
        }
        catch (Win32Exception)
        {
            return NotConfirmed("The package index query executable is unavailable.");
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
                return NotConfirmed("The local package index did not return usable metadata.");
            return ParseLocalMetadata(candidate, stdout.Result);
        }
        catch (OperationCanceledException)
        {
            TryTerminate(process);
            return NotConfirmed("The local package index query timed out.");
        }
        catch (IOException)
        {
            TryTerminate(process);
            return NotConfirmed("The local package index query could not be read.");
        }
    }

    public static PackageVerification ParseLocalMetadata(PackageCandidate candidate, string output)
    {
        if (candidate.Manager is not (PackageManager.Apt or PackageManager.Pacman))
            throw new ArgumentException("No local metadata parser for this backend.");

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
                return new(VerificationStatus.FoundInLocalMetadata, version, arch,
                    "Exact package record found in local metadata. Repository contents may have changed.");
        }
        return NotConfirmed("No exact package record was confirmed in the local metadata.");
    }

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
