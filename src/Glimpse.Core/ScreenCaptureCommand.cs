namespace Glimpse.Core;

public enum CaptureMode
{
    FullScreen,
    Window,
    Interactive,
}

/// <summary>Builds macOS <c>screencapture</c> arguments. Pure — unit-testable without invoking the binary.</summary>
public static class ScreenCaptureCommand
{
    public static IReadOnlyList<string> BuildArgs(CaptureMode mode, string outputPath, int? windowId = null)
        => mode switch
        {
            CaptureMode.FullScreen => ["-x", outputPath],
            CaptureMode.Interactive => ["-i", outputPath],
            CaptureMode.Window when windowId is { } id => ["-x", "-o", $"-l{id}", outputPath],
            CaptureMode.Window => throw new ArgumentException("Window capture requires a windowId."),
            _ => throw new ArgumentOutOfRangeException(nameof(mode)),
        };
}
