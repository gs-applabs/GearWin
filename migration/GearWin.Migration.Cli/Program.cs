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
        case ["plan", string manifestPath]:
            WritePlan(manifestPath, PackageDiscovery.InspectHost());
            return 0;
        case ["plan", string manifestPath, "--target", string targetPath]:
            var target = ReadJson<ProfileDocument>(targetPath);
            if (target.SchemaVersion != PackagePlanner.SchemaVersion)
                throw new ArgumentException($"Unsupported target profile schema version {target.SchemaVersion}.");
            WritePlan(manifestPath, target.Profile);
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

void WritePlan(string manifestPath, PackageProfile target)
{
    var manifest = ReadJson<PackageManifest>(manifestPath);
    var applications = PackagePlanner.CreatePlan(target, manifest);
    WriteJson(new PlanDocument(PackagePlanner.SchemaVersion, target, applications));
}

static void PrintUsage(TextWriter output)
{
    output.WriteLine("GearWin Migration — package inventory and installation preview");
    output.WriteLine("  inspect");
    output.WriteLine("  plan <manifest.json> [--target <profile.json>]");
}

sealed record ProfileDocument(int SchemaVersion, PackageProfile Profile);
sealed record PlanDocument(int SchemaVersion, PackageProfile Target, IReadOnlyList<ApplicationPlan> Applications);
