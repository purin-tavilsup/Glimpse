namespace Glimpse.Core;

public sealed class GlimpseRenderToolException(string tool, string hint)
    : Exception($"{tool}: {hint}")
{
    public string Tool { get; } = tool;
    public string Hint { get; } = hint;
}

public sealed record RenderOutcome(
    string Status,
    int ExitCode,
    string OutputPath,
    int Width,
    int Height,
    IReadOnlyList<string> Warnings);

/// <summary>Resolve tool -> run command -> analyse PNG -> outcome. The whole pipeline, minus persistence.</summary>
public sealed class RenderEngine(IProcessRunner runner)
{
    public async Task<RenderOutcome> RenderAsync(RendererSpec spec, RenderRequest request)
    {
        var executable = ToolLocator.Resolve(spec.Tool)
            ?? throw new GlimpseRenderToolException(spec.Tool, ToolLocator.InstallHint(spec.Tool));

        var command = RenderCommandBuilder.Build(spec, request, executable);
        var result = await runner.RunAsync(command.Executable, command.Args);

        if (result.ExitCode != 0 || !File.Exists(request.OutputPath))
            return new RenderOutcome("failed", 2, request.OutputPath, 0, 0,
                [$"render-failed:{result.StdErr.Trim()}"]);

        var inspection = PngAnalysis.Inspect(request.OutputPath);
        var exitCode = inspection.Warnings.Count > 0 ? 1 : 0;
        return new RenderOutcome("ok", exitCode, request.OutputPath,
            inspection.Width, inspection.Height, inspection.Warnings);
    }
}
