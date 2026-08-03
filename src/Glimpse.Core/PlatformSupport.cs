namespace Glimpse.Core;

/// <summary>
/// Picks the OS-specific window finder and capturer. A null return is the honest
/// "this OS has no live capture" signal, so callers raise one clear message instead of
/// scattering OperatingSystem checks.
/// </summary>
public static class PlatformSupport
{
    public static IWindowFinder? WindowFinder()
    {
        if (OperatingSystem.IsMacOS())
            return new MacWindowFinder();

        if (OperatingSystem.IsWindows())
            return new WindowsWindowFinder();

        return null;
    }

    public static IAppCapturer? AppCapturer(IProcessRunner runner)
    {
        if (OperatingSystem.IsMacOS())
            return new MacAppCapturer(runner);

        if (OperatingSystem.IsWindows())
            return new WindowsAppCapturer();

        return null;
    }

    public static string UnsupportedMessage(string feature)
        => $"{feature} needs live-window support, which Glimpse provides on macOS and Windows only.";
}
