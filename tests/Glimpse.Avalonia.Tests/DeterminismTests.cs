using Avalonia.Controls;
using Avalonia.Media;
using Glimpse.Avalonia;
using Xunit;

namespace Glimpse.Avalonia.Tests;

[Collection("snapshot")]
public class DeterminismTests(SnapshotSessionFixture fixture)
{
    [Fact]
    public void Render_SameSceneThreeTimes_ShouldProduceByteIdenticalPng()
    {
        Control NewControl() => new TextBlock { Text = "deterministic", Foreground = Brushes.Black };

        var results = Enumerable.Range(0, 3)
            .Select(_ => fixture.Session.Render(NewControl, new RenderOptions(Width: 200, Height: 100)))
            .ToList();

        // Guard: determinism must not be satisfied by three identical *blank* frames.
        Assert.DoesNotContain(results[0].Warnings, w => w.StartsWith("single-color-frame"));

        var hashes = results.Select(r => Convert.ToHexString(r.Png)).Distinct().ToList();
        Assert.Single(hashes);
    }

    [Fact(Skip = "headless render timer does not drive indeterminate animations")]
    public void Render_PerpetuallyAnimatingControl_ShouldWarnSettleCapHit()
    {
        // An indeterminate ProgressBar animates forever, so no two consecutive frames match → cap hit.
        var result = fixture.Session.Render(
            () => new ProgressBar { IsIndeterminate = true, Width = 160, Height = 8 },
            new RenderOptions(Width: 200, Height: 100));

        Assert.Contains(result.Warnings, w => w.StartsWith("settle-cap-hit"));
    }
}
