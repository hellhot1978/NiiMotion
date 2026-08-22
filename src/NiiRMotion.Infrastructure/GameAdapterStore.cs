using System.Text.Json;
using System.Text.Json.Nodes;
using NiiRMotion.Core;

namespace NiiRMotion.Infrastructure;

public sealed class GameAdapterStore
{
    private readonly string _root;
    private string ConfigDirectory => Path.Combine(_root, "config");
    private string StorePath => Path.Combine(ConfigDirectory, "game-adapters.json");
    private string ProfilePath => Path.Combine(_root, "native", "openvr-driver", "dist", "resources", "input", "niirmotion_profile.json");
    private string BindingsDirectory => Path.Combine(Path.GetDirectoryName(ProfilePath)!, "default_bindings");
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public GameAdapterStore(string? root = null) => _root = root ?? NiiMotionPaths.Root;
    public bool HasOriginalProfileBackup => File.Exists(ProfilePath + ".niirmotion.backup");

    public IReadOnlyList<UserGameAdapter> Load()
    {
        try { return File.Exists(StorePath) ? JsonSerializer.Deserialize<UserGameAdapter[]>(File.ReadAllText(StorePath)) ?? [] : []; }
        catch (JsonException) { return []; }
        catch (IOException) { return []; }
    }

    public double LoadActiveSpeedMultiplier()
    {
        var selected = new GameSelectionStore(ConfigDirectory).Load();
        return Load().FirstOrDefault(x => x.Id.Equals(selected, StringComparison.OrdinalIgnoreCase))?.SpeedMultiplier ?? 1;
    }

    public void SaveAndInstall(UserGameAdapter adapter)
    {
        var errors = GameAdapterValidator.Validate(adapter);
        if (errors.Count > 0) throw new InvalidOperationException(string.Join(Environment.NewLine, errors));
        var adapters = Load().Where(x => !x.SteamAppId.Equals(adapter.SteamAppId, StringComparison.OrdinalIgnoreCase)).Append(adapter).ToArray();
        InstallBinding(adapter);
        Directory.CreateDirectory(ConfigDirectory);
        AtomicWrite(StorePath, JsonSerializer.Serialize(adapters, JsonOptions));
    }

    public bool Remove(string steamAppId)
    {
        var before = Load(); var remaining = before.Where(x => !x.SteamAppId.Equals(steamAppId, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (remaining.Length == before.Count) return false;
        RemoveProfileEntry(steamAppId); var binding = Path.Combine(BindingsDirectory, $"steam.app.{steamAppId}_niirmotion.json"); if (File.Exists(binding)) File.Delete(binding);
        AtomicWrite(StorePath, JsonSerializer.Serialize(remaining, JsonOptions));
        if (new GameSelectionStore(ConfigDirectory).Load() == $"user-steam-{steamAppId}") new GameSelectionStore(ConfigDirectory).Save("half-life-alyx");
        return true;
    }

    public GameAdapterRestoreResult RestoreOriginalProfile()
    {
        var backup = ProfilePath + ".niirmotion.backup"; if (!File.Exists(backup)) throw new InvalidOperationException("Geri yüklenecek özgün sürücü profili yedeği bulunamadı.");
        var adapters = Load(); Directory.CreateDirectory(Path.GetDirectoryName(ProfilePath)!);
        var safetyCopy = ProfilePath + $".before-restore-{DateTime.Now:yyyyMMdd-HHmmss}.json"; if (File.Exists(ProfilePath)) File.Copy(ProfilePath, safetyCopy, true);
        AtomicWrite(ProfilePath, File.ReadAllText(backup));
        foreach (var adapter in adapters) { var binding = Path.Combine(BindingsDirectory, $"steam.app.{adapter.SteamAppId}_niirmotion.json"); if (File.Exists(binding)) File.Delete(binding); }
        Directory.CreateDirectory(ConfigDirectory); AtomicWrite(StorePath, "[]"); new GameSelectionStore(ConfigDirectory).Save("half-life-alyx");
        return new GameAdapterRestoreResult(adapters.Count, safetyCopy);
    }

    private void InstallBinding(UserGameAdapter adapter)
    {
        Directory.CreateDirectory(BindingsDirectory);
        var fileName = $"steam.app.{adapter.SteamAppId}_niirmotion.json";
        var bindingPath = Path.Combine(BindingsDirectory, fileName);
        var inputs = new JsonObject { ["position"] = new JsonObject { ["output"] = adapter.MovementAction } };
        if (!string.IsNullOrWhiteSpace(adapter.ActivationAction)) inputs["click"] = new JsonObject { ["output"] = adapter.ActivationAction };
        var root = new JsonObject
        {
            ["action_manifest_version"] = 0, ["alias_info"] = new JsonObject(), ["app_key"] = $"steam.app.{adapter.SteamAppId}",
            ["bindings"] = new JsonObject { [adapter.ActionSet] = new JsonObject { ["sources"] = new JsonArray(new JsonObject
            {
                ["inputs"] = inputs, ["mode"] = "joystick", ["parameters"] = new JsonObject { ["deadzone_pct"] = "2", ["maxzone_pct"] = "100" }, ["path"] = "/user/treadmill/input/joystick"
            }) } },
            ["category"] = "steamvr_input", ["controller_type"] = "niirmotion_locomotion",
            ["description"] = $"NiiMotion user-created locomotion adapter for {adapter.Name}.", ["interaction_profile"] = "",
            ["name"] = $"NiiMotion - {adapter.Name}",
            ["options"] = new JsonObject { ["mirror_actions"] = false, ["returnBindingsWithLeftHand"] = true, ["returnBindingsWithRightHand"] = false, ["simulated_controller_type"] = "none" },
            ["simulated_actions"] = new JsonArray()
        };
        AtomicWrite(bindingPath, root.ToJsonString(JsonOptions));

        var profile = JsonNode.Parse(File.ReadAllText(ProfilePath))!.AsObject();
        var defaults = profile["default_bindings"]!.AsArray();
        for (var i = defaults.Count - 1; i >= 0; i--)
            if (defaults[i]?["app_key"]?.GetValue<string>() == $"steam.app.{adapter.SteamAppId}") defaults.RemoveAt(i);
        defaults.Add(new JsonObject { ["app_key"] = $"steam.app.{adapter.SteamAppId}", ["binding_url"] = $"default_bindings/{fileName}" });
        var backup = ProfilePath + ".niirmotion.backup";
        if (!File.Exists(backup)) File.Copy(ProfilePath, backup);
        AtomicWrite(ProfilePath, profile.ToJsonString(JsonOptions));
    }

    private void RemoveProfileEntry(string steamAppId)
    {
        if (!File.Exists(ProfilePath)) return; var profile = JsonNode.Parse(File.ReadAllText(ProfilePath))!.AsObject(); var defaults = profile["default_bindings"]?.AsArray(); if (defaults is null) return;
        for (var i = defaults.Count - 1; i >= 0; i--) if (defaults[i]?["app_key"]?.GetValue<string>() == $"steam.app.{steamAppId}") defaults.RemoveAt(i);
        AtomicWrite(ProfilePath, profile.ToJsonString(JsonOptions));
    }

    private static void AtomicWrite(string path, string content)
    {
        var temp = path + ".tmp"; File.WriteAllText(temp, content); File.Move(temp, path, true);
    }
}

public sealed record GameAdapterRestoreResult(int RemovedAdapterCount, string SafetyCopyPath);
