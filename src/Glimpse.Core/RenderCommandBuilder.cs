using Glimpse.Abstractions;

namespace Glimpse.Core;

public sealed record RenderRequest(
    string Source,
    string OutputPath,
    int Width,
    int Height,
    SnapshotTheme Theme,
    int? WindowId = null);

public sealed record RenderCommand(string Executable, IReadOnlyList<string> Args);

/// <summary>Pure: fills a spec's arg placeholders from a request. No I/O.</summary>
public static class RenderCommandBuilder
{
    public static RenderCommand Build(RendererSpec spec, RenderRequest request, string executable)
    {
        var args = spec.Args.Select(arg => Substitute(arg, request)).ToList();
        return new RenderCommand(executable, args);
    }

    private static string Substitute(string arg, RenderRequest request)
    {
        if (arg.Contains("{windowid}") && request.WindowId is null)
            throw new ArgumentException("This renderer needs a window id, but none was supplied.");

        return arg
            .Replace("{source}", request.Source)
            .Replace("{out}", request.OutputPath)
            .Replace("{width}", request.Width.ToString())
            .Replace("{height}", request.Height.ToString())
            .Replace("{theme}", request.Theme == SnapshotTheme.Dark ? "dark" : "default")
            .Replace("{windowid}", request.WindowId?.ToString() ?? "");
    }
}
