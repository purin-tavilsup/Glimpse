using Avalonia.Controls;
using Glimpse.Avalonia.Abstractions;
using Xunit;

namespace Glimpse.Avalonia.Tests;

public class SceneContractTests
{
    private sealed class MinimalScene : IScene
    {
        public string Name => "minimal";
        public Control Build() => new TextBlock { Text = "hi" };
    }

    [Fact]
    public void ReadyAsync_WhenNotOverridden_ShouldBeAlreadyCompleted()
    {
        IScene scene = new MinimalScene();

        Assert.True(scene.ReadyAsync().IsCompletedSuccessfully);
    }
}
