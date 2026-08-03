using System.Security.Cryptography;
using System.Text;
using ModelContextProtocol.Protocol;

namespace AgentPrism.Mcp.UnitTests;

public sealed class McpPromptSnapshotTests
{
    [Fact]
    public void Mesajlar_birlestirilir()
    {
        var result = new GetPromptResult
        {
            Messages =
            [
                new PromptMessage { Role = Role.User, Content = new TextContentBlock { Text = "birinci" } },
                new PromptMessage { Role = Role.User, Content = new TextContentBlock { Text = "ikinci" } },
            ],
        };

        var snapshot = McpPromptSnapshot.Build(result);

        snapshot.Text.ShouldBe("birinci\n\nikinci");
    }

    [Fact]
    public void Hash_icerikle_tutarlidir()
    {
        var result = new GetPromptResult
        {
            Messages = [new PromptMessage { Role = Role.User, Content = new TextContentBlock { Text = "sabit metin" } }],
        };

        var snapshot = McpPromptSnapshot.Build(result);

        var expectedHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("sabit metin")));

        snapshot.Hash.ShouldBe(expectedHash);
    }

    [Fact]
    public void Ayni_icerik_ayni_hash_uretir()
    {
        var first = McpPromptSnapshot.Build(new GetPromptResult
        {
            Messages = [new PromptMessage { Role = Role.User, Content = new TextContentBlock { Text = "ayni" } }],
        });

        var second = McpPromptSnapshot.Build(new GetPromptResult
        {
            Messages = [new PromptMessage { Role = Role.User, Content = new TextContentBlock { Text = "ayni" } }],
        });

        first.Hash.ShouldBe(second.Hash);
    }

    [Fact]
    public void Farkli_icerik_farkli_hash_uretir()
    {
        var first = McpPromptSnapshot.Build(new GetPromptResult
        {
            Messages = [new PromptMessage { Role = Role.User, Content = new TextContentBlock { Text = "birinci surum" } }],
        });

        var second = McpPromptSnapshot.Build(new GetPromptResult
        {
            Messages = [new PromptMessage { Role = Role.User, Content = new TextContentBlock { Text = "ikinci surum" } }],
        });

        string.Equals(first.Hash, second.Hash, StringComparison.Ordinal).ShouldBeFalse();
    }
}
