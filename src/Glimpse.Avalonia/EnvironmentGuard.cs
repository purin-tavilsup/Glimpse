using Avalonia;

namespace Glimpse.Avalonia;

/// <summary>Fails fast when the host's .NET or Avalonia version is incompatible.</summary>
public static class EnvironmentGuard
{
    public const int RequiredRuntimeMajor = 10;
    public const int RequiredAvaloniaMajor = 11;
    public const int RequiredAvaloniaMinor = 3;

    public static void EnsureCompatible()
        => EnsureCompatible(
            typeof(Application).Assembly.GetName().Version!,
            Environment.Version);

    internal static void EnsureCompatible(Version avaloniaVersion, Version runtimeVersion)
    {
        if (runtimeVersion.Major != RequiredRuntimeMajor)
            throw new GlimpseEnvironmentException(
                $"Glimpse requires .NET {RequiredRuntimeMajor}.x; found {runtimeVersion}.");

        if (avaloniaVersion.Major != RequiredAvaloniaMajor ||
            avaloniaVersion.Minor != RequiredAvaloniaMinor)
            throw new GlimpseEnvironmentException(
                $"Glimpse requires Avalonia {RequiredAvaloniaMajor}.{RequiredAvaloniaMinor}.x; " +
                $"found {avaloniaVersion}.");
    }
}
