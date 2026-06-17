using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Glimpse.Avalonia.Abstractions;
using System.Security.Cryptography;

namespace Glimpse.Avalonia;

/// <summary>
/// One-shot, non-reentrant, single-threaded headless render session.
/// The headless platform is global, so create exactly one per process.
/// </summary>
public sealed class SnapshotSession : ISnapshotRenderer, IDisposable
{
    private const int MaxSettleIterations = 20;

    private static int _instantiated;

    private readonly HeadlessUnitTestSession _session;

    public SnapshotSession()
    {
        // The headless platform is process-global; a second *successful* session would throw a confusing,
        // order-dependent error inside StartNew. Fail fast and clearly instead.
        if (Interlocked.Exchange(ref _instantiated, 1) == 1)
            throw new GlimpseRenderException(
                "SnapshotSession is one-shot per process (the Avalonia headless platform is global).");

        try
        {
            EnvironmentGuard.EnsureCompatible();
            _session = HeadlessUnitTestSession.StartNew(typeof(HeadlessSnapshotApp));
        }
        catch
        {
            Interlocked.Exchange(ref _instantiated, 0); // construction failed — let the real error surface and allow retry
            throw;
        }
    }

    // Sync entry point for tests/simple callers; the async path is canonical. Safe here because
    // the session runs its own dispatcher on a dedicated thread (no sync-over-async deadlock).
    public RenderResult Render(Control control, RenderOptions options)
        => _session.Dispatch(() => RenderCore(control, options), CancellationToken.None)
            .GetAwaiter().GetResult();

    public Task<RenderResult> RenderSceneAsync(IScene scene, RenderOptions options)
        => _session.Dispatch(async () =>
        {
            var control = scene.Build();
            await scene.ReadyAsync();
            return RenderCore(control, options);
        }, CancellationToken.None);

    private static RenderResult RenderCore(Control control, RenderOptions options)
    {
        using var _ = CultureScope.Invariant();

        var window = new Window
        {
            SystemDecorations = SystemDecorations.None,
            SizeToContent = SizeToContent.Manual,
            CanResize = false,
            Width = options.Width,
            Height = options.Height,
            Content = control,
            RequestedThemeVariant = options.Theme ?? ThemeVariant.Light,
        };
        if (options.Background is { } background)
            window.Background = background;

        try
        {
            window.Show();

            var (frame, settled, iterations) = Settle(window);
            if (frame is null)
                throw new GlimpseRenderException(
                    "CaptureRenderedFrame returned null — engine needs UseSkia() + UseHeadlessDrawing=false.");

            using (frame)
            {
                using var stream = new MemoryStream();
                frame.Save(stream);

                var warnings = new List<string>();
                if (!settled)
                    warnings.Add($"settle-cap-hit:{iterations}");
                if (options.Scaling != 1.0)
                    warnings.Add("scaling-ignored"); // headless 11.3.x has no DPI knob — see Task 6

                return new RenderResult(stream.ToArray(), frame.PixelSize.Width, frame.PixelSize.Height, warnings);
            }
        }
        finally
        {
            window.Close(); // always tear down so a throw can't leak a window into the next render
        }
    }

    /// <summary>Pumps the dispatcher + render timer until two consecutive frames are byte-identical (settled).
    /// Returns the surviving frame (caller owns/disposes it); disposes every intermediate frame it discards.</summary>
    private static (WriteableBitmap? Frame, bool Settled, int Iterations) Settle(Window window)
    {
        WriteableBitmap? lastFrame = null;
        string? previousHash = null;
        for (var i = 1; i <= MaxSettleIterations; i++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();

            var frame = window.CaptureRenderedFrame();
            if (frame is null)
                continue;

            using var stream = new MemoryStream();
            frame.Save(stream);
            var hash = Convert.ToHexString(SHA256.HashData(stream.ToArray()));
            if (hash == previousHash)
            {
                lastFrame?.Dispose();
                return (frame, true, i); // stable — caller owns/disposes this frame
            }

            previousHash = hash;
            lastFrame?.Dispose();
            lastFrame = frame;
        }

        return (lastFrame, false, MaxSettleIterations); // cap hit — return the most recent frame (or null)
    }

    public void Dispose() => _session.Dispose();
}
