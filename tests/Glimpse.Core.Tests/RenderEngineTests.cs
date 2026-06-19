using Glimpse.Abstractions;
using Glimpse.Core;
using SkiaSharp;
using Xunit;

namespace Glimpse.Core.Tests;

public class RenderEngineTests
{
    // Uses 'ls' as a real, always-present tool so ToolLocator resolves; the fake runner
    // simulates the tool's effect (writing a PNG or not) without actually invoking it.
    private static RendererSpec LsSpec() => new("fake", "ls", ["{out}"], [".x"]);

    private sealed class FakeRunner(int exitCode, Action onRun) : IProcessRunner
    {
        public Task<ProcessResult> RunAsync(string executable, IReadOnlyList<string> args)
        {
            onRun();
            return Task.FromResult(new ProcessResult(exitCode, exitCode == 0 ? "" : "boom"));
        }
    }

    private static void WriteSolidPng(string path, SKColor color)
    {
        using var bitmap = new SKBitmap(32, 32);
        using (var canvas = new SKCanvas(bitmap))
            canvas.Clear(color);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(path, data.ToArray());
    }

    private static void WriteTwoColorPng(string path)
    {
        using var bitmap = new SKBitmap(32, 32);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.White);
            using var paint = new SKPaint { Color = SKColors.Black };
            canvas.DrawRect(0, 0, 16, 32, paint);
        }
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(path, data.ToArray());
    }

    private static RenderRequest RequestTo(string outPath) =>
        new("in.x", outPath, 100, 100, SnapshotTheme.Light);

    [Fact]
    public async Task RenderAsync_WhenCommandSucceedsWithGoodPng_ShouldReturnOkExitZero()
    {
        var outPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.png");
        var engine = new RenderEngine(new FakeRunner(0, () => WriteTwoColorPng(outPath)));

        var outcome = await engine.RenderAsync(LsSpec(), RequestTo(outPath));

        Assert.Equal("ok", outcome.Status);
        Assert.Equal(0, outcome.ExitCode);
        Assert.Empty(outcome.Warnings);
        File.Delete(outPath);
    }

    [Fact]
    public async Task RenderAsync_WhenPngIsSingleColor_ShouldReturnExitOneWithWarning()
    {
        var outPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.png");
        var engine = new RenderEngine(new FakeRunner(0, () => WriteSolidPng(outPath, SKColors.White)));

        var outcome = await engine.RenderAsync(LsSpec(), RequestTo(outPath));

        Assert.Equal("ok", outcome.Status);
        Assert.Equal(1, outcome.ExitCode);
        Assert.NotEmpty(outcome.Warnings);
        File.Delete(outPath);
    }

    [Fact]
    public async Task RenderAsync_WhenCommandFails_ShouldReturnFailedExitTwo()
    {
        var outPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.png");
        var engine = new RenderEngine(new FakeRunner(1, () => { /* writes nothing */ }));

        var outcome = await engine.RenderAsync(LsSpec(), RequestTo(outPath));

        Assert.Equal("failed", outcome.Status);
        Assert.Equal(2, outcome.ExitCode);
    }

    [Fact]
    public async Task RenderAsync_WhenToolMissing_ShouldThrowWithHint()
    {
        var spec = new RendererSpec("mermaid", "definitely-not-a-real-tool-xyz", ["{out}"], [".x"]);
        var engine = new RenderEngine(new FakeRunner(0, () => { }));

        var ex = await Assert.ThrowsAsync<GlimpseRenderToolException>(
            () => engine.RenderAsync(spec, RequestTo("/tmp/x.png")));
        Assert.Equal("definitely-not-a-real-tool-xyz", ex.Tool);
    }
}
