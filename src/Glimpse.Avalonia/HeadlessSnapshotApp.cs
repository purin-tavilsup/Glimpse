using Avalonia;
using Avalonia.Headless;
using Avalonia.Themes.Fluent;

namespace Glimpse.Avalonia;

/// <summary>The headless Avalonia application booted once per process. Skia on, headless drawing off (real pixels).</summary>
public sealed class HeadlessSnapshotApp : Application
{
    public override void Initialize() => Styles.Add(new FluentTheme());

    // HeadlessUnitTestSession.StartNew(Type) reflectively invokes this static method to get the builder.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<HeadlessSnapshotApp>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .WithInterFont();
}
