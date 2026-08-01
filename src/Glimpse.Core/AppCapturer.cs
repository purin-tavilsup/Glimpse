using System.Runtime.Versioning;

namespace Glimpse.Core;

/// <summary>
/// Captures a live window (or the whole screen) to a PNG. Implementations are OS-specific:
/// macOS shells out to <c>screencapture</c>, Windows captures in-process via GDI, because
/// Windows ships no screenshot command for the renderer contract to wrap.
/// </summary>
public interface IAppCapturer
{
    /// <summary>A null <see cref="RenderRequest.WindowId"/> means "capture the whole screen".</summary>
    Task<RenderOutcome> CaptureAsync(RenderRequest request);
}

/// <summary>macOS capture via <c>screencapture</c>. Needs Screen Recording (TCC) permission.</summary>
[SupportedOSPlatform("macos")]
public sealed class MacAppCapturer(IProcessRunner runner) : IAppCapturer
{
    /// <summary>Whole-screen args. Kept here (not in the registry) because the built-in
    /// "app" spec is the window-targeted variant and needs a {windowid}.</summary>
    public static RendererSpec FullScreenSpec { get; } =
        new("app", "screencapture", ["-x", "{out}"], []);

    public Task<RenderOutcome> CaptureAsync(RenderRequest request)
    {
        var spec = request.WindowId is null
            ? FullScreenSpec
            : RendererRegistry.Default().Resolve("app", null);

        return new RenderEngine(runner).RenderAsync(spec, request);
    }
}
