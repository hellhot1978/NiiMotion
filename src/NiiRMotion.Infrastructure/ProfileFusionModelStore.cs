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
        var content = JsonSerializer.Serialize(model.Safe(), new JsonSerializerOptions { WriteIndented = true });
        if (File.Exists(path) && !string.Equals(await File.ReadAllTextAsync(path, token), content, StringComparison.Ordinal)) Backup(path, model.ProfileId);
        await File.WriteAllTextAsync(temporary, content, token);
        File.Move(temporary, path, true);
    }
    public bool RestorePrevious(string profileId)
    {
        var path = PathFor(profileId); var history = History(profileId); if (!Directory.Exists(history)) return false;
        var previous = new DirectoryInfo(history).GetFiles("*.json").OrderByDescending(x => x.Name).FirstOrDefault(); if (previous is null) return false;
        if (File.Exists(path)) Backup(path, profileId); File.Copy(previous.FullName, path, true); previous.Delete(); return Load(profileId) is not null;
    }
    public void Invalidate(string profileId) { var path = PathFor(profileId); if (File.Exists(path)) { Backup(path, profileId); File.Delete(path); } }
    public int HistoryCount(string profileId) => Directory.Exists(History(profileId)) ? Directory.GetFiles(History(profileId), "*.json").Length : 0;
    private void Backup(string path, string profileId)
    {
        var history = History(profileId); Directory.CreateDirectory(history); File.Copy(path, Path.Combine(history, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + ".json"), true);
        foreach (var old in new DirectoryInfo(history).GetFiles("*.json").OrderByDescending(x => x.Name).Skip(20)) old.Delete();
    }
    private string History(string profileId) => Path.Combine(_directory, "history", SafeName(profileId));
    private static string SafeName(string value) => string.Concat(value.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_'));
}
