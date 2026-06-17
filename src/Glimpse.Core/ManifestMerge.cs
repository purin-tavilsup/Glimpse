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
        var carried = existing?.Scenes.Where(e => !replacedKeys.Contains((e.Scene, e.Theme)))
            ?? Enumerable.Empty<SceneEntry>();

        var merged = carried
            .Concat(entries)
            .OrderBy(e => e.Scene, StringComparer.Ordinal)
            .ThenBy(e => e.Theme, StringComparer.Ordinal)
            .ToList();

        return new Manifest(CurrentSchemaVersion, runId, generatedAt, merged, failures);
    }
}
