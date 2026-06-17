using Glimpse.Abstractions;
using Glimpse.ScratchConsole;
using Xunit;

namespace Glimpse.ScratchConsole.Tests;

public class CliOptionsTests
{
    [Fact]
    public void Parse_WithNoArgs_ShouldDefaultToAllScenesBothThemes()
    {
        var options = CliOptions.Parse([]);

        Assert.Equal(["all"], options.Scenes);
        Assert.Equal([SnapshotTheme.Light, SnapshotTheme.Dark], options.Themes);
        Assert.Null(options.OutDir);
    }

    [Theory]
    [InlineData("light", new[] { SnapshotTheme.Light })]
    [InlineData("dark", new[] { SnapshotTheme.Dark })]
    [InlineData("both", new[] { SnapshotTheme.Light, SnapshotTheme.Dark })]
    public void Parse_WithThemeArg_ShouldSelectExpectedThemes(string theme, SnapshotTheme[] expected)
    {
        var options = CliOptions.Parse(["--theme", theme]);

        Assert.Equal(expected, options.Themes);
    }

    [Fact]
    public void Parse_WithSceneAndOut_ShouldCaptureBoth()
    {
        var options = CliOptions.Parse(["--scene", "LoginWindow", "--out", "/tmp/x"]);

        Assert.Equal(["LoginWindow"], options.Scenes);
        Assert.Equal("/tmp/x", options.OutDir);
    }

    [Fact]
    public void Parse_WithFlagMissingItsValue_ShouldThrow()
    {
        Assert.Throws<ArgumentException>(() => CliOptions.Parse(["--scene"]));
    }

    [Fact]
    public void Parse_WithUnknownTheme_ShouldThrow()
    {
        Assert.Throws<ArgumentException>(() => CliOptions.Parse(["--theme", "sepia"]));
    }
}
