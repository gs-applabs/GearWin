using System.Globalization;
using System.Text.Json;
using GearWin.Migration;

try
{
    switch (args)
    {
        case ["inspect"]:
            WriteJson(new ProfileDocument(PackagePlanner.SchemaVersion, PackageDiscovery.InspectHost()));
            return 0;
        case ["inventory"]:
            WriteJson(ApplicationDiscovery.InspectHost());
            return 0;
        case ["plan", string manifestPath, .. var options]:
            await WritePlanAsync(manifestPath, options);
            return 0;
        case ["--help"] or ["-h"]:
            PrintUsage(Console.Out);
            return 0;
        default:
            PrintUsage(Console.Error);
            return 2;
    }
}
catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
{
    foreach (char character in error.Message)
    {
        if (char.IsControl(character) || char.GetUnicodeCategory(character) == UnicodeCategory.Format)
            Console.Error.Write($"\\u{(int)character:x4}");
        else
            Console.Error.Write(character);
    }
    Console.Error.WriteLine();
    return 2;
}

void WriteJson<T>(T value) => Console.WriteLine(MigrationDocuments.Serialize(value));

async Task WritePlanAsync(string manifestPath, string[] options)
{
    string? targetPath = null;
    string? inventoryPath = null;
    bool verify = false;
    for (int i = 0; i < options.Length; i++)
    {
        if (options[i] == "--verify" && !verify)
        {
            verify = true;
            continue;
        }
        if (options[i] is not ("--target" or "--inventory"))
            throw new ArgumentException($"Unknown or duplicate option {options[i]}.");
        if (i + 1 == options.Length || options[i + 1].StartsWith("--", StringComparison.Ordinal))
            throw new ArgumentException($"Expected a file path after {options[i]}.");
        switch (options[i])
        {
            case "--target" when targetPath is null: targetPath = options[++i]; break;
            case "--inventory" when inventoryPath is null: inventoryPath = options[++i]; break;
            default: throw new ArgumentException($"Unknown or duplicate option {options[i]}.");
        }
    }

    if (verify && targetPath is not null)
        throw new ArgumentException("Run --verify on the destination and omit --target to query that computer's local metadata.");

    PackageProfile target;
    if (targetPath is null)
        target = PackageDiscovery.InspectHost();
    else target = MigrationDocuments.ReadTarget(targetPath);
    var manifest = MigrationDocuments.Read<PackageManifest>(manifestPath);
    var inventory = inventoryPath is null ? null : MigrationDocuments.Read<ApplicationInventory>(inventoryPath);
    var document = MigrationDocuments.CreatePlan(target, manifest, inventory);
    if (verify)
        document = document with { Applications = await PackageVerifier.VerifyAsync(document.Applications) };
    WriteJson(document);
}

static void PrintUsage(TextWriter output)
{
    output.WriteLine("GearWin Migration — package inventory and installation preview");
    output.WriteLine("  inspect");
    output.WriteLine("  inventory");
    output.WriteLine("  plan <manifest.json> [--target <profile.json> | --verify] [--inventory <inventory.json>]");
}

