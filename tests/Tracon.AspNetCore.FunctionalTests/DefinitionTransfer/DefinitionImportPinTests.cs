using System.Text.Json.Nodes;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests.DefinitionTransfer;

/// <summary>
/// A version pin names the version the SOURCE host gave an agent. In the target that number can mean other
/// content, so the import translates it to the version the target holds, or refuses it.
/// </summary>
public sealed class DefinitionImportPinTests
{
    [Fact]
    public async Task A_callable_pin_follows_the_agent_to_the_version_the_target_gives_it()
    {
        await using var source = await TraconTestHost.StartAsync();
        await using var target = await TraconTestHost.StartAsync();

        // The source's "x" is at version 4, and "a" pins it there.
        await source.CreateAgentAsync(TestData.Request("x", "x one"));
        await source.UpdateAgentAsync(TestData.Request("x", "x two"));
        await source.UpdateAgentAsync(TestData.Request("x", "x three"));
        await source.UpdateAgentAsync(TestData.Request("x", "x four"));
        await source.CreateAgentAsync(TestData.Request("a", "uses x") with
        {
            CallableAgents = [new CallableAgentReference { Name = "x", Version = 4 }],
        });

        // The target's "x" is another agent at version 1: "x@4" would mean nothing there.
        await target.CreateAgentAsync(TestData.Request("x", "something else entirely"));

        var bundle = await source.ExportAsync("?agent=a");
        var dry = await target.ImportOkAsync(bundle, dryRun: true);

        dry["valid"]!.GetValue<bool>().ShouldBeTrue(dry.ToJsonString());

        var x = TransferHost.Item(dry, "agent", "x");

        x["action"]!.GetValue<string>().ShouldBe("newVersion");
        x["sourceVersion"]!.GetValue<int>().ShouldBe(4);
        x["targetVersion"]!.GetValue<int>().ShouldBe(2);

        await target.ImportOkAsync(bundle, dryRun: false);

        (await PinOfAsync(target, "a")).ShouldBe(2);
        (await target.AgentVersionCountAsync("x")).ShouldBe(2);
    }

    [Fact]
    public async Task A_pin_to_an_agent_the_target_already_holds_the_same_follows_the_version_it_has()
    {
        await using var source = await TraconTestHost.StartAsync();
        await using var target = await TraconTestHost.StartAsync();

        await source.CreateAgentAsync(TestData.Request("x", "x one"));
        await source.UpdateAgentAsync(TestData.Request("x", "x two"));
        await source.CreateAgentAsync(TestData.Request("a", "uses x") with
        {
            CallableAgents = [new CallableAgentReference { Name = "x", Version = 2 }],
        });

        // The target holds the very content the source's current "x" has, as its only version.
        await target.CreateAgentAsync(TestData.Request("x", "x two"));

        var bundle = await source.ExportAsync("?agent=a");
        var dry = await target.ImportOkAsync(bundle, dryRun: true);

        var x = TransferHost.Item(dry, "agent", "x");

        x["action"]!.GetValue<string>().ShouldBe("unchanged");
        x["targetVersion"]!.GetValue<int>().ShouldBe(1);

        await target.ImportOkAsync(bundle, dryRun: false);

        (await PinOfAsync(target, "a")).ShouldBe(1);
        (await target.AgentVersionCountAsync("x")).ShouldBe(1);
    }

    [Fact]
    public async Task A_reviewer_pin_and_a_workflow_node_pin_are_translated_too()
    {
        await using var source = await TraconTestHost.StartAsync();
        await using var target = await TraconTestHost.StartAsync();

        await source.CreateAgentAsync(TestData.Request("rev", "review one"));
        await source.UpdateAgentAsync(TestData.Request("rev", "review two"));
        await source.CreateAgentAsync(WriterWithReviewer(reviewerVersion: 2));
        await source.CreateWorkflowAsync("flow", new JsonObject
        {
            ["kind"] = "Sequential",
            ["nodes"] = new JsonArray(new JsonObject { ["name"] = "rev", ["kind"] = "Agent", ["agentVersion"] = 2 }),
        });

        await target.CreateAgentAsync(TestData.Request("rev", "a reviewer of the target"));

        var bundle = await source.ExportAsync("?agent=writer&workflow=flow");

        await target.ImportOkAsync(bundle, dryRun: false);

        // The target's "rev" is now at version 2, and both pins say so.
        using var writer = await target.Client.GetAsync(TransferHost.Api("agents/writer"));
        var criterion = (await TraconTestHost.ReadJsonAsync(writer))
            .GetProperty("definition").GetProperty("harness").GetProperty("loop").GetProperty("criteria")[0];

        criterion.GetProperty("reviewerAgentVersion").GetInt32().ShouldBe(2);

        using var flow = await target.Client.GetAsync(TransferHost.Api("workflows/flow"));

        (await TraconTestHost.ReadJsonAsync(flow)).GetProperty("nodes")[0].GetProperty("agentVersion").GetInt32().ShouldBe(2);
    }

    [Fact]
    public async Task A_pin_the_file_cannot_translate_stops_the_import_and_nothing_is_written()
    {
        await using var target = await TraconTestHost.StartAsync();

        // The file holds "x" at source version 4, "a" pins 2, and "b" pins an agent the file does not hold.
        var bundle = TransferHost.NewBundle()
            .WithAgent(TestData.Request("x", "x four"), sourceVersion: 4)
            .WithAgent(
                TestData.Request("a", "pins the wrong version") with
                {
                    CallableAgents = [new CallableAgentReference { Name = "x", Version = 2 }],
                })
            .WithAgent(
                TestData.Request("b", "pins an outsider") with
                {
                    CallableAgents = [new CallableAgentReference { Name = "x" }, new CallableAgentReference { Name = "outsider", Version = 1 }],
                });

        var dry = await target.ImportOkAsync(bundle, dryRun: true);

        dry["valid"]!.GetValue<bool>().ShouldBeFalse();

        foreach (var name in new[] { "a", "b" })
        {
            var item = TransferHost.Item(dry, "agent", name);

            item["action"]!.GetValue<string>().ShouldBe("rejected");
            item["messages"]!.AsArray()
                .Select(message => message!["code"]!.GetValue<string>())
                .ShouldContain(static code => string.Equals(code, "import_pin_untranslatable", StringComparison.Ordinal));
        }

        var (response, _) = await target.ImportAsync(bundle, dryRun: false);

        using (response)
        {
            response.StatusCode.ShouldBe(System.Net.HttpStatusCode.BadRequest);
        }

        (await target.AgentExistsAsync("x")).ShouldBeFalse();
        (await target.AgentExistsAsync("a")).ShouldBeFalse();
    }

    private static async Task<int> PinOfAsync(TraconTestHost host, string agent)
    {
        using var response = await host.Client.GetAsync(TransferHost.Api($"agents/{agent}"));

        return (await TraconTestHost.ReadJsonAsync(response))
            .GetProperty("definition").GetProperty("callableAgents")[0].GetProperty("version").GetInt32();
    }

    private static AgentDefinitionRequest WriterWithReviewer(int reviewerVersion)
        => TestData.Request("writer", "Write.") with
        {
            Harness = new HarnessSettings
            {
                Loop = new LoopSettings
                {
                    MaxIterations = 2,
                    Criteria =
                    [
                        new LoopCriterion { Kind = "agentReview", ReviewerAgent = "rev", ReviewerAgentVersion = reviewerVersion },
                    ],
                },
            },
        };
}
