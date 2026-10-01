using Glimpse.Core;
using Xunit;

namespace Glimpse.Core.Tests;

public class ToolLocatorTests
{
    /// <summary>A throwaway PATH directory containing exactly the files a test names.</summary>
    private static string DirectoryWith(params string[] fileNames)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"glimpse-tool-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        foreach (var name in fileNames)
            File.WriteAllText(Path.Combine(dir, name), "");
        return dir;
    }

    private static ToolSearchEnvironment WindowsEnv(string dir, string? pathExt = ".COM;.EXE;.BAT;.CMD;.VBS;.PY")
        => new(dir, pathExt, WindowsRules: true);

    private static ToolSearchEnvironment UnixEnv(string dir)
        => new(dir, null, WindowsRules: false);

    [Fact]
    public void Resolve_OnWindowsWithBothExtensionlessAndCmd_ShouldPickTheCmd()
    {
        // npm installs BOTH 'mmdc' (a bash script) and 'mmdc.cmd'. Verified 2026-08-01:
        // Process.Start on the extensionless file throws "not a valid application for this
        // OS platform", so resolving it would break every mermaid render on Windows.
        var dir = DirectoryWith("mmdc", "mmdc.cmd");

        var resolved = ToolLocator.Resolve("mmdc", WindowsEnv(dir));

        Assert.Equal(Path.Combine(dir, "mmdc.cmd"), resolved);
    }

    [Fact]
    public void Resolve_OnWindowsWithOnlyAnExtensionlessFile_ShouldReturnNull()
    {
        var dir = DirectoryWith("mmdc");

        Assert.Null(ToolLocator.Resolve("mmdc", WindowsEnv(dir)));
    }

    [Fact]
    public void Resolve_OnWindowsWithUnlaunchableExtensionOnly_ShouldReturnNull()
    {
        // PATHEXT lists .VBS and .PY, but CreateProcess cannot start either directly.
        var dir = DirectoryWith("thing.vbs", "thing.py");

        Assert.Null(ToolLocator.Resolve("thing", WindowsEnv(dir)));
    }

    [Fact]
    public void Resolve_OnWindowsWithSeveralLaunchableExtensions_ShouldFollowPathExtOrder()
    {
        var dir = DirectoryWith("tool.cmd", "tool.exe");

        var resolved = ToolLocator.Resolve("tool", WindowsEnv(dir, ".COM;.EXE;.BAT;.CMD"));

        Assert.Equal(Path.Combine(dir, "tool.exe"), resolved);
    }

    [Fact]
    public void Resolve_OnWindowsWhenPathExtIsEmpty_ShouldStillFindAnExe()
    {
        var dir = DirectoryWith("tool.exe");

        var resolved = ToolLocator.Resolve("tool", WindowsEnv(dir, pathExt: null));

        Assert.Equal(Path.Combine(dir, "tool.exe"), resolved);
    }

    [Fact]
    public void Resolve_OnWindowsWhenCallerSuppliesTheExtension_ShouldHonourItExactly()
    {
        var dir = DirectoryWith("tool.exe", "tool.cmd");

        var resolved = ToolLocator.Resolve("tool.cmd", WindowsEnv(dir));

        Assert.Equal(Path.Combine(dir, "tool.cmd"), resolved);
    }

    [Fact]
    public void Resolve_OnUnixWithAnExtensionlessFile_ShouldReturnIt()
    {
        var dir = DirectoryWith("mmdc");

        var resolved = ToolLocator.Resolve("mmdc", UnixEnv(dir));

        Assert.Equal(Path.Combine(dir, "mmdc"), resolved);
    }

    [Fact]
    public void Resolve_WithEarlierPathEntryWinning_ShouldReturnTheFirstMatch()
    {
        var first = DirectoryWith("tool.exe");
        var second = DirectoryWith("tool.exe");
        var env = new ToolSearchEnvironment(
            $"{first}{Path.PathSeparator}{second}", ".EXE", WindowsRules: true);

        Assert.Equal(Path.Combine(first, "tool.exe"), ToolLocator.Resolve("tool", env));
    }

    [Fact]
    public void Resolve_WithQuotedPathEntry_ShouldStillProbeIt()
    {
        // Windows PATH entries are sometimes quoted; an unstripped quote makes the
        // directory unopenable and silently hides every tool inside it.
        var dir = DirectoryWith("tool.exe");
        var env = new ToolSearchEnvironment($"\"{dir}\"", ".EXE", WindowsRules: true);

        Assert.Equal(Path.Combine(dir, "tool.exe"), ToolLocator.Resolve("tool", env));
    }

    [Fact]
    public void Resolve_WithNonexistentPathEntry_ShouldNotThrow()
    {
        var env = new ToolSearchEnvironment(
            Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}"), ".EXE", WindowsRules: true);

        Assert.Null(ToolLocator.Resolve("tool", env));
    }

    [Fact]
    public void Resolve_WithRealToolOnRealPath_ShouldReturnAbsolutePath()
    {
        // 'dotnet' is on PATH by definition wherever this suite runs — unlike 'ls',
        // which the previous version of this test used and which Windows lacks.
        var path = ToolLocator.Resolve("dotnet");

        Assert.NotNull(path);
        Assert.True(Path.IsPathRooted(path));
    }

    [Fact]
    public void Resolve_WithNonexistentTool_ShouldReturnNull()
    {
        Assert.Null(ToolLocator.Resolve("definitely-not-a-real-tool-xyz"));
    }

    [Fact]
    public void Resolve_ForChrome_ShouldReturnAnExistingFileOrNull()
    {
        // Chrome is never on PATH, so it has a per-OS special case. Assert the contract
        // (a real file, or null) rather than a machine-specific path.
        var path = ToolLocator.Resolve("chrome");

        Assert.True(path is null || File.Exists(path));
    }

    [Theory]
    [InlineData("mmdc", "mermaid-cli")]
    [InlineData("dot", "graphviz")]
    [InlineData("d2", "d2")]
    public void InstallHint_ForKnownTool_ShouldMentionThePackage(string tool, string fragment)
    {
        var hint = ToolLocator.InstallHint(tool);

        Assert.Contains(fragment, hint);
    }

    [Fact]
    public void InstallHint_ShouldNameThePackageManagerForThisPlatform()
    {
        // A hint naming the wrong package manager is worse than no hint: it sends the
        // reader somewhere that cannot possibly work.
        var hint = ToolLocator.InstallHint("d2");

        var expected = OperatingSystem.IsWindows() ? "winget" : "brew";
        Assert.Contains(expected, hint);
    }

    [Fact]
    public void InstallHint_ForUnknownTool_ShouldFallBackToAGenericMessage()
    {
        Assert.Contains("mystery-tool", ToolLocator.InstallHint("mystery-tool"));
    }
}
