using Glimpse.Capture;
using Xunit;

namespace Glimpse.Capture.Tests;

public class CliTests
{
    private readonly StringWriter _output = new();
    private readonly StringWriter _error = new();

    [Fact]
    public void Start_WithUnknownFlag_ShouldExitWithTwo()
    {
        var start = Cli.Start(["--bogus"], _output, _error);

        Assert.Null(start.Options);
        Assert.Equal(2, start.ExitCode);
    }

    [Fact]
    public void Start_WithUnknownFlag_ShouldPrintTheErrorThenUsageToError()
    {
        Cli.Start(["--bogus"], _output, _error);

        var error = _error.ToString();
        Assert.StartsWith("Unexpected argument '--bogus'.", error);
        Assert.Contains("Usage:", error);
        Assert.Empty(_output.ToString());
    }

    [Fact]
    public void Start_WithMissingFlagValue_ShouldExitWithTwo()
    {
        var start = Cli.Start(["x.mmd", "--size"], _output, _error);

        Assert.Equal(2, start.ExitCode);
        Assert.StartsWith("Missing value for '--size'.", _error.ToString());
    }

    [Theory]
    [InlineData("--help")]
    [InlineData("-h")]
    public void Start_WithHelpFlag_ShouldPrintUsageAndExitWithZero(string flag)
    {
        var start = Cli.Start([flag], _output, _error);

        Assert.Null(start.Options);
        Assert.Equal(0, start.ExitCode);
        Assert.StartsWith("Usage:", _output.ToString());
        Assert.Empty(_error.ToString());
    }

    [Fact]
    public void Start_WithVersionFlag_ShouldPrintTheBareVersionAndExitWithZero()
    {
        var start = Cli.Start(["--version"], _output, _error);

        Assert.Null(start.Options);
        Assert.Equal(0, start.ExitCode);
        Assert.Matches(@"^\d+\.\d+\.\d+(-[0-9A-Za-z.]+)?\r?\n$", _output.ToString());
    }

    [Fact]
    public void Start_WithASource_ShouldReturnTheOptionsAndPrintNothing()
    {
        var start = Cli.Start(["x.mmd"], _output, _error);

        Assert.Equal("x.mmd", start.Options?.Source);
        Assert.Empty(_output.ToString());
        Assert.Empty(_error.ToString());
    }
}
