using Glimpse.Drive;
using Xunit;

namespace Glimpse.Drive.Tests;

public class AppPresenceTests
{
    private const string App = "Notepad";
    private static readonly TimeSpan NoGrace = TimeSpan.Zero;
    private static readonly TimeSpan LongGrace = TimeSpan.FromHours(1);
    private static readonly TimeSpan LongTimeout = TimeSpan.FromHours(1);

    [Fact]
    public void ThrowIfGone_WhenTheAppIsGonePastTheGrace_ShouldThrowNamingTheApp()
    {
        var presence = new AppPresence(App, _ => false, NoGrace);

        var ex = Assert.Throws<InvalidOperationException>(presence.ThrowIfGone);

        Assert.Contains($"{App} is not running", ex.Message);
    }

    [Fact]
    public void ThrowIfGone_WhenTheAppIsGoneWithinTheGrace_ShouldNotThrow()
    {
        var presence = new AppPresence(App, _ => false, LongGrace);

        presence.ThrowIfGone();
    }

    [Fact]
    public void ThrowIfGone_WhenTheAppComesBack_ShouldRestartTheGrace()
    {
        var running = new Queue<bool>([false, true, false]);
        var presence = new AppPresence(App, _ => running.Dequeue(), TimeSpan.FromMilliseconds(50));

        presence.ThrowIfGone();
        Thread.Sleep(60);
        presence.ThrowIfGone();
        presence.ThrowIfGone();
    }

    [Fact]
    public void ThrowIfGone_WhenTheAppIsRunning_ShouldNotThrow()
    {
        var presence = new AppPresence(App, _ => true, NoGrace);

        presence.ThrowIfGone();
    }

    [Fact]
    public void Wait_WhenTheAppIsGone_ShouldThrowNotRunningLongBeforeTheTimeout()
    {
        var presence = new AppPresence(App, _ => false, NoGrace);

        var ex = Assert.Throws<InvalidOperationException>(() => presence.Wait(_ => throw NotFound(), LongTimeout));

        Assert.Contains("not running", ex.Message);
    }

    [Fact]
    public void Wait_WhenTheTimeoutPassesWhileTheAppRuns_ShouldRethrowTheLookupFailure()
    {
        var presence = new AppPresence(App, _ => true, NoGrace);

        var ex = Assert.Throws<InvalidOperationException>(() => presence.Wait(_ => throw NotFound(), TimeSpan.Zero));

        Assert.Equal(NotFound().Message, ex.Message);
    }

    [Fact]
    public void Wait_WhenALaterAttemptFindsIt_ShouldReturn()
    {
        var attempts = 0;
        var presence = new AppPresence(App, _ => true, NoGrace);

        presence.Wait(_ => { if (++attempts < 3) throw NotFound(); }, LongTimeout);

        Assert.Equal(3, attempts);
    }

    [Fact]
    public void Wait_ShouldAttemptInSlicesNoLongerThanTheTimeout()
    {
        var slices = new List<TimeSpan>();
        var presence = new AppPresence(App, _ => true, NoGrace);

        presence.Wait(slices.Add, TimeSpan.FromMilliseconds(100));

        Assert.True(slices.Single() <= TimeSpan.FromMilliseconds(100));
    }

    private static InvalidOperationException NotFound() => new("'Save' not found in Notepad");
}
