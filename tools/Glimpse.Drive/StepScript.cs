using System.Text;
using System.Text.RegularExpressions;

namespace Glimpse.Drive;

/// <summary>
/// Turns the lines of a <c>.steps</c> flow into driver commands: one command per line, <c>#</c> comments,
/// <c>$name</c> filled from a <c>name=value</c> argument of <c>run</c>.
/// </summary>
public static class StepScript
{
    /// <summary>Splits <c>run</c>'s arguments into flow files and <c>name=value</c> variables.</summary>
    public static (string[] Files, Dictionary<string, string> Variables) SplitRunArgs(IEnumerable<string> args)
    {
        var list = args.ToList();
        var files = list.Where(a => !a.Contains('=')).ToArray();
        var variables = list.Where(a => a.Contains('='))
                            .ToDictionary(a => a[..a.IndexOf('=')], a => a[(a.IndexOf('=') + 1)..]);
        return (files, variables);
    }

    /// <summary>Returns the command on a line, or null for a blank or comment line.</summary>
    /// <exception cref="InvalidOperationException">The line uses a <c>$name</c> that has no value.</exception>
    public static string[]? ParseLine(string raw, IReadOnlyDictionary<string, string> variables)
    {
        var line = raw.Trim();
        if (line.Length == 0 || line.StartsWith('#'))
        {
            return null;
        }

        return Tokenize(Substitute(line, variables));
    }

    // An unknown $name is a hard error, never a silent half-run.
    private static string Substitute(string line, IReadOnlyDictionary<string, string> variables) =>
        Regex.Replace(line, @"\$(\w+)", m =>
            variables.TryGetValue(m.Groups[1].Value, out var value)
                ? value
                : throw new InvalidOperationException($"no value for ${m.Groups[1].Value} (pass {m.Groups[1].Value}=... to run)"));

    // Split on whitespace but keep "quoted runs" as one token, so `keys App "My App" x` stays four args.
    private static string[] Tokenize(string line)
    {
        var tokens = new List<string>();
        var token = new StringBuilder();
        var quoted = false;
        foreach (var c in line)
        {
            if (c == '"')
            {
                quoted = !quoted;
            }
            else if (char.IsWhiteSpace(c) && !quoted)
            {
                if (token.Length > 0)
                {
                    tokens.Add(token.ToString());
                    token.Clear();
                }
            }
            else
            {
                token.Append(c);
            }
        }

        if (token.Length > 0)
        {
            tokens.Add(token.ToString());
        }

        return [.. tokens];
    }
}
