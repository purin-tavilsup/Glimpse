using Glimpse.Abstractions;
using Xunit;

namespace Glimpse.Core.Tests;

public class AbstractionsTests
{
    [Fact]
    public void SnapshotSize_DefaultScaling_ShouldBeOne()
    {
        var size = new SnapshotSize(1024, 768);

        Assert.Equal(1.0, size.Scaling);
    }

    [Fact]
    public void SnapshotTheme_ShouldHaveLightAndDark()
    {
        Assert.Equal(0, (int)SnapshotTheme.Light);
        Assert.Equal(1, (int)SnapshotTheme.Dark);
    }
}
