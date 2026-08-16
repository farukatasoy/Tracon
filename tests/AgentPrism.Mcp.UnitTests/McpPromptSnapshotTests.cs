using System.Security.Cryptography;
using System.Text;
using ModelContextProtocol.Protocol;

namespace AgentPrism.Mcp.UnitTests;

public sealed class McpPromptSnapshotTests
{
    [Fact]
    public void Messages_are_joined()
    {
        var result = new GetPromptResult
        {
            Messages =
            [
                new PromptMessage { Role = Role.User, Content = new TextContentBlock { Text = "first" } },
                new PromptMessage { Role = Role.User, Content = new TextContentBlock { Text = "second" } },
            ],
        };

        var snapshot = McpPromptSnapshot.Build(result);

        snapshot.Text.ShouldBe("first\n\nsecond");
    }

    [Fact]
    public void Hash_is_consistent_with_the_content()
    {
        var result = new GetPromptResult
        {
            Messages = [new PromptMessage { Role = Role.User, Content = new TextContentBlock { Text = "fixed text" } }],
        };

        var snapshot = McpPromptSnapshot.Build(result);

        var expectedHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("fixed text")));

        snapshot.Hash.ShouldBe(expectedHash);
    }

    [Fact]
    public void Same_content_produces_the_same_hash()
    {
        var first = McpPromptSnapshot.Build(new GetPromptResult
        {
            Messages = [new PromptMessage { Role = Role.User, Content = new TextContentBlock { Text = "same" } }],
        });

        var second = McpPromptSnapshot.Build(new GetPromptResult
        {
            Messages = [new PromptMessage { Role = Role.User, Content = new TextContentBlock { Text = "same" } }],
        });

        first.Hash.ShouldBe(second.Hash);
    }

    [Fact]
    public void Different_content_produces_a_different_hash()
    {
        var first = McpPromptSnapshot.Build(new GetPromptResult
        {
            Messages = [new PromptMessage { Role = Role.User, Content = new TextContentBlock { Text = "first version" } }],
        });

        var second = McpPromptSnapshot.Build(new GetPromptResult
        {
            Messages = [new PromptMessage { Role = Role.User, Content = new TextContentBlock { Text = "second version" } }],
        });

        string.Equals(first.Hash, second.Hash, StringComparison.Ordinal).ShouldBeFalse();
    }
}
