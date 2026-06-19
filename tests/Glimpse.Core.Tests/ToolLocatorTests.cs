using Glimpse.Core;
using Xunit;

namespace Glimpse.Core.Tests;

public class ToolLocatorTests
{
    [Fact]
    public void Resolve_WithRealShellTool_ShouldReturnAbsolutePath()
    {
        // 'ls' exists on every macOS/Linux dev box and is on PATH.
        var path = ToolLocator.Resolve("ls");

        Assert.NotNull(path);
        Assert.True(Path.IsPathRooted(path));
    }

    [Fact]
    public void Resolve_WithNonexistentTool_ShouldReturnNull()
    {
        Assert.Null(ToolLocator.Resolve("definitely-not-a-real-tool-xyz"));
    }

    [Theory]
    [InlineData("mmdc", "mermaid-cli")]
    [InlineData("dot", "graphviz")]
    [InlineData("d2", "d2")]
    public void InstallHint_ForKnownTool_ShouldMentionThePackage(string tool, string fragment)
    {
        var hint = ToolLocator.InstallHint(tool);

        Assert.Contains(fragment, hint);
    }
}
