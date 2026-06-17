using Glimpse.Avalonia;
using Xunit;

namespace Glimpse.Avalonia.Tests;

public class EnvironmentGuardTests
{
    [Fact]
    public void EnsureCompatible_WithMatchingVersions_ShouldNotThrow()
    {
        EnvironmentGuard.EnsureCompatible(new Version(11, 3, 0), new Version(10, 0, 0));
    }

    [Theory]
    [InlineData(11, 2, 9, 0)]   // wrong Avalonia minor
    [InlineData(12, 0, 10, 0)]  // wrong Avalonia major
    public void EnsureCompatible_WithWrongAvalonia_ShouldThrow(int aMaj, int aMin, int rtMaj, int rtMin)
    {
        Assert.Throws<GlimpseEnvironmentException>(() =>
            EnvironmentGuard.EnsureCompatible(new Version(aMaj, aMin, 0), new Version(rtMaj, rtMin)));
    }

    [Fact]
    public void EnsureCompatible_WithWrongRuntimeMajor_ShouldThrow()
    {
        Assert.Throws<GlimpseEnvironmentException>(() =>
            EnvironmentGuard.EnsureCompatible(new Version(11, 3, 0), new Version(9, 0)));
    }
}
