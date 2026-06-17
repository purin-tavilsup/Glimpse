using Glimpse.Core;
using Xunit;

namespace Glimpse.Core.Tests;

public class OutputLocatorTests
{
    [Fact]
    public void Resolve_InsideRepo_ShouldReturnPerRepoPath()
    {
        var repo = Directory.CreateTempSubdirectory("glimpse-repo-");
        Directory.CreateDirectory(Path.Combine(repo.FullName, ".git"));
        var nested = Directory.CreateDirectory(Path.Combine(repo.FullName, "src", "app"));

        var result = OutputLocator.Resolve(nested.FullName, "/home/user");

        Assert.Equal(Path.Combine(repo.FullName, ".claude/tmp/ui-snapshots"), result);
    }

    [Fact]
    public void Resolve_OutsideRepo_ShouldReturnCentralPath()
    {
        var loose = Directory.CreateTempSubdirectory("glimpse-loose-");

        var result = OutputLocator.Resolve(loose.FullName, "/home/user");

        Assert.Equal(Path.Combine("/home/user", ".claude/ui-snapshots"), result);
    }
}
