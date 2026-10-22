using Glimpse.Drive;
using Xunit;

namespace Glimpse.Drive.Tests;

public class ShotArgumentsTests
{
    [Fact]
    public void Parse_WithOnlyAFile_ShouldShootTheWholeDesktop()
    {
        var shot = ShotArguments.Parse(["out.png"]);

        Assert.Equal(new ShotArguments("out.png", null, null), shot);
    }

    [Fact]
    public void Parse_WithTheFileFirst_ShouldReadTheWindowAfterIt()
    {
        var shot = ShotArguments.Parse(["out.png", "notepad", "Untitled"]);

        Assert.Equal(new ShotArguments("out.png", "notepad", "Untitled"), shot);
    }

    [Fact]
    public void Parse_WithTheFileLast_ShouldReadTheWindowBeforeIt()
    {
        var shot = ShotArguments.Parse(["notepad", "Untitled", "out.png"]);

        Assert.Equal(new ShotArguments("out.png", "notepad", "Untitled"), shot);
    }

    [Fact]
    public void Parse_WithAnUppercaseExtension_ShouldStillFindTheFile()
    {
        var shot = ShotArguments.Parse(["notepad", "Untitled", "OUT.PNG"]);

        Assert.Equal("OUT.PNG", shot.File);
    }

    [Fact]
    public void Parse_WithNoPngArgument_ShouldTakeTheFirstAsTheFile()
    {
        // Keeps the documented order working for a file named without the extension.
        var shot = ShotArguments.Parse(["capture", "notepad", "Untitled"]);

        Assert.Equal(new ShotArguments("capture", "notepad", "Untitled"), shot);
    }
}
