using System.Text;
using System.Text.Json;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests.DefinitionTransfer;

/// <summary>
/// A file may chain as many agents as its size allows, each needing the next. Walking such a chain must not
/// spend the stack: a stack overflow ends the process, and the dry run that reaches it needs only the Operator role.
/// </summary>
public sealed class DefinitionImportDepthTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Ordering_a_chain_far_deeper_than_any_stack_puts_the_deepest_agent_first()
    {
        const int Links = 200_000;

        var agents = new Dictionary<string, AgentDefinition>(Links, StringComparer.Ordinal);

        for (var index = 0; index < Links; index++)
        {
            agents[$"a{index}"] = TestData.Definition($"a{index}") with
            {
                SharedInstructionsName = index + 1 < Links ? $"a{index + 1}" : null,
            };
        }

        var ordered = DefinitionReferenceGraph.OrderAgents(agents);

        ordered.Count.ShouldBe(Links);
        ordered[0].ShouldBe($"a{Links - 1}");
        ordered[^1].ShouldBe("a0");
    }

    [Fact]
    public void Ordering_keeps_callees_before_callers_and_the_file_order_among_the_unrelated()
    {
        var agents = new Dictionary<string, AgentDefinition>(StringComparer.Ordinal)
        {
            ["top"] = TestData.Definition("top") with { SharedInstructionsName = "middle" },
            ["lone"] = TestData.Definition("lone"),
            ["middle"] = TestData.Definition("middle") with { SharedInstructionsName = "bottom" },
            ["bottom"] = TestData.Definition("bottom"),
        };

        DefinitionReferenceGraph.OrderAgents(agents).ShouldBe(["bottom", "middle", "top", "lone"]);
    }

    [Fact]
    public async Task A_dry_run_of_the_longest_chain_the_size_limit_allows_is_answered_not_fatal()
    {
        var template = JsonSerializer.Serialize(
            TestData.Request("AGENTNAME") with { SharedInstructionsName = "SHAREDNAME" },
            Web);

        var count = 7_500_000 / (template.Length + 40);

        var body = new StringBuilder(8_000_000);
        body.Append("{\"format\":\"tracon.definitions\",\"formatVersion\":1,\"exportedAt\":\"2026-10-29T12:00:00Z\",\"agents\":[");

        for (var index = 0; index < count; index++)
        {
            if (index > 0)
            {
                body.Append(',');
            }

            body.Append("{\"sourceVersion\":1,\"definition\":")
                .Append(template
                    .Replace("AGENTNAME", $"a{index}", StringComparison.Ordinal)
                    .Replace("SHAREDNAME", $"a{index + 1}", StringComparison.Ordinal))
                .Append('}');
        }

        body.Append("],\"workflows\":[],\"skills\":[],\"mcpServers\":[]}");

        await using var host = await TraconTestHost.StartAsync();

        using var response = await host.Client.PostAsync(
            TransferHost.Api("definitions/import"),
            new StringContent(body.ToString(), Encoding.UTF8, "application/json"),
            TestContext.Current.CancellationToken);

        ((int)response.StatusCode).ShouldBeLessThan(500);
    }
}
