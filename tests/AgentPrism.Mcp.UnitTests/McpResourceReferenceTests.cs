namespace AgentPrism.Mcp.UnitTests;

public sealed class McpResourceReferenceTests
{
    [Fact]
    public void Basit_referans_ayristirilir()
    {
        McpResourceReference.TryParse("github:https://example.com/readme", out var server, out var uri).ShouldBeTrue();

        server.ShouldBe("github");
        uri.ShouldBe("https://example.com/readme");
    }

    [Fact]
    public void Uri_kendi_icinde_kolon_tasiyabilir()
    {
        // Sunucu adi yalniz [a-zA-Z0-9_-] icerir; ilk ':' guvenli ayiricidir.
        McpResourceReference.TryParse("docs:file:///var/data/readme.md", out var server, out var uri).ShouldBeTrue();

        server.ShouldBe("docs");
        uri.ShouldBe("file:///var/data/readme.md");
    }

    [Theory]
    [InlineData("gecersiz")]
    [InlineData(":bos-sunucu-adi")]
    [InlineData("bos-uri:")]
    [InlineData("")]
    public void Gecersiz_bicim_reddedilir(string reference)
    {
        McpResourceReference.TryParse(reference, out _, out _).ShouldBeFalse();
    }
}
