using System.Reflection;

namespace Glimpse.Capture;

/// <summary>
/// The outcome of reading the command line: options to run with, or the exit code of a run that ended there
/// (help, version, or arguments that could not be parsed).
/// </summary>
public sealed record CliStart(CaptureOptions? Options, int ExitCode);

public static class Cli
{
    public const string Usage = """
        Usage:
          glimpse <source> [options]                render a diagram or page to a PNG
          glimpse --renderer app --window <name>    capture a live app window
          glimpse --list-windows                    list the windows that can be captured
          glimpse --check-icons <file.d2>           check that a D2 file's icon URLs resolve

        Options:
          --renderer <name>     mermaid | graphviz | d2 | web | app (default: from the file extension)
          --name <name>         snapshot name (default: from the source file or window)
          --out <dir>           output folder (default: the project's glimpse folder)
          --size <W>x<H>        render size (default: 1280x800)
          --theme light|dark    theme (default: light)
          --window <name>       app window owner to capture; --title narrows it by title
          --window-id <id>      exact window id from --list-windows
          --no-manifest         don't record the snapshot in manifest.json
          --prune               remove other snapshots from the output folder
          -h, --help            show this help
          --version             show the version

        """;

    /// <summary>
    /// Parses <paramref name="args"/>. Help and version are written to <paramref name="output"/>; an argument
    /// error is written to <paramref name="error"/>, followed by the usage, and ends the run with exit code 2.
    /// </summary>
    public static CliStart Start(string[] args, TextWriter output, TextWriter error)
    {
        CaptureOptions options;
        try
        {
            options = CaptureOptions.Parse(args);
        }
        catch (ArgumentException ex)
        {
            error.WriteLine(ex.Message);
            error.WriteLine();
            error.Write(Usage);
            return new CliStart(null, 2);
        }

        if (options.Help)
        {
            output.Write(Usage);
            return new CliStart(null, 0);
        }

        if (options.Version)
        {
            output.WriteLine(Version());
            return new CliStart(null, 0);
        }

        return new CliStart(options, 0);
    }

    // The SDK appends "+<commit>" to the informational version; the package version is the part before it.
    private static string Version() =>
        typeof(Cli).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            .Split('+')[0]
        ?? "unknown";
}
