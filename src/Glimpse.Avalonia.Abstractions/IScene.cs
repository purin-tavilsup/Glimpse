using Avalonia.Controls;

namespace Glimpse.Avalonia.Abstractions;

/// <summary>A renderable unit: a view + its (stub-by-default) DataContext.</summary>
public interface IScene
{
    /// <summary>Stable identity; becomes the PNG file stem.</summary>
    string Name { get; }

    /// <summary>Builds the control to render. Default scenes set a hand-built stub DataContext.</summary>
    Control Build();

    /// <summary>Real-VM scenes that load asynchronously await completion here before capture.</summary>
    Task ReadyAsync() => Task.CompletedTask;
}
