using Avalonia.Controls;
using Avalonia.Media;
using Glimpse.Abstractions;
using Glimpse.Avalonia;
using Glimpse.Avalonia.Abstractions;
using Glimpse.Core;
using Glimpse.ScratchConsole;
using Xunit;
using RenderOptions = Glimpse.Avalonia.RenderOptions;

namespace Glimpse.ScratchConsole.Tests;

public class SnapshotRunnerTests
{
    private sealed class StubScene(string name) : IScene
    {
        public string Name => name;
        public Control Build() => new Border { Background = Brushes.White };
    }

    private sealed class FakeRenderer(Func<IScene, RenderResult> render) : ISnapshotRenderer
    {
        public RenderResult Render(Func<Control> build, RenderOptions options) => throw new NotSupportedException();
        public Task<RenderResult> RenderSceneAsync(IScene scene, RenderOptions options) => Task.FromResult(render(scene));
    }

    private static SnapshotRunner NewRunner(Func<IScene, RenderResult> render, out string dir)
    {
        dir = Directory.CreateTempSubdirectory("glimpse-run-").FullName;
        return new SnapshotRunner(new FakeRenderer(render), new SnapshotWriter(dir));
    }

    [Fact]
    public async Task RunAsync_AllScenesSucceed_ShouldReturnZeroAndWriteManifest()
    {
        var runner = NewRunner(_ => new RenderResult([0x89, 1], 10, 10, []), out var dir);

        var exit = await runner.RunAsync([new StubScene("Home")], [SnapshotTheme.Light], "run-1", DateTimeOffset.UnixEpoch);

        Assert.Equal(0, exit);
        Assert.True(File.Exists(Path.Combine(dir, "Home.light.png")));
        var manifest = new SnapshotWriter(dir).ReadExisting()!;
        Assert.Equal("ok", manifest.Scenes.Single().Status);
        Assert.Empty(manifest.Failures);
    }

    [Fact]
    public async Task RunAsync_WhenASceneThrows_ShouldMarkEntryFailedAndRecordFailure()
    {
        var runner = NewRunner(
            scene => scene.Name == "Bad" ? throw new InvalidOperationException("boom")
                                          : new RenderResult([0x89], 10, 10, []),
            out var dir);

        var exit = await runner.RunAsync(
            [new StubScene("Good"), new StubScene("Bad")], [SnapshotTheme.Light], "run-1", DateTimeOffset.UnixEpoch);

        Assert.Equal(1, exit);
        var manifest = new SnapshotWriter(dir).ReadExisting()!;
        // The safety property: a failed scene must NOT masquerade as ok in the manifest.
        Assert.Equal("failed", manifest.Scenes.Single(e => e.Scene == "Bad").Status);
        Assert.Equal("ok", manifest.Scenes.Single(e => e.Scene == "Good").Status);
        Assert.Contains(manifest.Failures, f => f.Scene == "Bad");
    }

    [Fact]
    public async Task RunAsync_WhenAPreviouslyOkSceneFails_ShouldFlipEntryToFailed()
    {
        var dir = Directory.CreateTempSubdirectory("glimpse-run-").FullName;
        var writer = new SnapshotWriter(dir);

        var ok = new SnapshotRunner(new FakeRenderer(_ => new RenderResult([0x89], 10, 10, [])), writer);
        await ok.RunAsync([new StubScene("Flaky")], [SnapshotTheme.Light], "run-1", DateTimeOffset.UnixEpoch);

        var fails = new SnapshotRunner(new FakeRenderer(_ => throw new InvalidOperationException("boom")), writer);
        await fails.RunAsync([new StubScene("Flaky")], [SnapshotTheme.Light], "run-2", DateTimeOffset.UnixEpoch);

        var manifest = writer.ReadExisting()!;
        Assert.Equal("failed", manifest.Scenes.Single(e => e.Scene == "Flaky").Status); // not stale "ok"
    }

    [Fact]
    public async Task RunAsync_WithDuplicateSceneNames_ShouldReturnTwo()
    {
        var runner = NewRunner(_ => new RenderResult([0x89], 10, 10, []), out _);

        var exit = await runner.RunAsync(
            [new StubScene("Dup"), new StubScene("Dup")], [SnapshotTheme.Light], "run-1", DateTimeOffset.UnixEpoch);

        Assert.Equal(2, exit);
    }

    [Fact]
    public async Task RunAsync_WithNoScenes_ShouldReturnTwo()
    {
        var runner = NewRunner(_ => new RenderResult([0x89], 10, 10, []), out _);

        var exit = await runner.RunAsync([], [SnapshotTheme.Light], "run-1", DateTimeOffset.UnixEpoch);

        Assert.Equal(2, exit);
    }
}
