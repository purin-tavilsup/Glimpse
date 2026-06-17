using Glimpse.Core;
using Xunit;

namespace Glimpse.Core.Tests;

public class SnapshotWriterTests
{
    [Fact]
    public void SavePng_ShouldWriteSceneThemeFileAndReturnRelativeName()
    {
        var dir = Directory.CreateTempSubdirectory("glimpse-out-").FullName;
        var writer = new SnapshotWriter(dir);

        var name = writer.SavePng("LoginWindow", "dark", [1, 2, 3]);

        Assert.Equal("LoginWindow.dark.png", name);
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(Path.Combine(dir, name)));
    }

    [Fact]
    public void WriteManifest_ThenReadExisting_ShouldRoundTrip()
    {
        var dir = Directory.CreateTempSubdirectory("glimpse-out-").FullName;
        var writer = new SnapshotWriter(dir);
        var manifest = ManifestMerge.Merge(null,
            [new SceneEntry("Home", "light", "Home.light.png", 10, 10, 1.0, "ok", DateTimeOffset.UnixEpoch, [])],
            [], "run-1", DateTimeOffset.UnixEpoch);

        writer.WriteManifest(manifest);
        var read = writer.ReadExisting();

        Assert.NotNull(read);
        Assert.Equal("run-1", read!.RunId);
        Assert.Single(read.Scenes);
        Assert.False(File.Exists(Path.Combine(dir, "manifest.json.run-1.tmp"))); // temp cleaned up
    }
}
