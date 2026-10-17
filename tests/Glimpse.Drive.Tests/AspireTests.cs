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
}
