namespace Glimpse.Core;

public sealed record SceneEntry(
    string Scene,
    string Theme,
    string Path,
    int Width,
    int Height,
    double Scaling,
    string Status,                       // "ok" | "failed" | "stale"
    DateTimeOffset RenderedAt,
    IReadOnlyList<string> Warnings);

public sealed record Failure(string Scene, string Theme, string Error);

public sealed record Manifest(
    int SchemaVersion,
    string RunId,
    DateTimeOffset GeneratedAt,
    IReadOnlyList<SceneEntry> Scenes,
    IReadOnlyList<Failure> Failures);
