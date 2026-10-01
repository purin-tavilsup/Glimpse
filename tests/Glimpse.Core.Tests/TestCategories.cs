namespace Glimpse.Core.Tests;

public static class TestCategories
{
    /// <summary>Needs an interactive desktop with real windows on it. A CI runner's bare session
    /// would fail these for an environment reason, so CI filters them out and they run locally.</summary>
    public const string RealDesktop = "RealDesktop";
}
