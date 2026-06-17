using Avalonia.Styling;
using Glimpse.Abstractions;
using Glimpse.Avalonia;
using Glimpse.Avalonia.Abstractions;
using Glimpse.Core;

namespace Glimpse.ScratchConsole;

/// <summary>Renders each (scene, theme), writes PNGs + a merged manifest. Fail-soft per scene.</summary>
public sealed class SnapshotRunner(ISnapshotRenderer renderer, SnapshotWriter writer)
{
    private const int ExitOk = 0;
    private const int ExitSomeFailed = 1;
    private const int ExitBadInput = 2;

    public async Task<int> RunAsync(
        IReadOnlyList<IScene> scenes,
        IReadOnlyList<SnapshotTheme> themes,
        string runId,
        DateTimeOffset now)
    {
        if (scenes.Count == 0)
            return ExitBadInput; // empty discovery is an error (spec §4.4)

        if (scenes.Select(s => s.Name).Distinct(StringComparer.Ordinal).Count() != scenes.Count)
            return ExitBadInput; // duplicate names would collide on <scene>.png

        var entries = new List<SceneEntry>();
        var failures = new List<Failure>();

        foreach (var scene in scenes)
            foreach (var theme in themes)
                await RenderOneAsync(scene, theme, now, entries, failures);

        writer.WriteManifest(ManifestMerge.Merge(writer.ReadExisting(), entries, failures, runId, now));
        return failures.Count > 0 ? ExitSomeFailed : ExitOk;
    }

    private async Task RenderOneAsync(
        IScene scene, SnapshotTheme theme, DateTimeOffset now,
        List<SceneEntry> entries, List<Failure> failures)
    {
        var themeName = theme.ToString().ToLowerInvariant();
        try
        {
            var options = new RenderOptions(Theme: ThemeVariantFor(theme));
            var result = await renderer.RenderSceneAsync(scene, options);
            var path = writer.SavePng(scene.Name, themeName, result.Png);
            entries.Add(new SceneEntry(scene.Name, themeName, path,
                result.PixelWidth, result.PixelHeight, options.Scaling, "ok", now, result.Warnings));
        }
        catch (Exception ex)
        {
            failures.Add(new Failure(scene.Name, themeName, ex.Message));
            entries.Add(new SceneEntry(scene.Name, themeName, $"{scene.Name}.{themeName}.png",
                0, 0, 1.0, "failed", now, [ex.Message]));
        }
    }

    private static ThemeVariant ThemeVariantFor(SnapshotTheme theme)
        => theme == SnapshotTheme.Dark ? ThemeVariant.Dark : ThemeVariant.Light;
}
