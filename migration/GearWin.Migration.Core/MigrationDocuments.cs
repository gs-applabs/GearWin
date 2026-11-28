using System.Text.Json;
using System.Text.Json.Serialization;

namespace GearWin.Migration;

// Common, strict document contract for the CLI and the Windows planning UI.
public sealed record ProfileDocument(int SchemaVersion, PackageProfile Profile);
public sealed record PlanDocument(int SchemaVersion, PackageProfile Target, IReadOnlyList<ApplicationPlan> Applications,
    IReadOnlyList<string>? InventoryWarnings, IReadOnlyList<string>? DestinationInventoryWarnings = null);

public static class MigrationDocuments
{
    private const int MaxJsonBytes = 16 * 1024 * 1024;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        AllowDuplicateProperties = false,
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) }
    };

    public static T Read<T>(string path) where T : class =>
        JsonSerializer.Deserialize<T>(BoundedText.ReadFile(path, MaxJsonBytes), Json) ??
            throw new JsonException("Expected a JSON object.");

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Json);

    public static string SerializeFile<T>(T value)
    {
        string json = Serialize(value);
        if (System.Text.Encoding.UTF8.GetByteCount(json) > MaxJsonBytes)
            throw new IOException("Migration document exceeds the 16 MiB input limit.");
        return json;
    }

    public static PackageProfile ReadTarget(string path)
    {
        var document = Read<ProfileDocument>(path);
        if (document.SchemaVersion != PackagePlanner.SchemaVersion)
            throw new ArgumentException($"Unsupported target profile schema version {document.SchemaVersion}.");
        return document.Profile;
    }

    public static PlanDocument CreatePlan(PackageProfile target, PackageManifest manifest, ApplicationInventory? inventory = null,
        ApplicationInventory? destination = null) =>
        new(PackagePlanner.SchemaVersion, target, PackagePlanner.CreatePlan(target, manifest, inventory, destination),
            inventory?.Warnings, destination?.Warnings);
}
