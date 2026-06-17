using Glimpse.Avalonia;
using Glimpse.ScratchConsole.Scenes;
using Xunit;

namespace Glimpse.ScratchConsole.Tests;

[Collection("real-session")]
public class SceneSmokeTests(RealSessionFixture fixture)
{
    [Fact]
    public void SampleScenes_ShouldHaveUniqueNames()
    {
        var names = SampleScenes.All.Select(s => s.Name).ToList();

        Assert.Equal(names.Count, names.Distinct().Count());
    }

    [Fact]
    public async Task EverySampleScene_ShouldRenderWithoutThrowingOrBlank()
    {
        foreach (var scene in SampleScenes.All)
        {
            var result = await fixture.Session.RenderSceneAsync(scene, new RenderOptions());

            Assert.NotEmpty(result.Png);
            Assert.DoesNotContain(result.Warnings, w => w.StartsWith("single-color-frame"));
        }
    }
}
