namespace Glimpse.Avalonia;

/// <summary>Thrown when the runtime or Avalonia version does not match Glimpse's hard requirement.</summary>
public sealed class GlimpseEnvironmentException(string message) : Exception(message);
