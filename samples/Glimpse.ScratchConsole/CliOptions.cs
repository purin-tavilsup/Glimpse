using Glimpse.Abstractions;

namespace Glimpse.ScratchConsole;

public sealed record CliOptions(
    IReadOnlyList<string> Scenes,
    IReadOnlyList<SnapshotTheme> Themes,
    string? OutDir)
{
    public static CliOptions Parse(string[] args)
    {
        var scene = "all";
        var theme = "both";
        string? outDir = null;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--scene": scene = NextValue(args, ref i); break;
                case "--theme": theme = NextValue(args, ref i); break;
                case "--out": outDir = NextValue(args, ref i); break;
                default: throw new ArgumentException($"Unexpected argument '{args[i]}'.");
            }
        }

        return new CliOptions([scene], ThemesFor(theme), outDir);
    }

    private static string NextValue(string[] args, ref int i)
    {
        if (i + 1 >= args.Length)
            throw new ArgumentException($"Missing value for '{args[i]}'.");
        return args[++i];
    }

    private static IReadOnlyList<SnapshotTheme> ThemesFor(string theme) => theme switch
    {
        "light" => [SnapshotTheme.Light],
        "dark" => [SnapshotTheme.Dark],
        "both" => [SnapshotTheme.Light, SnapshotTheme.Dark],
        _ => throw new ArgumentException($"Unknown --theme '{theme}' (expected light|dark|both)."),
    };
}
