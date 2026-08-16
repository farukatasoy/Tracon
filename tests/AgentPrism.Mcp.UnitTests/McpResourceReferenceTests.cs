namespace AgentPrism.Mcp.UnitTests;

public sealed class McpResourceReferenceTests
{
    [Fact]
    public void Simple_reference_is_parsed()
    {
        McpResourceReference.TryParse("github:https://example.com/readme", out var server, out var uri).ShouldBeTrue();

        server.ShouldBe("github");
        uri.ShouldBe("https://example.com/readme");
    }

    [Fact]
    public void Uri_can_carry_a_colon_of_its_own()
    {
        // The server name only contains [a-zA-Z0-9_-]; the first ':' is a safe separator.
        McpResourceReference.TryParse("docs:file:///var/data/readme.md", out var server, out var uri).ShouldBeTrue();

        server.ShouldBe("docs");
        uri.ShouldBe("file:///var/data/readme.md");
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData(":empty-server-name")]
    [InlineData("empty-uri:")]
    [InlineData("")]
    public void Invalid_format_is_rejected(string reference)
    {
        McpResourceReference.TryParse(reference, out _, out _).ShouldBeFalse();
    }
}
