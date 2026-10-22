namespace Glimpse.Drive;

/// <summary>
/// What <c>shot</c> was asked for: the file, and optionally the window to bring to the front first. The file is the
/// first argument, as documented, or the last when only the last ends in <c>.png</c>, so <c>shot app Title out.png</c>
/// works too. Only those two positions are looked at, because a window title can itself end in <c>.png</c>.
/// </summary>
public sealed record ShotArguments(string File, string? Proc, string? TitlePart)
{
    public static ShotArguments Parse(IReadOnlyList<string> args)
    {
        if (args.Count < 3)
        {
            return new ShotArguments(args[0], null, null);
        }
        return IsPng(args[^1]) && !IsPng(args[0])
            ? new ShotArguments(args[^1], args[0], args[1])
            : new ShotArguments(args[0], args[1], args[2]);
    }

    private static bool IsPng(string arg) => arg.EndsWith(".png", StringComparison.OrdinalIgnoreCase);
}
