using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Glimpse.Avalonia.Abstractions;

namespace Glimpse.ScratchConsole.Scenes;

/// <summary>Inline scenes — no app references, deterministic stub content.</summary>
public static class SampleScenes
{
    public static IReadOnlyList<IScene> All => [new HelloScene(), new CardScene(), new ThemedControlsScene()];

    private sealed class HelloScene : IScene
    {
        public string Name => "Hello";
        public Control Build() => new TextBlock
        {
            Text = "Hello, Glimpse",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
    }

    private sealed class CardScene : IScene
    {
        public string Name => "Card";
        public Control Build() => new Border
        {
            Margin = new Thickness(40),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(24),
            Background = Brushes.SlateBlue,
            Child = new TextBlock { Text = "Card body", Foreground = Brushes.White },
        };
    }

    // Templated FluentTheme controls (Button/TextBox) pull their control templates + theme brushes
    // from the registered theme. This is the in-repo proxy for the resource-resolution risk the spec
    // flags (§4.2): if theme resources weren't applied, these render unstyled or throw at template build.
    private sealed class ThemedControlsScene : IScene
    {
        public string Name => "ThemedControls";
        public Control Build() => new StackPanel
        {
            Margin = new Thickness(24),
            Spacing = 12,
            Children =
            {
                new TextBox { Watermark = "Username", Width = 220 },
                new Button { Content = "Sign in" },
            },
        };
    }
}
