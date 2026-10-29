namespace Tracon.Mcp.UnitTests;

/// <summary>
/// A server name or a tool name that ends in a line feed is not a name (phase 260, F-756).
/// </summary>
/// <remarks>
/// The pattern was <c>^[a-zA-Z0-9_-]+$</c>, and .NET's <c>$</c> also matches before a trailing <c>\n</c>. The
/// qualified name <c>{server}_{tool}</c> goes to a model provider as a function name; a provider that accepts
/// only <c>[a-zA-Z0-9_-]</c> rejects a name with a line feed at call time, long after the registration passed.
/// Each case pairs the bad value with the same value without the line feed.
/// </remarks>
public sealed class McpToolNamingTests
{
    [Theory]
    [InlineData("github")]
    [InlineData("Docs_Server-2")]
    public void A_server_name_is_valid_without_the_line_feed_and_not_with_it(string name)
    {
        McpToolNaming.IsValidServerName(name).ShouldBeTrue();
        McpToolNaming.IsValidServerName(name + "\n").ShouldBeFalse();
        McpToolNaming.IsValidServerName(name + "\r\n").ShouldBeFalse();
    }

    [Theory]
    [InlineData("search_issues")]
    [InlineData("Get-Repo")]
    public void A_tool_name_is_qualified_without_the_line_feed_and_not_with_it(string tool)
    {
        McpToolNaming.TryQualify("github", tool).ShouldBe($"github_{tool}");
        McpToolNaming.TryQualify("github", tool + "\n").ShouldBeNull();
        McpToolNaming.TryQualify("github", tool + "\r\n").ShouldBeNull();
    }
}
