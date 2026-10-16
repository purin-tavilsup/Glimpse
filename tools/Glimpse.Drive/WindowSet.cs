namespace Glimpse.Drive;

/// <summary>
/// The <c>expect</c> and <c>hold</c> assertions. They check the whole set of visible windows, not one element,
/// because a window that should have stayed hidden is usually the bug being looked for.
/// </summary>
public static class WindowSet
{
    public const string None = "none";

    /// <summary>Passes as soon as the visible windows are exactly those holding <paramref name="anchors"/>.</summary>
    public static int Expect(Func<IReadOnlyList<string>, IReadOnlyList<string>> labels, string anchors, TimeSpan timeout, TimeSpan poll)
    {
        var wanted = Wanted(anchors);
        var deadline = DateTime.UtcNow + timeout;
        string actual;
        while (!Is(labels(wanted), wanted, out actual) && DateTime.UtcNow < deadline)
        {
            Thread.Sleep(poll);
        }
        return Report(anchors, actual, Is(labels(wanted), wanted, out actual));
    }

    /// <summary>Like <see cref="Expect"/>, but the set must match for the whole duration — for "nothing reappears" checks.</summary>
    public static int Hold(Func<IReadOnlyList<string>, IReadOnlyList<string>> labels, string anchors, TimeSpan duration, TimeSpan poll)
    {
        var wanted = Wanted(anchors);
        var deadline = DateTime.UtcNow + duration;
        string actual;
        do
        {
            if (!Is(labels(wanted), wanted, out actual))
            {
                return Report(anchors, actual, false);
            }
            Thread.Sleep(poll);
        } while (DateTime.UtcNow < deadline);
        return Report(anchors, actual, true);
    }

    private static string[] Wanted(string anchors) => anchors == None ? [] : anchors.Split(',');

    private static bool Is(IReadOnlyList<string> labels, string[] wanted, out string actual)
    {
        actual = labels.Count == 0 ? None : string.Join(",", labels.Order());
        return actual == (wanted.Length == 0 ? None : string.Join(",", wanted.Order()));
    }

    private static int Report(string anchors, string actual, bool pass)
    {
        Console.WriteLine(pass ? $"PASS windows = {actual}" : $"FAIL windows: expected {anchors}, got {actual}");
        return pass ? 0 : 1;
    }
}
