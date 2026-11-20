using System.Text.RegularExpressions;

namespace GearWin.Migration;

public sealed record PackageTarget(string? DistributionId = null, string? Version = null,
    string? VariantId = null, string? Architecture = null)
{
    internal bool Matches(PackageProfile profile) =>
        (DistributionId is null || DistributionId == profile.DistributionId) &&
        (Version is null || Version == profile.DistributionVersion) &&
        (VariantId is null || VariantId == profile.VariantId) &&
        (Architecture is null || Architecture == profile.Architecture);
}

public sealed record PackageCandidate(PackageManager Manager, string PackageId, string? Source = null, PackageTarget? Target = null);
public sealed record ApplicationRequest(string Name, IReadOnlyList<PackageCandidate> Candidates);
public sealed record PackageManifest(int SchemaVersion, IReadOnlyList<ApplicationRequest> Applications);

public enum PlanStatus { CommandPreview, Manual, Unresolved }

public sealed record CommandPreview(string Executable, IReadOnlyList<string> Arguments);
public sealed record ApplicationPlan(string Name, PlanStatus Status, PackageCandidate? Candidate, CommandPreview? Command, string Note);

public static partial class PackagePlanner
{
    public const int SchemaVersion = 1;

    [GeneratedRegex(@"\A[A-Za-z0-9][A-Za-z0-9._+:-]*\z", RegexOptions.CultureInvariant)]
    private static partial Regex SimplePackageId();

    [GeneratedRegex(@"\A[A-Za-z0-9][A-Za-z0-9._+-]*/[A-Za-z0-9][A-Za-z0-9._+-]*\z", RegexOptions.CultureInvariant)]
    private static partial Regex GentooPackageId();

    [GeneratedRegex(@"\A[A-Za-z_][A-Za-z0-9_-]*(\.[A-Za-z_][A-Za-z0-9_-]*){2,}\z", RegexOptions.CultureInvariant)]
    private static partial Regex FlatpakAppId();

    public static IReadOnlyList<ApplicationPlan> CreatePlan(PackageProfile target, PackageManifest manifest)
    {
        if (manifest.SchemaVersion != SchemaVersion)
            throw new ArgumentException($"Unsupported manifest schema version {manifest.SchemaVersion}.");
        if (target.Backends.Any(backend => backend is null || !Enum.IsDefined(backend.Manager)))
            throw new ArgumentException("The target profile contains an invalid backend.");

        var backends = target.Backends
            .Where(backend => target.OperatingSystem switch
            {
                "windows" => backend.Manager == PackageManager.Winget,
                "linux" => backend.Manager != PackageManager.Winget,
                _ => false
            })
            .ToLookup(backend => backend.Manager);

        var plan = new List<ApplicationPlan>(manifest.Applications.Count);
        foreach (ApplicationRequest request in manifest.Applications)
        {
            if (request is null || string.IsNullOrWhiteSpace(request.Name))
                throw new ArgumentException("Every application needs a name.");
            foreach (PackageCandidate candidate in request.Candidates)
                Validate(request.Name, candidate);

            var candidateOnTarget = request.Candidates.FirstOrDefault(candidate =>
                backends.Contains(candidate.Manager) && (candidate.Target?.Matches(target) ?? true));
            if (candidateOnTarget is null)
            {
                plan.Add(new(request.Name, PlanStatus.Unresolved, null, null,
                    "No catalog entry matches the destination profile and its detected backends."));
                continue;
            }

            if (candidateOnTarget.Manager == PackageManager.NixOs)
            {
                plan.Add(new(request.Name, PlanStatus.Manual, candidateOnTarget, null,
                    "Review this package in the target's NixOS configuration before rebuilding."));
                continue;
            }

            PackageBackend backend = backends[candidateOnTarget.Manager].First();
            string note = backend.Manager == PackageManager.RpmOstree
                ? "Package layering generally takes effect after reboot. Check availability on the destination."
                : "Check package availability and compatibility on the destination before installing.";
            plan.Add(new(request.Name, PlanStatus.CommandPreview, candidateOnTarget, BuildCommand(backend, candidateOnTarget), note));
        }
        return plan;
    }

    private static void Validate(string appName, PackageCandidate candidate)
    {
        if (candidate is null || !Enum.IsDefined(candidate.Manager) || string.IsNullOrEmpty(candidate.PackageId))
            throw new ArgumentException($"Invalid package candidate for {appName}.");

        Regex pattern = candidate.Manager switch
        {
            PackageManager.Emerge => GentooPackageId(),
            PackageManager.Flatpak => FlatpakAppId(),
            _ => SimplePackageId()
        };

        if (!pattern.IsMatch(candidate.PackageId))
            throw new ArgumentException($"Invalid {candidate.Manager} package ID for {appName}.");

        if (candidate.Source is null)
            return;

        bool supportedSource = candidate.Manager switch
        {
            PackageManager.Winget => candidate.Source is "winget" or "msstore",
            PackageManager.Flatpak => SimplePackageId().IsMatch(candidate.Source),
            _ => false
        };
        if (!supportedSource)
            throw new ArgumentException($"Unsupported {candidate.Manager} package source for {appName}.");
    }

    private static CommandPreview BuildCommand(PackageBackend backend, PackageCandidate candidate)
    {
        string id = candidate.PackageId;
        return backend.Manager switch
        {
            PackageManager.Winget => new("winget.exe", ["install", "--id", id, "--exact", "--source", candidate.Source ?? "winget"]),
            PackageManager.Apt => new("apt-get", ["install", id]),
            PackageManager.Dnf => new(backend.Executable == "dnf5" ? "dnf5" : "dnf", ["install", id]),
            PackageManager.Pacman => new("pacman", ["-S", id]),
            PackageManager.Zypper => new("zypper", ["install", id]),
            PackageManager.Apk => new("apk", ["add", id]),
            PackageManager.Xbps => new("xbps-install", [id]),
            PackageManager.Emerge => new("emerge", [id]),
            PackageManager.RpmOstree => new("rpm-ostree", ["install", id]),
            PackageManager.Flatpak => new("flatpak", ["install", candidate.Source ?? "flathub", id]),
            _ => throw new ArgumentOutOfRangeException(nameof(backend))
        };
    }
}
