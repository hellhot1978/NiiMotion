using System.Security.Cryptography;
using System.Text.Json;

namespace NiiRMotion.Infrastructure;

public sealed record ReleaseFileHash(string Path, long Bytes, string Sha256);
public sealed record ReleaseIntegrityManifest(int SchemaVersion, string Version, DateTimeOffset CreatedAtUtc, IReadOnlyList<ReleaseFileHash> Files);

public static class ReleaseIntegrityService
{
    public static ReleaseIntegrityManifest Create(string root, string version)
    {
        var names = new[] { "NiiRMotion.App.exe", Path.Combine("OpenXRLayer", "niirmotion_openxr.json"), Path.Combine("OpenXRLayer", "bin", "win64", "niirmotion_openxr.dll"), Path.Combine("OpenVRDriver", "bin", "win64", "driver_niirmotion.dll") };
        var files = names.Select(name => (Name: name.Replace('\\', '/'), Full: Path.Combine(root, name))).Where(x => File.Exists(x.Full)).Select(x =>
        {
            using var stream = File.OpenRead(x.Full); return new ReleaseFileHash(x.Name, stream.Length, Convert.ToHexString(SHA256.HashData(stream)));
        }).ToArray();
        return new(1, version, DateTimeOffset.UtcNow, files);
    }
    public static void Save(ReleaseIntegrityManifest manifest, string path) { Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true })); }
    public static bool Verify(string root, ReleaseIntegrityManifest manifest) => manifest.Files.Count > 0 && manifest.Files.All(item =>
    {
        var path = Path.Combine(root, item.Path.Replace('/', Path.DirectorySeparatorChar)); if (!File.Exists(path)) return false;
        using var stream = File.OpenRead(path); return stream.Length == item.Bytes && Convert.ToHexString(SHA256.HashData(stream)).Equals(item.Sha256, StringComparison.OrdinalIgnoreCase);
    });
}
