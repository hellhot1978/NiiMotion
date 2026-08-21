using System.Text.Json;
using System.Text.Json.Nodes;

namespace NiiRMotion.Infrastructure;

public static class AlyxBindingOverride
{
    public static string RemovePhysicalForwardVector(string json) =>
        RemovePhysicalVector(json, "/actions/move/in/teleportturn", ["/user/hand/left/input/joystick"]);

    public static string RemoveArizonaSunshine2PhysicalMovement(string json) =>
        RemovePhysicalVector(json, "/actions/vertigo/in/axis0_axis2d",
            ["/user/hand/left/input/joystick", "/user/hand/right/input/joystick"]);

    private static string RemovePhysicalVector(string json, string outputAction, IReadOnlyCollection<string> sourcePaths)
    {
        var root = JsonNode.Parse(json)?.AsObject() ?? throw new InvalidDataException("Oyun kontrol bağlaması okunamadı.");
        var sources = root["bindings"]?.AsObject().SelectMany(binding => binding.Value?["sources"]?.AsArray() ?? []).ToArray();
        if (sources is null or { Length: 0 }) throw new InvalidDataException("Oyun hareket bağlaması bulunamadı.");

        foreach (var sourceNode in sources)
        {
            if (sourceNode is not JsonObject source ||
                source["path"] is null || !sourcePaths.Any(path => string.Equals(path, source["path"]!.GetValue<string>(), StringComparison.OrdinalIgnoreCase)) ||
                source["inputs"] is not JsonObject inputs ||
                inputs["position"] is not JsonObject position ||
                !string.Equals(position["output"]?.GetValue<string>(), outputAction, StringComparison.OrdinalIgnoreCase)) continue;

            inputs.Remove("position");
        }

        EnsureRightStickTurn(root);
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private static void EnsureRightStickTurn(JsonObject root)
    {
        var sources = root["bindings"]?.AsObject().SelectMany(binding => binding.Value?["sources"]?.AsArray() ?? []);
        if (sources is null) return;
        foreach (var sourceNode in sources)
        {
            if (sourceNode is not JsonObject source ||
                !string.Equals(source["path"]?.GetValue<string>(), "/user/hand/right/input/joystick", StringComparison.OrdinalIgnoreCase) ||
                source["inputs"]?["position"]?["output"]?.GetValue<string>() is not { } output ||
                !string.Equals(output, "/actions/move/in/continuousturn", StringComparison.OrdinalIgnoreCase)) continue;

            var parameters = source["parameters"] as JsonObject ?? new JsonObject();
            parameters["deadzone_pct"] = "10";
            parameters["sticky_click"] = "false";
            source["parameters"] = parameters;
        }
    }
}
