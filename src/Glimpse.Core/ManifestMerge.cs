namespace Glimpse.Core;

/// <summary>Upserts this run's entries into the prior manifest, keyed by (Scene, Theme).</summary>
public static class ManifestMerge
{
    public const int CurrentSchemaVersion = 1;

    public static Manifest Merge(
        Manifest? existing,
        IReadOnlyList<SceneEntry> entries,
        IReadOnlyList<Failure> failures,
        string runId,
        DateTimeOffset generatedAt)
    {
        var replacedKeys = entries.Select(e => (e.Scene, e.Theme)).ToHashSet();

        var carriedScenes = existing?.Scenes.Where(e => !replacedKeys.Contains((e.Scene, e.Theme)))
            ?? Enumerable.Empty<SceneEntry>();
        var mergedScenes = carriedScenes
            .Concat(entries)
            .OrderBy(e => e.Scene, StringComparer.Ordinal)
            .ThenBy(e => e.Theme, StringComparer.Ordinal)
            .ToList();

        var carriedFailures = existing?.Failures.Where(f => !replacedKeys.Contains((f.Scene, f.Theme)))
            ?? Enumerable.Empty<Failure>();
        var mergedFailures = carriedFailures
            .Concat(failures)
            .OrderBy(f => f.Scene, StringComparer.Ordinal)
            .ThenBy(f => f.Theme, StringComparer.Ordinal)
            .ToList();

        return new Manifest(CurrentSchemaVersion, runId, generatedAt, mergedScenes, mergedFailures);
    }
}
