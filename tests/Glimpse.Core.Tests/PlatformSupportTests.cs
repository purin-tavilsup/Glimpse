using Glimpse.Core;
using Xunit;

namespace Glimpse.Core.Tests;

public class PlatformSupportTests
{
    [Fact]
    public void WindowFinder_OnASupportedOs_ShouldReturnThatOsImplementation()
    {
        var finder = PlatformSupport.WindowFinder();

        if (OperatingSystem.IsWindows())
            Assert.IsType<WindowsWindowFinder>(finder);
        else if (OperatingSystem.IsMacOS())
            Assert.IsType<MacWindowFinder>(finder);
        else
            Assert.Null(finder);
    }

    [Fact]
    public void AppCapturer_OnASupportedOs_ShouldReturnThatOsImplementation()
    {
        var capturer = PlatformSupport.AppCapturer(new ProcessRunner());

        if (OperatingSystem.IsWindows())
            Assert.IsType<WindowsAppCapturer>(capturer);
        else if (OperatingSystem.IsMacOS())
            Assert.IsType<MacAppCapturer>(capturer);
        else
            Assert.Null(capturer);
    }

    [Fact]
    public void UnsupportedMessage_ShouldNameTheFeatureAndTheSupportedPlatforms()
    {
        var message = PlatformSupport.UnsupportedMessage("--list-windows");

        Assert.Contains("--list-windows", message);
        Assert.Contains("macOS", message);
        Assert.Contains("Windows", message);
    }
}
