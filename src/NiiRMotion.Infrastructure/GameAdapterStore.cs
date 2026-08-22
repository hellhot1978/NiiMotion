using System.Text.Json;
using System.Text.Json.Nodes;
using NiiRMotion.Core;

namespace NiiRMotion.Infrastructure;

public sealed class GameAdapterStore
{
    private static string StorePath => Path.Combine(NiiMotionPaths.Config, "game-adapters.json");
    private static string ProfilePath => Path.Combine(NiiMotionPaths.Root, "native", "openvr-driver", "dist", "resources", "input", "niirmotion_profile.json");
    private static string BindingsDirectory => Path.Combine(Path.GetDirectoryName(ProfilePath)!, "default_bindings");
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public IReadOnlyList<UserGameAdapter> Load()
    {
        try { return File.Exists(StorePath) ? JsonSerializer.Deserialize<UserGameAdapter[]>(File.ReadAllText(StorePath)) ?? [] : []; }
        catch (JsonException) { return []; }
        catch (IOException) { return []; }
    }

    public double LoadActiveSpeedMultiplier()
    {
        var selected = new GameSelectionStore().Load();
        return Load().FirstOrDefault(x => x.Id.Equals(selected, StringComparison.OrdinalIgnoreCase))?.SpeedMultiplier ?? 1;
    }

    public void SaveAndInstall(UserGameAdapter adapter)
    {
        var errors = GameAdapterValidator.Validate(adapter);
        if (errors.Count > 0) throw new InvalidOperationException(string.Join(Environment.NewLine, errors));
        var adapters = Load().Where(x => !x.SteamAppId.Equals(adapter.SteamAppId, StringComparison.OrdinalIgnoreCase)).Append(adapter).ToArray();
        InstallBinding(adapter);
        Directory.CreateDirectory(NiiMotionPaths.Config);
        AtomicWrite(StorePath, JsonSerializer.Serialize(adapters, JsonOptions));
    }

    private static void InstallBinding(UserGameAdapter adapter)
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

    private static void AtomicWrite(string path, string content)
    {
        var temp = path + ".tmp"; File.WriteAllText(temp, content); File.Move(temp, path, true);
    }
}
