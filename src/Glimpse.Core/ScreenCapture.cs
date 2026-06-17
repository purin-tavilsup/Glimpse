using System.Diagnostics;

namespace Glimpse.Core;

public sealed class GlimpseCaptureException(string message) : Exception(message);

/// <summary>Thin wrapper over macOS <c>screencapture</c>. Needs Screen Recording (TCC) permission for window/screen modes.</summary>
public sealed class ScreenCapture
{
    public async Task<string> CaptureAsync(CaptureMode mode, string outputPath, int? windowId = null)
    {
        var startInfo = new ProcessStartInfo("screencapture") { RedirectStandardError = true };
        foreach (var arg in ScreenCaptureCommand.BuildArgs(mode, outputPath, windowId))
            startInfo.ArgumentList.Add(arg);

        using var process = Process.Start(startInfo)
            ?? throw new GlimpseCaptureException("Failed to start 'screencapture' (macOS only).");
        await process.WaitForExitAsync();

        if (process.ExitCode != 0 || !File.Exists(outputPath))
            throw new GlimpseCaptureException(
                $"screencapture failed (exit {process.ExitCode}): {await process.StandardError.ReadToEndAsync()}");

        return outputPath;
    }
}
