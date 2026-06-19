using System.Collections.Generic;
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

    [Fact]
    public void PngPath_ShouldReturnStableNameInsideOutputDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var writer = new SnapshotWriter(dir);

        var path = writer.PngPath("architecture");

        Assert.Equal(Path.Combine(dir, "architecture.png"), path);
        Assert.True(Directory.Exists(dir));
        Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public void Prune_ShouldDeletePngsNotInKeepSet()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, "keep.png"), [1]);
        File.WriteAllBytes(Path.Combine(dir, "stale.png"), [1]);
        var writer = new SnapshotWriter(dir);

        writer.Prune(new HashSet<string> { "keep" });

        Assert.True(File.Exists(Path.Combine(dir, "keep.png")));
        Assert.False(File.Exists(Path.Combine(dir, "stale.png")));
        Directory.Delete(dir, recursive: true);
    }
}
