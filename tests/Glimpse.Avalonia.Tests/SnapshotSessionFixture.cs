using Xunit;

namespace Glimpse.Avalonia.Tests;

public sealed class SnapshotSessionFixture : IDisposable
{
    public global::Glimpse.Avalonia.SnapshotSession Session { get; } = new();

    public void Dispose() => Session.Dispose();
}

[CollectionDefinition("snapshot")]
public sealed class SnapshotCollection : ICollectionFixture<SnapshotSessionFixture>;
