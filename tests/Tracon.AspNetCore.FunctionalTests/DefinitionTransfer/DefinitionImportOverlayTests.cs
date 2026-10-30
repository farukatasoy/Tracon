using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests.DefinitionTransfer;

/// <summary>
/// The file is validated as if it were already saved, and a dry run is invisible to everything that is not the
/// report.
/// </summary>
public sealed class DefinitionImportOverlayTests
{
    [Fact]
    public async Task A_reference_inside_the_file_is_judged_as_if_the_file_were_saved()
    {
        await using var target = await TraconTestHost.StartAsync();

        // The new skill, the block of shared instructions and the agent the caller names exist only in the file.
        var bundle = TransferHost.NewBundle()
            .WithSkill("policy")
            .WithAgent(TestData.Request("house-style", "Always be polite."))
            .WithAgent(TestData.Request("worker", "Work.") with { SkillNames = ["policy"], SharedInstructionsName = "house-style" })
            .WithAgent(TestData.Request("lead", "Lead.") with { CallableAgents = [CallableAgentReference.FromName("worker")] });

        var dry = await target.ImportOkAsync(bundle, dryRun: true);

        dry["valid"]!.GetValue<bool>().ShouldBeTrue(dry.ToJsonString());
        TransferHost.Actions(dry, "agent").ShouldBe(["house-style:create", "worker:create", "lead:create"]);

        var applied = await target.ImportOkAsync(bundle, dryRun: false);

        applied["applied"]!.GetValue<bool>().ShouldBeTrue();

        // Written, the three compile: a run needs the callee, the skill and the shared block to be real.
        foreach (var agent in new[] { "worker", "lead" })
        {
            using var run = await target.Client.PostAsJsonAsync(
                TransferHost.Api($"agents/{agent}/run"),
                new AgentRunRequest { Message = "hello" });

            run.StatusCode.ShouldBe(HttpStatusCode.OK, agent);
        }
    }

    [Fact]
    public async Task A_reference_to_nothing_the_file_or_the_target_holds_is_still_refused()
    {
        await using var target = await TraconTestHost.StartAsync();

        var bundle = TransferHost.NewBundle()
            .WithAgent(TestData.Request("worker", "Work.") with { SkillNames = ["absent"] });

        var dry = await target.ImportOkAsync(bundle, dryRun: true);

        dry["valid"]!.GetValue<bool>().ShouldBeFalse();
        TransferHost.Item(dry, "agent", "worker")["messages"]!.AsArray()
            .Select(message => message!["code"]!.GetValue<string>())
            .ShouldContain(static code => string.Equals(code, "unknown_skill", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_dry_run_reaches_neither_the_catalog_nor_the_run_surface()
    {
        await using var target = await TraconTestHost.StartAsync();

        await target.CreateAgentAsync(TestData.Request("stable", "old"));

        var bundle = TransferHost.NewBundle()
            .WithSkill("fresh-skill")
            .WithAgent(TestData.Request("stable", "new"))
            .WithAgent(TestData.Request("fresh", "Fresh.") with { SkillNames = ["fresh-skill"] });

        var dry = await target.ImportOkAsync(bundle, dryRun: true);

        TransferHost.Actions(dry, "agent").ShouldBe(["stable:newVersion", "fresh:create"]);

        // The agent that is new does not exist: not in the catalog, not on the run surface.
        (await target.AgentExistsAsync("fresh")).ShouldBeFalse();

        using (var run = await target.Client.PostAsJsonAsync(
                   TransferHost.Api("agents/fresh/run"),
                   new AgentRunRequest { Message = "hello" }))
        {
            run.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }

        using (var list = await target.Client.GetAsync(TransferHost.Api("agents")))
        {
            (await TraconTestHost.ReadJsonAsync(list)).EnumerateArray()
                .Select(static agent => agent.GetProperty("name").GetString())
                .ShouldNotContain(static name => string.Equals(name, "fresh", StringComparison.Ordinal));
        }

        using (var skills = await target.Client.GetAsync(TransferHost.Api("skills")))
        {
            (await TraconTestHost.ReadJsonAsync(skills)).GetArrayLength().ShouldBe(0);
        }

        // The agent that exists keeps its content and its history.
        using (var detail = await target.Client.GetAsync(TransferHost.Api("agents/stable")))
        {
            var body = await TraconTestHost.ReadJsonAsync(detail);

            body.GetProperty("definition").GetProperty("instructions").GetString().ShouldBe("old");
        }

        (await target.AgentVersionCountAsync("stable")).ShouldBe(1);
    }

    [Fact]
    public async Task A_dry_run_does_not_change_what_the_cached_agent_tells_the_model()
    {
        // The compiled agent of "stable" is in the cache after its first run. A dry run that compiled the file's version
        // through that path would leave the NEW instructions behind, and the next run would send them to the model.
        var probe = new Tracon.Testing.FakeModelProvider("probe").EchoesUserMessage();

        await using var target = await TraconTestHost.StartAsync(builder => builder.AddModelProvider(probe));

        var stable = new AgentDefinitionRequest
        {
            Name = "stable",
            Instructions = "OLD-WORDING",
            Model = new ModelBinding { Provider = "probe", Model = "probe-1" },
        };

        await target.CreateAgentAsync(stable);
        await RunAsync(target, "stable");

        var dry = await target.ImportOkAsync(
            TransferHost.NewBundle().WithAgent(stable with { Instructions = "NEW-WORDING" }),
            dryRun: true);

        TransferHost.Actions(dry, "agent").ShouldBe(["stable:newVersion"]);

        await RunAsync(target, "stable");

        var seen = SentToModel(probe);

        seen.ShouldContain("OLD-WORDING");
        seen.ShouldNotContain("NEW-WORDING");

        // Applied, the same path does pick the new version up: the probe sees instructions, so this is not a vacuous check.
        await target.ImportOkAsync(TransferHost.NewBundle().WithAgent(stable with { Instructions = "NEW-WORDING" }), dryRun: false);
        await RunAsync(target, "stable");

        SentToModel(probe).ShouldContain("NEW-WORDING");
    }

    private static async Task RunAsync(TraconTestHost host, string agent)
    {
        using var response = await host.Client.PostAsJsonAsync(
            TransferHost.Api($"agents/{agent}/run"),
            new AgentRunRequest { Message = "hello" });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        // The run is a stream: reading it to the end is what finishes the run.
        _ = await response.Content.ReadAsStringAsync();
    }

    private static string SentToModel(Tracon.Testing.FakeModelProvider probe)
    {
        var request = probe.Requests[^1];

        return string.Join(" ", request.Messages.Select(static message => message.Text)) + " " + request.Options?.Instructions;
    }
}
