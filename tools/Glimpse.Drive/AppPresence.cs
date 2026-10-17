using System.Diagnostics;

namespace Glimpse.Drive;

/// <summary>
/// Ends a wait early when the app it waits on has no process: a launch that never happened would otherwise burn the
/// whole timeout and then report only "not found". The grace covers an app that is still starting or restarting.
/// </summary>
public sealed class AppPresence(string proc, Func<string, bool> isRunning, TimeSpan grace)
{
    public static readonly TimeSpan DefaultGrace = TimeSpan.FromSeconds(10);

    // Short enough that a missing app is noticed soon after the grace ends.
    private static readonly TimeSpan Slice = TimeSpan.FromSeconds(2);

    private DateTime? goneSince;

    public static AppPresence For(string proc) => new(proc, HasProcess, DefaultGrace);

    /// <summary>Throws once <c>proc</c> has had no process for the whole grace; a process seen again restarts it.</summary>
    public void ThrowIfGone()
    {
        if (isRunning(proc))
        {
            goneSince = null;
            return;
        }
        goneSince ??= DateTime.UtcNow;
        if (DateTime.UtcNow - goneSince >= grace)
        {
            throw new InvalidOperationException($"{proc} is not running (no process for {grace.TotalSeconds:0} s); did it launch?");
        }
    }

    /// <summary>
    /// Repeats <paramref name="attempt"/>, a lookup that throws <see cref="InvalidOperationException"/> when it finds
    /// nothing within the time it's given, in short slices until it succeeds, the app is gone, or the timeout passes.
    /// </summary>
    public void Wait(Action<TimeSpan> attempt, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            try
            {
                var left = deadline - DateTime.UtcNow;
                attempt(left < Slice ? (left > TimeSpan.Zero ? left : TimeSpan.Zero) : Slice);
                return;
            }
            catch (InvalidOperationException) when (DateTime.UtcNow < deadline)
            {
                ThrowIfGone();
            }
        }
    }

    private static bool HasProcess(string name)
    {
        var processes = Process.GetProcessesByName(name);
        foreach (var process in processes)
        {
            process.Dispose();
        }
        return processes.Length > 0;
    }
}
