namespace Glimpse.Drive;

/// <summary>
/// What <c>shot</c> was asked for: the file, and optionally the window to bring to the front first. The file is the
/// <c>.png</c> argument wherever it stands, so <c>shot app Title out.png</c> works as well as the documented
/// <c>shot out.png app Title</c>; with no <c>.png</c> argument the first one is the file.
/// </summary>
public sealed record ShotArguments(string File, string? Proc, string? TitlePart)
{
    public static ShotArguments Parse(IReadOnlyList<string> args)
    {
        var fileIndex = Math.Max(0, args.ToList().FindIndex(a => a.EndsWith(".png", StringComparison.OrdinalIgnoreCase)));
        var window = args.Where((_, i) => i != fileIndex).ToList();
        return window.Count >= 2
            ? new ShotArguments(args[fileIndex], window[0], window[1])
            : new ShotArguments(args[fileIndex], null, null);
    }
}
