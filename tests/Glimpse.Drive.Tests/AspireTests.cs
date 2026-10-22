using Glimpse.Drive;
using Xunit;

namespace Glimpse.Drive.Tests;

public class AspireTests
{
    [Fact]
    public void TimeoutMessage_WithAWaitingResource_ShouldPointAtItsDependencies()
    {
        var message = Aspire.TimeoutMessage("web", "Running", "web | Project | Waiting");

        Assert.Contains("web | Project | Waiting", message);
        Assert.Contains("resources it waits for", message);
    }

    [Fact]
    public void TimeoutMessage_WithAnotherState_ShouldNotMentionDependencies()
    {
        var message = Aspire.TimeoutMessage("web", "Running", "web | Project | Starting");

        Assert.DoesNotContain("waits for", message);
    }

    [Fact]
    public void TimeoutMessage_WithNoRow_ShouldSaySo()
    {
        var message = Aspire.TimeoutMessage("web", "Running", "");

        Assert.Contains("(not found)", message);
    }

    [Fact]
    public void TimeoutMessage_WhenAStopFindsTheResourceStillRunning_ShouldSuggestTheAppRestartedItself()
    {
        var message = Aspire.TimeoutMessage("app", "Finished|Exited", "app | Project | Running");

        Assert.Contains("restarted itself", message);
    }

    [Fact]
    public void TimeoutMessage_WhenAStartTimesOut_ShouldNotMentionARestart()
    {
        var message = Aspire.TimeoutMessage("app", "Running", "app | Project | Running");

        Assert.DoesNotContain("restarted itself", message);
    }
}
