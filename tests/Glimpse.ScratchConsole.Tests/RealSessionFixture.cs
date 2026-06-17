using Glimpse.Avalonia;
using Xunit;

namespace Glimpse.ScratchConsole.Tests;

public sealed class RealSessionFixture : IDisposable
{
    public SnapshotSession Session { get; } = new();

    public void Dispose() => Session.Dispose();
}

[CollectionDefinition("real-session")]
public sealed class RealSessionCollection : ICollectionFixture<RealSessionFixture>;
