using Glimpse.Drive;
using Xunit;

namespace Glimpse.Drive.Tests;

public class StepScriptTests
{
    private static readonly Dictionary<string, string> NoVariables = [];

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("# a comment")]
    [InlineData("   # an indented comment")]
    public void ParseLine_WithBlankOrComment_ShouldBeNull(string line)
    {
        Assert.Null(StepScript.ParseLine(line, NoVariables));
    }

    [Fact]
    public void ParseLine_WithUnknownVariable_ShouldThrowNamingIt()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => StepScript.ParseLine("type App EmailBox $email", NoVariables));

        Assert.Contains("$email", ex.Message);
    }

    [Fact]
    public void ParseLine_WithUnclosedQuote_ShouldKeepTheRestAsOneToken()
    {
        Assert.Equal(["keys", "App", "My App x"], StepScript.ParseLine("keys App \"My App x", NoVariables)!);
    }

    [Fact]
    public void ParseLine_WithQuotedRun_ShouldKeepItAsOneToken()
    {
        Assert.Equal(["keys", "App", "My App", "{ENTER}"], StepScript.ParseLine("keys App \"My App\" {ENTER}", NoVariables)!);
    }

    [Fact]
    public void ParseLine_WithVariable_ShouldFillItBeforeSplitting()
    {
        var variables = new Dictionary<string, string> { ["email"] = "a@example.com" };

        Assert.Equal(["type", "App", "EmailBox", "a@example.com"], StepScript.ParseLine("  type App EmailBox $email  ", variables)!);
    }

    [Fact]
    public void SplitRunArgs_WithEqualsInValue_ShouldSplitAtTheFirstEquals()
    {
        var (files, variables) = StepScript.SplitRunArgs(["flow.steps", "url=http://localhost/?a=b"]);

        Assert.Equal(["flow.steps"], files);
        Assert.Equal("http://localhost/?a=b", variables["url"]);
    }

    [Fact]
    public void SplitRunArgs_WithOnlyVariables_ShouldHaveNoFiles()
    {
        var (files, _) = StepScript.SplitRunArgs(["a=1"]);

        Assert.Empty(files);
    }
}
