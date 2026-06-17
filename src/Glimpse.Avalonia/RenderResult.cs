namespace Glimpse.Avalonia;

/// <summary>Engine output: PNG bytes + actual pixel dims + non-fatal warnings. No file paths (Core owns I/O).</summary>
public sealed record RenderResult(
    byte[] Png,
    int PixelWidth,
    int PixelHeight,
    IReadOnlyList<string> Warnings);
