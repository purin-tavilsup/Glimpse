using Glimpse.Abstractions;
using Glimpse.Core;
using Glimpse.ScratchConsole.Scenes;
using Xunit;

namespace Glimpse.ScratchConsole.Tests;

[Collection("real-session")]
public class EndToEndGateTests(RealSessionFixture fixture)
{
    [Fact]
    public async Task FullPipeline_RealSessionToManifest_ShouldWriteAllScenesOk()
    {
        var dir = Directory.CreateTempSubdirectory("glimpse-e2e-").FullName;
        var runner = new SnapshotRunner(fixture.Session, new SnapshotWriter(dir));

        var exit = await runner.RunAsync(
            SampleScenes.All, [SnapshotTheme.Light, SnapshotTheme.Dark], "gate-run", DateTimeOffset.UnixEpoch);

        Assert.Equal(0, exit);
        var manifest = new SnapshotWriter(dir).ReadExisting()!;
        Assert.Equal(SampleScenes.All.Count * 2, manifest.Scenes.Count);
        Assert.All(manifest.Scenes, e =>
        {
            Assert.Equal("ok", e.Status);
            Assert.True(File.Exists(Path.Combine(dir, e.Path)));
            Assert.True(new FileInfo(Path.Combine(dir, e.Path)).Length > 0);
        });
        Assert.Empty(manifest.Failures);
    }
}
