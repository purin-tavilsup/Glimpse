using Glimpse.Core;
using Xunit;

namespace Glimpse.Core.Tests;

public class RendererRegistryTests
{
    private readonly RendererRegistry registry = RendererRegistry.Default();

    [Theory]
    [InlineData("diagram.mmd", "mermaid")]
    [InlineData("graph.dot", "graphviz")]
    [InlineData("graph.gv", "graphviz")]
    [InlineData("layout.d2", "d2")]
    [InlineData("page.html", "web")]
    [InlineData("page.HTM", "web")]
    public void Resolve_ByExtension_ShouldPickRenderer(string sourcePath, string expectedName)
    {
        var spec = registry.Resolve(null, sourcePath);

        Assert.Equal(expectedName, spec.Name);
    }

    [Fact]
    public void Resolve_WithExplicitName_ShouldOverrideExtension()
    {
        var spec = registry.Resolve("graphviz", "diagram.mmd");

        Assert.Equal("graphviz", spec.Name);
    }

    [Fact]
    public void Resolve_WithUnknownExtensionAndNoName_ShouldThrow()
    {
        Assert.Throws<ArgumentException>(() => registry.Resolve(null, "notes.txt"));
    }

    [Fact]
    public void Resolve_WithUnknownName_ShouldThrow()
    {
        Assert.Throws<ArgumentException>(() => registry.Resolve("plantuml", "x.puml"));
    }

    [Fact]
    public void All_ShouldContainTheFiveBuiltIns()
    {
        var names = BuiltInRenderers.All.Select(r => r.Name).ToList();

        Assert.Equal(new[] { "mermaid", "graphviz", "d2", "web", "app" }, names);
    }
}
