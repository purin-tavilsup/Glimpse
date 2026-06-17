using Glimpse.Core;
using Xunit;

namespace Glimpse.Core.Tests;

public class ManifestMergeTests
{
    private static SceneEntry Entry(string scene, string theme, string status = "ok") =>
        new(scene, theme, $"{scene}.{theme}.png", 100, 100, 1.0, status,
            DateTimeOffset.UnixEpoch, []);

    private static Failure Fail(string scene, string theme) =>
        new(scene, theme, "render error");

    [Fact]
    public void Merge_WithNoExisting_ShouldContainNewEntriesSorted()
    {
        var result = ManifestMerge.Merge(null,
            [Entry("Beta", "dark"), Entry("Alpha", "light")], [], "run-1", DateTimeOffset.UnixEpoch);

        Assert.Equal(1, result.SchemaVersion);
        Assert.Equal("run-1", result.RunId);
        Assert.Collection(result.Scenes,
            e => Assert.Equal("Alpha", e.Scene),
            e => Assert.Equal("Beta", e.Scene));
    }

    [Fact]
    public void Merge_WithSameKey_ShouldReplaceEntryAndCarryOthers()
    {
        var existing = ManifestMerge.Merge(null,
            [Entry("Login", "dark", "ok"), Entry("Home", "light", "ok")], [], "run-1", DateTimeOffset.UnixEpoch);

        var result = ManifestMerge.Merge(existing,
            [Entry("Login", "dark", "failed")], [], "run-2", DateTimeOffset.UnixEpoch);

        Assert.Equal("run-2", result.RunId);
        Assert.Equal("failed", result.Scenes.Single(e => e.Scene == "Login" && e.Theme == "dark").Status);
        Assert.Contains(result.Scenes, e => e.Scene == "Home"); // carried over
    }

    [Fact]
    public void Merge_WhenSceneRerunSucceeds_ShouldDropItsPriorFailure()
    {
        var existing = ManifestMerge.Merge(null,
            [Entry("Login", "dark", "failed")], [Fail("Login", "dark")], "run-1", DateTimeOffset.UnixEpoch);

        var result = ManifestMerge.Merge(existing,
            [Entry("Login", "dark", "ok")], [], "run-2", DateTimeOffset.UnixEpoch);

        Assert.Empty(result.Failures);
        Assert.Equal("ok", result.Scenes.Single(e => e.Scene == "Login" && e.Theme == "dark").Status);
    }

    [Fact]
    public void Merge_WhenOtherSceneRerun_ShouldKeepPriorFailure()
    {
        var existing = ManifestMerge.Merge(null,
            [Entry("Login", "dark", "failed")], [Fail("Login", "dark")], "run-1", DateTimeOffset.UnixEpoch);

        var result = ManifestMerge.Merge(existing,
            [Entry("Home", "light", "ok")], [], "run-2", DateTimeOffset.UnixEpoch);

        Assert.Contains(result.Failures, f => f.Scene == "Login" && f.Theme == "dark");
        Assert.Equal("failed", result.Scenes.Single(e => e.Scene == "Login" && e.Theme == "dark").Status);
    }
}
