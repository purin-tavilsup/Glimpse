using Avalonia.Controls;
using Avalonia.Media;
using Glimpse.Avalonia;
using Glimpse.Avalonia.Abstractions;
using Xunit;

namespace Glimpse.Avalonia.Tests;

[Collection("snapshot")]
public class SceneRenderTests(SnapshotSessionFixture fixture)
{
    private sealed class AsyncScene : TextBlock, IScene
    {
        public new string Name => "async";

        public Control Build() => this;

        public async Task ReadyAsync()
        {
            await Task.Delay(10);
            Text = "loaded"; // only present if the engine awaited ReadyAsync before capture
            Foreground = Brushes.Black;
        }
    }

    [Fact]
    public async Task RenderSceneAsync_WithAsyncReadyScene_ShouldCaptureLoadedContent()
    {
        var scene = new AsyncScene();

        var result = await fixture.Session.RenderSceneAsync(scene, new RenderOptions(Width: 200, Height: 80));

        Assert.NotEmpty(result.Png);
        Assert.DoesNotContain(result.Warnings, w => w.StartsWith("single-color-frame"));
    }
}
