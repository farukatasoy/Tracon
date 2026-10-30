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
}
