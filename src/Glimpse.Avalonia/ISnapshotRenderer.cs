using Avalonia.Controls;
using Glimpse.Avalonia.Abstractions;

namespace Glimpse.Avalonia;

public interface ISnapshotRenderer
{
    RenderResult Render(Func<Control> build, RenderOptions options);

    Task<RenderResult> RenderSceneAsync(IScene scene, RenderOptions options);
}
