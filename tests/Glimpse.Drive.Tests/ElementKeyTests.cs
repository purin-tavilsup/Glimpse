using Glimpse.Drive;
using Xunit;

namespace Glimpse.Drive.Tests;

public class ElementKeyTests
{
    [Theory]
    [InlineData("CloseButton")]
    [InlineData("~Hidden Icons")]
    [InlineData("a/b")]
    public void Parse_WithoutAtPrefix_ShouldHaveNoAnchor(string key)
    {
        var parsed = ElementKey.Parse(key);

        Assert.Null(parsed.Anchor);
        Assert.Equal(key, parsed.Target);
    }

    [Fact]
    public void Parse_WithAtButNoSlash_ShouldKeepTheWholeKeyAsTarget()
    {
        var parsed = ElementKey.Parse("@TitleTextBox");

        Assert.Null(parsed.Anchor);
        Assert.Equal("@TitleTextBox", parsed.Target);
    }

    [Fact]
    public void Parse_WithAnchor_ShouldSplitAtTheFirstSlash()
    {
        var parsed = ElementKey.Parse("@TitleTextBox/Parent>CloseButton/x");

        Assert.Equal("TitleTextBox", parsed.Anchor);
        Assert.Equal("Parent>CloseButton/x", parsed.Target);
    }

    [Fact]
    public void Path_WithNestedScopes_ShouldListThemOutermostFirst()
    {
        Assert.Equal(["Form", "Panel", "textBox1"], ElementKey.Path("Form>Panel>textBox1"));
    }

    [Fact]
    public void Path_WithoutScopes_ShouldBeTheKeyAlone()
    {
        Assert.Equal(["~Save as"], ElementKey.Path("~Save as"));
    }
}
