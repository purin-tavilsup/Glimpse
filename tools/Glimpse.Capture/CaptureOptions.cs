using Glimpse.Abstractions;

namespace Glimpse.Capture;

public sealed record CaptureOptions(
    string? Source,
    string? Renderer,
    string Name,
    string? OutDir,
    int Width,
    int Height,
    SnapshotTheme Theme,
    int? WindowId,
    string? Window,
    string? Title,
    bool Prune,
    bool NoManifest,
    bool ListWindows)
{
    public static CaptureOptions Parse(string[] args)
    {
        string? source = null, renderer = null, name = null, outDir = null, window = null, title = null;
        int width = 1280, height = 800, windowId = 0;
        var theme = SnapshotTheme.Light;
        var prune = false;
        var noManifest = false;
        var listWindows = false;
        var hasWindowId = false;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "--renderer": renderer = Next(args, ref i); break;
                case "--name": name = Next(args, ref i); break;
                case "--out": outDir = Next(args, ref i); break;
                case "--theme": theme = ThemeFor(Next(args, ref i)); break;
                case "--window-id":
                    var windowIdRaw = Next(args, ref i);
                    if (!int.TryParse(windowIdRaw, out windowId))
                        throw new ArgumentException("--window-id requires an integer.");
                    hasWindowId = true;
                    break;
                case "--window": window = Next(args, ref i); break;
                case "--title": title = Next(args, ref i); break;
                case "--prune": prune = true; break;
                case "--no-manifest": noManifest = true; break;
                case "--list-windows": listWindows = true; break;
                case "--size": (width, height) = SizeFor(Next(args, ref i)); break;
                default:
                    if (arg.StartsWith('-'))
                        throw new ArgumentException($"Unexpected argument '{arg}'.");
                    source = arg;
                    break;
            }
        }

        var resolvedName = name
            ?? (source is not null ? Path.GetFileNameWithoutExtension(source)
                : window is not null ? Slug(window)
                : renderer ?? "snapshot");

        return new CaptureOptions(source, renderer, resolvedName, outDir, width, height, theme,
            hasWindowId ? windowId : null, window, title, prune, noManifest, listWindows);
    }

    private static string Slug(string value)
    {
        var chars = value.ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '-')
            .ToArray();
        return new string(chars).Trim('-');
    }

    private static string Next(string[] args, ref int i)
    {
        if (i + 1 >= args.Length)
            throw new ArgumentException($"Missing value for '{args[i]}'.");
        return args[++i];
    }

    private static SnapshotTheme ThemeFor(string theme) => theme switch
    {
        "light" => SnapshotTheme.Light,
        "dark" => SnapshotTheme.Dark,
        _ => throw new ArgumentException($"Unknown --theme '{theme}' (expected light|dark)."),
    };

    private static (int Width, int Height) SizeFor(string size)
    {
        var parts = size.Split('x');
        if (parts.Length != 2 || !int.TryParse(parts[0], out var w) || !int.TryParse(parts[1], out var h))
            throw new ArgumentException($"Invalid --size '{size}' (expected WIDTHxHEIGHT, e.g. 1280x800).");
        if (w <= 0 || h <= 0)
            throw new ArgumentException("--size dimensions must be positive (e.g. 1280x800).");
        return (w, h);
    }
}
