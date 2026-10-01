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

/// <summary>The shared tail of every render path: a written file becomes an analysed outcome.
/// Used by both <see cref="RenderEngine"/> (external tools) and <see cref="IAppCapturer"/>
/// (in-process capture) so the two can never disagree about status or exit codes.</summary>
public static class RenderOutcomes
{
    public static RenderOutcome From(string outputPath, bool succeeded, string standardError)
    {
        if (!succeeded || !File.Exists(outputPath))
            return new RenderOutcome("failed", 2, outputPath, 0, 0,
                [$"render-failed:{standardError.Trim()}"]);

        var inspection = PngAnalysis.Inspect(outputPath);
        var exitCode = inspection.Warnings.Count > 0 ? 1 : 0;
        return new RenderOutcome("ok", exitCode, outputPath,
            inspection.Width, inspection.Height, inspection.Warnings);
    }
}

/// <summary>Resolve tool -> run command -> analyse PNG -> outcome. The whole pipeline, minus persistence.</summary>
public sealed class RenderEngine(IProcessRunner runner, TimeSpan? outputWait = null)
{
    private readonly TimeSpan _outputWait = outputWait ?? TimeSpan.FromSeconds(5);

    public async Task<RenderOutcome> RenderAsync(RendererSpec spec, RenderRequest request)
    {
        var executable = ToolLocator.Resolve(spec.Tool)
            ?? throw new GlimpseRenderToolException(spec.Tool, ToolLocator.InstallHint(spec.Tool));

        var command = RenderCommandBuilder.Build(spec, request, executable);
        var result = await runner.RunAsync(command.Executable, command.Args);
        if (result.ExitCode == 0)
            await WaitForOutputAsync(request.OutputPath);

        return RenderOutcomes.From(request.OutputPath, result.ExitCode == 0, result.StdErr);
    }

    // Headless Chrome on Windows can exit 0 while a child process is still writing the PNG.
    // Polls until the file exists and its size holds steady across two reads, or the wait runs
    // out; a tool that never writes still fails, just _outputWait later.
    private async Task WaitForOutputAsync(string path)
    {
        var poll = TimeSpan.FromMilliseconds(100);
        var deadline = DateTime.UtcNow + _outputWait;
        long lastLength = -1;

        while (DateTime.UtcNow < deadline)
        {
            var length = File.Exists(path) ? new FileInfo(path).Length : -1;
            if (length > 0 && length == lastLength)
                return;

            lastLength = length;
            await Task.Delay(poll);
        }
    }
}
