namespace Glimpse.Core;

/// <summary>The parsed shape of one on-screen window (one CGWindow dictionary on macOS).</summary>
public sealed record WindowInfo(
    uint WindowId,
    string OwnerName,
    string? Title,
    int X,
    int Y,
    int Width,
    int Height,
    int Layer,
    bool OnScreen);
