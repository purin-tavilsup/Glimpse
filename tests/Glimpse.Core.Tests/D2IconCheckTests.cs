using Glimpse.Core;
using Xunit;

namespace Glimpse.Core.Tests;

public class D2IconCheckTests
{
    [Fact]
    public void ExtractIconUrls_FindsAllHttpUrls()
    {
        var src = "a: A {\n  shape: image\n  icon: https://x/a.svg\n}\n" +
                  "b: B { shape: image; icon: https://y/b%20c.svg }\n" +
                  "c: plain box";

        var urls = D2IconCheck.ExtractIconUrls(src);

        Assert.Equal(new[] { "https://x/a.svg", "https://y/b%20c.svg" }, urls);
    }

    [Fact]
    public void ExtractIconUrls_WithNoIcons_ReturnsEmpty()
    {
        Assert.Empty(D2IconCheck.ExtractIconUrls("x: hello\nx -> y: label"));
    }
}
