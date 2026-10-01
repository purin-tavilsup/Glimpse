using System.Runtime.InteropServices;
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
public sealed class MacAppCapturer(IProcessRunner runner) : IAppCapturer
{
    /// <summary>Whole-screen args. Kept here (not in the registry) because the built-in
    /// "app" spec is the window-targeted variant and needs a {windowid}.</summary>
    public static RendererSpec FullScreenSpec { get; } =
        new("app", "screencapture", ["-x", "{out}"], []);

    // The platform attribute sits on the BEHAVIOUR, not the type: FullScreenSpec is a plain
    // RendererSpec record with no interop, so attributing the whole class would force an
    // untrue [SupportedOSPlatform("macos")] onto every test that merely reads that data.
    [SupportedOSPlatform("macos")]
    public Task<RenderOutcome> CaptureAsync(RenderRequest request)
    {
        var spec = request.WindowId is null
            ? FullScreenSpec
            : RendererRegistry.Default().Resolve("app", null);

        return new RenderEngine(runner).RenderAsync(spec, request);
    }
}

/// <summary>
/// Windows capture via GDI. Windows ships no screenshot command, so this runs in-process:
/// PrintWindow into a top-down DIB, then encode with SkiaSharp (already a Glimpse.Core
/// dependency — see PngAnalysis, which decodes with the same library).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsAppCapturer : IAppCapturer
{
    private const uint PrintWindowRenderFullContent = 0x2; // without this, DWM windows capture black
    private const int DibRgbColors = 0;
    private const int BiRgb = 0;
    private const uint SrcCopy = 0x00CC0020;
    private const uint CaptureBlt = 0x40000000; // includes layered windows
    private const int DwmwaExtendedFrameBounds = 9;

    private const int SmXVirtualScreen = 76;
    private const int SmYVirtualScreen = 77;
    private const int SmCxVirtualScreen = 78;
    private const int SmCyVirtualScreen = 79;

    private static readonly IntPtr PerMonitorAwareV2 = new(-4);
    private static bool dpiDeclared;

    /// <summary>Pixel dimensions only, no position -- what <see cref="WithDib"/> needs to size a DIB.</summary>
    private readonly record struct PixelSize(int Width, int Height);

    /// <summary>A screen-space rectangle: position and size. Doubles as the geometry for both a
    /// window (from GetWindowRect) and the virtual desktop (from GetSystemMetrics).</summary>
    private readonly record struct ScreenRegion(int X, int Y, int Width, int Height)
    {
        public PixelSize Size => new(Width, Height);
    }

    public Task<RenderOutcome> CaptureAsync(RenderRequest request)
    {
        try
        {
            DeclareDpiAwareness();

            var captured = request.WindowId is { } id
                ? CaptureWindow(new IntPtr(id))
                : CaptureVirtualScreen();

            BgraPngEncoder.Write(captured.Bgra, captured.Width, captured.Height, request.OutputPath);
            return Task.FromResult(RenderOutcomes.From(request.OutputPath, true, ""));
        }
        catch (GlimpseCaptureException ex)
        {
            return Task.FromResult(RenderOutcomes.From(request.OutputPath, false, ex.Message));
        }
        catch (Exception ex)
        {
            // The external-tool path turns ANY tool failure into failed/exit 2 (see
            // RenderEngine.RenderAsync). This path must not be less forgiving, or the same
            // user error -- an unwritable --out path, say -- crashes on Windows and reports
            // cleanly on macOS.
            return Task.FromResult(RenderOutcomes.From(request.OutputPath, false, ex.Message));
        }
    }

    /// <summary>Without per-monitor-v2 awareness Win32 reports virtualised coordinates and
    /// captures come back downscaled and blurry on any scaled display.</summary>
    private static void DeclareDpiAwareness()
    {
        if (dpiDeclared)
            return;

        dpiDeclared = true;
        // Fails harmlessly if awareness was already set (e.g. by an app manifest).
        SetProcessDpiAwarenessContext(PerMonitorAwareV2);
    }

    private static CapturedFrame CaptureWindow(IntPtr hwnd)
    {
        if (!IsWindow(hwnd))
            throw new GlimpseCaptureException($"No such window: {hwnd.ToInt64()}.");

        var windowRect = WindowRect(hwnd);
        if (windowRect.Width <= 0 || windowRect.Height <= 0)
            throw new GlimpseCaptureException("Window has no capturable area.");

        // Sized and blitted at the FULL window rect, not the (smaller) DWM-visible bounds:
        // PrintWindow renders at window-rect size unconditionally, so a smaller DIB would
        // receive the content shifted and cropped instead of simply excluding the border.
        var buffer = WithDib(windowRect.Size, memoryDc =>
            PrintWindow(hwnd, memoryDc, PrintWindowRenderFullContent)
            // PrintWindow refused: fall back to lifting the window's region off the screen.
            || BlitFromScreen(memoryDc, windowRect));

        var captured = new CapturedFrame(buffer, windowRect.Width, windowRect.Height);
        return BgraCrop.Apply(captured, VisibleInset(hwnd, windowRect));
    }

    private static CapturedFrame CaptureVirtualScreen()
    {
        var region = new ScreenRegion(
            GetSystemMetrics(SmXVirtualScreen), GetSystemMetrics(SmYVirtualScreen),
            GetSystemMetrics(SmCxVirtualScreen), GetSystemMetrics(SmCyVirtualScreen));

        if (region.Width <= 0 || region.Height <= 0)
            throw new GlimpseCaptureException("Could not determine the virtual screen size.");

        var buffer = WithDib(region.Size, memoryDc => BlitFromScreen(memoryDc, region));
        return new CapturedFrame(buffer, region.Width, region.Height);
    }

    private static bool BlitFromScreen(IntPtr memoryDc, ScreenRegion region)
    {
        var screenDc = GetDC(IntPtr.Zero);
        try
        {
            return BitBlt(memoryDc, 0, 0, region.Width, region.Height,
                screenDc, region.X, region.Y, SrcCopy | CaptureBlt);
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    /// <summary>Creates a top-down 32bpp DIB, runs <paramref name="draw"/> into it, and
    /// copies the pixels out. Owns every GDI handle it creates.</summary>
    private static byte[] WithDib(PixelSize size, Func<IntPtr, bool> draw)
    {
        var header = BuildTopDownHeader(size);

        var memoryDc = CreateCompatibleDC(IntPtr.Zero);
        if (memoryDc == IntPtr.Zero)
            throw new GlimpseCaptureException("CreateCompatibleDC failed.");

        var dib = IntPtr.Zero;
        try
        {
            dib = CreateDIBSection(memoryDc, ref header, DibRgbColors, out var pixels, IntPtr.Zero, 0);
            if (dib == IntPtr.Zero || pixels == IntPtr.Zero)
                throw new GlimpseCaptureException("CreateDIBSection failed.");

            return DrawIntoSurface(new DibSurface(memoryDc, dib, pixels, size), draw);
        }
        finally
        {
            if (dib != IntPtr.Zero)
                DeleteObject(dib);
            DeleteDC(memoryDc);
        }
    }

    private static BitmapInfoHeader BuildTopDownHeader(PixelSize size) => new()
    {
        Size = (uint)Marshal.SizeOf<BitmapInfoHeader>(),
        Width = size.Width,
        Height = -size.Height, // NEGATIVE = top-down rows; positive yields a flipped capture
        Planes = 1,
        BitCount = 32,
        Compression = BiRgb,
    };

    /// <summary>The live handles <see cref="DrawIntoSurface"/> needs. Ownership (i.e. deletion)
    /// stays with <see cref="WithDib"/>'s finally block -- this is a parameter bundle, not a
    /// second owner.</summary>
    private readonly record struct DibSurface(IntPtr MemoryDc, IntPtr Bitmap, IntPtr Pixels, PixelSize Size);

    private static byte[] DrawIntoSurface(DibSurface surface, Func<IntPtr, bool> draw)
    {
        var previous = SelectObject(surface.MemoryDc, surface.Bitmap);
        try
        {
            if (!draw(surface.MemoryDc))
                throw new GlimpseCaptureException("PrintWindow and BitBlt both failed.");

            var buffer = new byte[surface.Size.Width * surface.Size.Height * 4];
            Marshal.Copy(surface.Pixels, buffer, 0, buffer.Length);
            return buffer;
        }
        finally
        {
            SelectObject(surface.MemoryDc, previous);
        }
    }

    private static ScreenRegion WindowRect(IntPtr hwnd)
    {
        if (!GetWindowRect(hwnd, out var rect))
            return default;

        return new ScreenRegion(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
    }

    /// <summary>DWMWA_EXTENDED_FRAME_BOUNDS excludes the invisible resize border that
    /// <paramref name="windowRect"/> (from GetWindowRect) includes; the difference on each edge
    /// is what the capture must be cropped by afterwards. Falls back to a zero inset (a no-op
    /// crop) when DWM is unavailable.</summary>
    private static BgraCrop.Insets VisibleInset(IntPtr hwnd, ScreenRegion windowRect)
    {
        if (DwmGetWindowAttribute(hwnd, DwmwaExtendedFrameBounds, out Rect frame,
                Marshal.SizeOf<Rect>()) != 0)
            return default;

        return new BgraCrop.Insets(
            frame.Left - windowRect.X,
            frame.Top - windowRect.Y,
            windowRect.X + windowRect.Width - frame.Right,
            windowRect.Y + windowRect.Height - frame.Bottom);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public uint ClrUsed;
        public uint ClrImportant;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PrintWindow(IntPtr hwnd, IntPtr deviceContext, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hwnd, IntPtr deviceContext);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr context);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr deviceContext);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateDIBSection(IntPtr deviceContext, ref BitmapInfoHeader header,
        uint usage, out IntPtr bits, IntPtr section, uint offset);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr deviceContext, IntPtr gdiObject);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BitBlt(IntPtr destination, int x, int y, int width, int height,
        IntPtr source, int sourceX, int sourceY, uint operation);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr gdiObject);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteDC(IntPtr deviceContext);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out Rect value, int size);
}
