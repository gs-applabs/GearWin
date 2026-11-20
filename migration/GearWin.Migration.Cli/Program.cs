using System.Text.Json;
using System.Text.Json.Serialization;
using GearWin.Migration;

var json = new JsonSerializerOptions(JsonSerializerDefaults.Web)
{
    WriteIndented = true,
    RespectNullableAnnotations = true,
    RespectRequiredConstructorParameters = true,
    Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) }
};

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
            WritePlan(manifestPath, options);
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
    Console.Error.WriteLine(error.Message);
    return 2;
}

T ReadJson<T>(string path) where T : class
{
    return JsonSerializer.Deserialize<T>(File.ReadAllText(path), json) ?? throw new JsonException("Expected a JSON object.");
}

void WriteJson<T>(T value) => Console.WriteLine(JsonSerializer.Serialize(value, json));

void WritePlan(string manifestPath, string[] options)
{
    string? targetPath = null;
    string? inventoryPath = null;
    for (int i = 0; i < options.Length; i += 2)
    {
        if (i + 1 == options.Length || options[i + 1].StartsWith("--", StringComparison.Ordinal))
            throw new ArgumentException($"Expected a file path after {options[i]}.");
        switch (options[i])
        {
            case "--target" when targetPath is null: targetPath = options[i + 1]; break;
            case "--inventory" when inventoryPath is null: inventoryPath = options[i + 1]; break;
            default: throw new ArgumentException($"Unknown or duplicate option {options[i]}.");
        }
    }

    PackageProfile target;
    if (targetPath is null)
        target = PackageDiscovery.InspectHost();
    else
    {
        var profile = ReadJson<ProfileDocument>(targetPath);
        if (profile.SchemaVersion != PackagePlanner.SchemaVersion)
            throw new ArgumentException($"Unsupported target profile schema version {profile.SchemaVersion}.");
        target = profile.Profile;
    }
    var manifest = ReadJson<PackageManifest>(manifestPath);
    var inventory = inventoryPath is null ? null : ReadJson<ApplicationInventory>(inventoryPath);
    var applications = PackagePlanner.CreatePlan(target, manifest, inventory);
    WriteJson(new PlanDocument(PackagePlanner.SchemaVersion, target, applications, inventory?.Warnings));
}

static void PrintUsage(TextWriter output)
{
    output.WriteLine("GearWin Migration — package inventory and installation preview");
    output.WriteLine("  inspect");
    output.WriteLine("  inventory");
    output.WriteLine("  plan <manifest.json> [--target <profile.json>] [--inventory <inventory.json>]");
}

sealed record ProfileDocument(int SchemaVersion, PackageProfile Profile);
sealed record PlanDocument(int SchemaVersion, PackageProfile Target, IReadOnlyList<ApplicationPlan> Applications,
    IReadOnlyList<string>? InventoryWarnings);
