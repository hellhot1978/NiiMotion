using System.Text.Json;
using System.Text.Json.Nodes;

namespace NiiRMotion.Infrastructure;

public sealed record MigrationStepResult(string Name, bool Applied, string Detail);
public sealed record MigrationReport(int SchemaVersion, IReadOnlyList<MigrationStepResult> Steps, DateTimeOffset CompletedAtUtc);

public sealed class DataMigrationService
{
    public const int CurrentSchema = 3;
    private readonly string _config;
    public DataMigrationService(string? configDirectory = null) => _config = configDirectory ?? NiiMotionPaths.Config;
    public MigrationReport Run()
    {
        Directory.CreateDirectory(_config); var steps = new List<MigrationStepResult>();
        var statePath = Path.Combine(_config, "data-schema.json"); var previous = ReadVersion(statePath);
        if (previous < 1) steps.Add(NormalizeJson("user-hardware.json", "Donanım envanteri"));
        if (previous < 2) steps.Add(NormalizeJson("game-motion-profiles.json", "Oyun hareket profilleri"));
        if (previous < 3) steps.Add(NormalizeJson("calibration-progress.json", "Kalibrasyon ilerlemesi"));
        AtomicWrite(statePath, JsonSerializer.Serialize(new { schemaVersion = CurrentSchema, migratedFrom = previous, completedAtUtc = DateTimeOffset.UtcNow }, new JsonSerializerOptions { WriteIndented = true }));
        return new(CurrentSchema, steps, DateTimeOffset.UtcNow);
    }
    private MigrationStepResult NormalizeJson(string name, string label)
    {
        var path = Path.Combine(_config, name); if (!File.Exists(path)) return new(label, false, "Dosya henüz oluşmadı.");
        try
        {
            var node = JsonNode.Parse(File.ReadAllText(path)); if (node is null) throw new JsonException();
            var backup = path + $".pre-schema-{CurrentSchema}.backup"; if (!File.Exists(backup)) File.Copy(path, backup);
            AtomicWrite(path, node.ToJsonString(new JsonSerializerOptions { WriteIndented = true })); return new(label, true, "Doğrulandı ve geri dönüş kopyası alındı.");
        }
        catch { return new(label, false, "Dosya bozuk; değiştirilmedi ve tanı merkezinde bildirilecek."); }
    }
    private static int ReadVersion(string path) { try { using var doc = JsonDocument.Parse(File.ReadAllText(path)); return doc.RootElement.GetProperty("schemaVersion").GetInt32(); } catch { return 0; } }
    private static void AtomicWrite(string path, string content) { File.WriteAllText(path + ".tmp", content); File.Move(path + ".tmp", path, true); }
}
