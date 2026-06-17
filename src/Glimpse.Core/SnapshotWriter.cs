using System.Text.Json;

namespace Glimpse.Core;

/// <summary>Owns all disk I/O: PNG files + atomic manifest. One instance per output dir.</summary>
public sealed class SnapshotWriter(string outputDir)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private string ManifestPath => Path.Combine(outputDir, "manifest.json");

    public string SavePng(string scene, string theme, byte[] png)
    {
        Directory.CreateDirectory(outputDir);
        var name = $"{scene}.{theme}.png";
        File.WriteAllBytes(Path.Combine(outputDir, name), png);
        return name;
    }

    public Manifest? ReadExisting()
        => File.Exists(ManifestPath)
            ? JsonSerializer.Deserialize<Manifest>(File.ReadAllText(ManifestPath), JsonOptions)
            : null;

    public void WriteManifest(Manifest manifest)
    {
        Directory.CreateDirectory(outputDir);
        var temp = Path.Combine(outputDir, $"manifest.json.{manifest.RunId}.tmp");
        File.WriteAllText(temp, JsonSerializer.Serialize(manifest, JsonOptions));
        File.Move(temp, ManifestPath, overwrite: true); // atomic replace
    }
}
