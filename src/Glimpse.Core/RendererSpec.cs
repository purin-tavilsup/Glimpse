namespace Glimpse.Core;

/// <summary>
/// A renderer is a command that writes a PNG to a path. <see cref="Args"/> are passed to
/// <see cref="Tool"/> with placeholders substituted: {source} {out} {width} {height} {theme} {windowid},
/// and {sourceurl} for tools that take a URL (an absolute file:// URL of the source).
/// </summary>
public sealed record RendererSpec(
    string Name,
    string Tool,
    IReadOnlyList<string> Args,
    IReadOnlyList<string> SourceExtensions);

public static class BuiltInRenderers
{
    public static IReadOnlyList<RendererSpec> All { get; } =
    [
        new("mermaid", "mmdc",
            ["-i", "{source}", "-o", "{out}", "-t", "{theme}", "-w", "{width}"],
            [".mmd", ".mermaid"]),

        new("graphviz", "dot",
            ["-Tpng", "{source}", "-o", "{out}"],
            [".dot", ".gv"]),

        new("d2", "d2",
            ["{source}", "{out}"],
            [".d2"]),

        new("web", "chrome",
            ["--headless", "--disable-gpu", "--screenshot={out}", "--window-size={width},{height}", "{sourceurl}"],
            [".html", ".htm"]),

        new("app", "screencapture",
            ["-x", "-o", "-l{windowid}", "{out}"],
            []),
    ];
}
