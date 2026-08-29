using System.Text.Json;
using NiiRMotion.Core;

namespace NiiRMotion.Infrastructure;

public sealed class ProfileFusionModelStore(string? directory = null)
{
    private readonly string _directory = directory ?? Path.Combine(NiiMotionPaths.Config, "profile-fusion");
    public string PathFor(string profileId) => Path.Combine(_directory, SafeName(profileId) + ".json");
    public bool Exists(string profileId) => File.Exists(PathFor(profileId));
    public ProfileFusionModel? Load(string profileId)
    {
        try
        {
            var path = PathFor(profileId); if (!File.Exists(path)) return null;
            var model = JsonSerializer.Deserialize<ProfileFusionModel>(File.ReadAllText(path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return model?.ProfileId == profileId && model.Sensors.Count >= 2 && model.AcceptedSamples > 0 ? model.Safe() : null;
        }
        catch (JsonException) { return null; } catch (IOException) { return null; }
    }
    public async Task SaveAsync(ProfileFusionModel model, CancellationToken token = default)
    {
        Directory.CreateDirectory(_directory); var path = PathFor(model.ProfileId); var temporary = path + ".tmp";
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(model.Safe(), new JsonSerializerOptions { WriteIndented = true }), token);
        File.Move(temporary, path, true);
    }
    private static string SafeName(string value) => string.Concat(value.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_'));
}
