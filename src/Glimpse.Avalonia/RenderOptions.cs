using Avalonia.Media;
using Avalonia.Styling;

namespace Glimpse.Avalonia;

/// <summary>Render parameters. Theme null = Light. Background null = themed default.</summary>
public sealed record RenderOptions(
    int Width = 1024,
    int Height = 768,
    double Scaling = 1.0,
    ThemeVariant? Theme = null,
    IBrush? Background = null);
