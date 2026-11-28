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
public sealed record ApplicationIdentity(InventorySource Kind, string? Id = null, string? Name = null, string? Publisher = null)
{
    internal ApplicationIdentity Normalize() => new(Kind,
        Kind is InventorySource.WindowsRegistry or InventorySource.WindowsStorePackage ? Id?.ToUpperInvariant() : Id,
        Name?.ToUpperInvariant(), Publisher?.ToUpperInvariant());

    internal static IEnumerable<ApplicationIdentity> KeysFor(InstalledApplication application)
    {
        var identity = new ApplicationIdentity(application.Kind, application.Id, application.Name, application.Publisher).Normalize();
        yield return identity with { Name = null, Publisher = null };
        yield return identity with { Id = null, Publisher = null };
        yield return identity with { Publisher = null };
        if (identity.Publisher is not null)
        {
            yield return identity with { Name = null };
            yield return identity with { Id = null };
            yield return identity;
        }
    }
}

public sealed record ApplicationRequest(string Name, IReadOnlyList<PackageCandidate> Candidates,
    IReadOnlyList<ApplicationIdentity>? Sources = null);
public sealed record PackageManifest(int SchemaVersion, IReadOnlyList<ApplicationRequest> Applications);

public enum PlanStatus { CommandPreview, Manual, Unresolved }

public sealed record CommandPreview(string Executable, IReadOnlyList<string> Arguments);
public enum DestinationMatchStatus { ExplicitIdentityFound, NotConfirmed, MultipleMatches }
public sealed record DestinationInventoryMatch(DestinationMatchStatus Status, string? Version = null);

public sealed record ApplicationPlan(string Name, PlanStatus Status, PackageCandidate? Candidate,
    CommandPreview? Command, string Note, InstalledApplication? Source = null, PackageVerification? Verification = null,
    DestinationInventoryMatch? DestinationMatch = null);

public static partial class PackagePlanner
{
    public const int SchemaVersion = 1;

    [GeneratedRegex(@"\A[A-Za-z0-9][A-Za-z0-9._+:-]*\z", RegexOptions.CultureInvariant)]
    private static partial Regex SimplePackageId();

    [GeneratedRegex(@"\A[a-z0-9][a-z0-9.+-]+(?::[a-z0-9][a-z0-9-]*)?\z", RegexOptions.CultureInvariant)]
    private static partial Regex AptPackageId();

    [GeneratedRegex(@"\A[A-Za-z0-9][A-Za-z0-9._+-]*/[A-Za-z0-9][A-Za-z0-9._+-]*\z", RegexOptions.CultureInvariant)]
    private static partial Regex GentooPackageId();

    [GeneratedRegex(@"\A[A-Za-z_][A-Za-z0-9_-]*(\.[A-Za-z_][A-Za-z0-9_-]*){2,}\z", RegexOptions.CultureInvariant)]
    private static partial Regex FlatpakAppId();

    public static IReadOnlyList<ApplicationPlan> CreatePlan(PackageProfile target, PackageManifest manifest,
        ApplicationInventory? source = null, ApplicationInventory? destination = null)
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

        foreach (ApplicationRequest request in manifest.Applications)
        {
            if (request is null || string.IsNullOrWhiteSpace(request.Name))
                throw new ArgumentException("Every application needs a name.");
            foreach (PackageCandidate candidate in request.Candidates)
                Validate(request.Name, candidate);
            if (request.Sources?.Any(identity => identity is null || !Enum.IsDefined(identity.Kind) ||
                    (identity.Id is null && identity.Name is null) ||
                    (identity.Id is not null && string.IsNullOrWhiteSpace(identity.Id)) ||
                    (identity.Name is not null && string.IsNullOrWhiteSpace(identity.Name)) ||
                    (identity.Publisher is not null && string.IsNullOrWhiteSpace(identity.Publisher))) == true)
                throw new ArgumentException($"Every source identity for {request.Name} needs an application ID or name.");
        }

        var identities = manifest.Applications.SelectMany(request =>
            (request.Sources ?? []).Select(identity => (Identity: identity.Normalize(), Request: request)))
            .ToLookup(entry => entry.Identity, entry => entry.Request);

        ApplicationRequest[] Match(InstalledApplication application) =>
            ApplicationIdentity.KeysFor(application).SelectMany(key => identities[key])
                .Distinct<ApplicationRequest>(ReferenceEqualityComparer.Instance).Take(2).ToArray();

        Dictionary<ApplicationRequest, DestinationInventoryMatch>? destinationMatches = null;
        if (destination != null)
        {
            if (destination.SchemaVersion != SchemaVersion || destination.OperatingSystem != target.OperatingSystem)
                throw new ArgumentException("Destination inventory schema or operating system does not match the target profile.");
            destinationMatches = new(ReferenceEqualityComparer.Instance);
            foreach (var application in destination.Applications)
            {
                ValidateInventoryEntry(application);
                if (application.Kind == InventorySource.WindowsSelectedExecutable) continue;
                var matches = Match(application);
                if (matches.Length != 1) continue;
                var request = matches[0];
                destinationMatches[request] = destinationMatches.ContainsKey(request)
                    ? new(DestinationMatchStatus.MultipleMatches)
                    : new(DestinationMatchStatus.ExplicitIdentityFound, application.Version);
            }
        }

        ApplicationPlan ForRequest(ApplicationRequest request)
        {
            var entry = PlanApplication(target, backends, request);
            return destinationMatches == null ? entry : entry with
            {
                DestinationMatch = destinationMatches.GetValueOrDefault(request) ??
                    new DestinationInventoryMatch(DestinationMatchStatus.NotConfirmed)
            };
        }

        if (source is null)
            return manifest.Applications.Select(ForRequest).ToArray();
        if (source.SchemaVersion != SchemaVersion)
            throw new ArgumentException($"Unsupported inventory schema version {source.SchemaVersion}.");

        var plan = new List<ApplicationPlan>(source.Applications.Count);
        foreach (InstalledApplication application in source.Applications)
        {
            ValidateInventoryEntry(application);
            if (application.Kind == InventorySource.WindowsSelectedExecutable)
            {
                plan.Add(new(application.Name, PlanStatus.Manual, null, null,
                    "User-selected executable: verify product identity, license and data manually.", application));
                continue;
            }

            var matches = Match(application);
            plan.Add(matches.Length switch
            {
                1 => ForRequest(matches[0]) with { Source = application },
                0 => new(application.Name, PlanStatus.Unresolved, null, null,
                    "No explicit catalog identity matches this installed application.", application),
                _ => new(application.Name, PlanStatus.Unresolved, null, null,
                    "More than one catalog application matches this identity. Review the catalog.", application)
            });
        }
        return plan;
    }

    private static void ValidateInventoryEntry(InstalledApplication application)
    {
        if (application is null || string.IsNullOrWhiteSpace(application.Id) ||
            string.IsNullOrWhiteSpace(application.Name) || !Enum.IsDefined(application.Kind))
            throw new ArgumentException("The inventory contains an invalid application.");
    }

    private static ApplicationPlan PlanApplication(PackageProfile target, ILookup<PackageManager, PackageBackend> backends,
        ApplicationRequest request)
    {
        var candidate = request.Candidates.FirstOrDefault(candidate =>
            backends.Contains(candidate.Manager) && (candidate.Target?.Matches(target) ?? true));
        if (candidate is null)
            return new(request.Name, PlanStatus.Unresolved, null, null,
                "No catalog entry matches the destination profile and its detected backends.");

        if (candidate.Manager == PackageManager.Apt && candidate.PackageId.Split(':')[0][^1] is '+' or '-')
            return new(request.Name, PlanStatus.Manual, candidate, null,
                "Confirm this exact APT package name. Trailing + and - have special meaning in install requests.");
        if (candidate.Manager == PackageManager.NixOs)
            return new(request.Name, PlanStatus.Manual, candidate, null,
                "Review this package in the target's NixOS configuration before rebuilding.");
        if (candidate.Manager == PackageManager.Guix)
            return new(request.Name, PlanStatus.Manual, candidate, null,
                "Review this package in the target's Guix configuration or user profile.");

        PackageBackend backend = backends[candidate.Manager].First();
        string note = backend.Manager is PackageManager.RpmOstree or PackageManager.TransactionalUpdate
            ? "System package changes are staged. Check availability and reboot requirements on the destination."
            : "Check package availability and compatibility on the destination before installing.";
        return new(request.Name, PlanStatus.CommandPreview, candidate, BuildCommand(backend, candidate), note);
    }

    private static void Validate(string appName, PackageCandidate candidate)
    {
        if (candidate is null || !Enum.IsDefined(candidate.Manager) || string.IsNullOrEmpty(candidate.PackageId))
            throw new ArgumentException($"Invalid package candidate for {appName}.");

        Regex pattern = candidate.Manager switch
        {
            PackageManager.Emerge => GentooPackageId(),
            PackageManager.Flatpak => FlatpakAppId(),
            PackageManager.Apt => AptPackageId(),
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

    internal static bool IsLocalQueryIdentifier(PackageCandidate candidate) => candidate.Manager switch
    {
        PackageManager.Apt => AptPackageId().IsMatch(candidate.PackageId),
        PackageManager.Pacman or PackageManager.Dnf or PackageManager.Zypper => SimplePackageId().IsMatch(candidate.PackageId),
        PackageManager.Apk or PackageManager.Xbps => SimplePackageId().IsMatch(candidate.PackageId) && !candidate.PackageId.Contains(':'),
        PackageManager.Flatpak => FlatpakAppId().IsMatch(candidate.PackageId),
        _ => false
    };

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
            PackageManager.Snap => new("snap", ["install", id]),
            PackageManager.Eopkg => new("eopkg", ["install", id]),
            PackageManager.Urpmi => new("urpmi", [id]),
            PackageManager.Slackpkg => new("slackpkg", ["install", id]),
            PackageManager.PackageKit => new("pkcon", ["install", id]),
            PackageManager.TransactionalUpdate => new("transactional-update", ["pkg", "install", id]),
            _ => throw new ArgumentOutOfRangeException(nameof(backend))
        };
    }
}
