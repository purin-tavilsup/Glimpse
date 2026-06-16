namespace Glimpse.Abstractions;

/// <summary>Framework-free render size. Scaling is the DPI factor (1.0 = 96 DPI).</summary>
public sealed record SnapshotSize(int Width, int Height, double Scaling = 1.0);
