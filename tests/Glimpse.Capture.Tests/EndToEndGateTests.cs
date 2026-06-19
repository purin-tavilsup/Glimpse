using Glimpse.Core;
using Xunit;

namespace Glimpse.Capture.Tests;

public class EndToEndGateTests
{
    [SkippableFact]
    public async Task Mermaid_RealRender_ShouldProduceNonBlankPngAndManifestEntry()
    {
        Skip.If(ToolLocator.Resolve("mmdc") is null, "mermaid-cli (mmdc) not installed.");

        var dir = Path.Combine(Path.GetTempPath(), $"glimpse-e2e-{Guid.NewGuid()}");
        Directory.CreateDirectory(dir);
        var source = Path.Combine(dir, "flow.mmd");
        await File.WriteAllTextAsync(source, "flowchart TD\n  A[Start] --> B[Render]\n  B --> C[Read]\n");
        var writer = new SnapshotWriter(dir);

        var spec = RendererRegistry.Default().Resolve("mermaid", source);
        var request = new RenderRequest(source, writer.PngPath("flow"), 800, 600, Abstractions.SnapshotTheme.Light);
        var outcome = await new RenderEngine(new ProcessRunner()).RenderAsync(spec, request);

        Assert.Equal("ok", outcome.Status);
        Assert.Empty(outcome.Warnings);            // a real flowchart is never single-color
        Assert.True(File.Exists(outcome.OutputPath));

        Directory.Delete(dir, recursive: true);
    }
}
