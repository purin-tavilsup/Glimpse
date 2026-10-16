using Glimpse.Drive;
using Xunit;

namespace Glimpse.Drive.Tests;

public class InstalledBrowserTests
{
    private static readonly string[] Candidates = ["/edge", "/chrome"];

    [Fact]
    public void Find_WithNoCandidateInstalled_ShouldThrowListingWhereItLooked()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => InstalledBrowser.Find(Candidates, _ => false));

        Assert.Contains("/edge", ex.Message);
        Assert.Contains("/chrome", ex.Message);
    }

    [Fact]
    public void Find_WithSeveralInstalled_ShouldPreferTheFirstCandidate()
    {
        Assert.Equal("/edge", InstalledBrowser.Find(Candidates, _ => true));
    }

    [Fact]
    public void Find_WithOnlyALaterCandidateInstalled_ShouldReturnIt()
    {
        Assert.Equal("/chrome", InstalledBrowser.Find(Candidates, path => path == "/chrome"));
    }
}
