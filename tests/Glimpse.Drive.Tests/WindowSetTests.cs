using Glimpse.Drive;
using Xunit;

namespace Glimpse.Drive.Tests;

public class WindowSetTests
{
    private static readonly TimeSpan Instant = TimeSpan.Zero;
    private static readonly TimeSpan NoPoll = TimeSpan.Zero;
    private const string Unexpected = "unexpected window {X=0,Y=0,Width=10,Height=10}";

    [Fact]
    public void Expect_WhenTheAppIsNotRunning_ShouldThrowBeforeTheTimeout()
    {
        var gone = new AppPresence("Notepad", _ => false, TimeSpan.Zero);

        Assert.Throws<InvalidOperationException>(() =>
            WindowSet.Expect(_ => [], "Main", TimeSpan.FromSeconds(3), NoPoll, gone));
    }

    [Fact]
    public void Expect_WithNoneWhenTheAppIsNotRunning_ShouldPass()
    {
        var gone = new AppPresence("Notepad", _ => false, TimeSpan.Zero);

        var code = WindowSet.Expect(_ => [], WindowSet.None, Instant, NoPoll, gone);

        Assert.Equal(0, code);
    }

    [Fact]
    public void Expect_WithAnExtraWindow_ShouldFail()
    {
        var code = WindowSet.Expect(_ => ["Main", Unexpected], "Main", Instant, NoPoll);

        Assert.Equal(1, code);
    }

    [Fact]
    public void Expect_WithAMissingWindow_ShouldFail()
    {
        var code = WindowSet.Expect(_ => ["Main"], "Main,Dialog", Instant, NoPoll);

        Assert.Equal(1, code);
    }

    [Fact]
    public void Expect_WithNoneButAWindowShowing_ShouldFail()
    {
        var code = WindowSet.Expect(_ => [Unexpected], WindowSet.None, Instant, NoPoll);

        Assert.Equal(1, code);
    }

    [Fact]
    public void Expect_WithNoneAndNoWindows_ShouldPass()
    {
        var code = WindowSet.Expect(_ => [], WindowSet.None, Instant, NoPoll);

        Assert.Equal(0, code);
    }

    [Fact]
    public void Expect_WithTheSameWindowsInAnotherOrder_ShouldPass()
    {
        var code = WindowSet.Expect(_ => ["Main", "Dialog"], "Dialog,Main", Instant, NoPoll);

        Assert.Equal(0, code);
    }

    [Fact]
    public void Expect_WhenTheSetMatchesOnALaterPoll_ShouldPass()
    {
        var polls = 0;

        var code = WindowSet.Expect(_ => ++polls < 3 ? [] : ["Main"], "Main", TimeSpan.FromSeconds(5), NoPoll);

        Assert.Equal(0, code);
    }

    [Fact]
    public void Hold_WhenAWindowAppearsMidway_ShouldFail()
    {
        var polls = 0;

        var code = WindowSet.Hold(_ => ++polls < 3 ? ["Main"] : ["Main", Unexpected], "Main", TimeSpan.FromSeconds(5), NoPoll);

        Assert.Equal(1, code);
    }

    [Fact]
    public void Hold_WhenTheSetStaysTheSame_ShouldPass()
    {
        var code = WindowSet.Hold(_ => ["Main"], "Main", TimeSpan.FromMilliseconds(50), NoPoll);

        Assert.Equal(0, code);
    }

    [Fact]
    public void Expect_ShouldAskTheDriverAboutTheWantedAnchors()
    {
        IReadOnlyList<string>? asked = null;

        WindowSet.Expect(a => { asked = a; return []; }, "Main,Dialog", Instant, NoPoll);

        Assert.Equal(["Main", "Dialog"], asked);
    }
}
