using System.Net;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests.DefinitionTransfer;

/// <summary>
/// An agent leaves one installation as a file and enters another with everything it needs: the happy path of the
/// definition transfer, across two hosts that share nothing.
/// </summary>
public sealed class DefinitionTransferRoundTripTests
{
    [Fact]
    public async Task An_agent_moves_with_its_skill_and_callee_and_a_second_import_changes_nothing()
    {
        await using var source = await TraconTestHost.StartAsync();
        await using var target = await TraconTestHost.StartAsync();

        await source.CreateSkillAsync("policy");
        await source.CreateAgentAsync(TestData.Request("worker", "Do the work."));
        await source.CreateAgentAsync(TestData.Request("triage", "Route the request.") with
        {
            SkillNames = ["policy"],
            CallableAgents = [CallableAgentReference.FromName("worker")],
        });
        await source.CreateAgentAsync(TestData.Request("unrelated", "Not part of the move."));

        var bundle = await source.ExportAsync("?agent=triage");

        bundle["format"]!.GetValue<string>().ShouldBe("tracon.definitions");
        bundle["formatVersion"]!.GetValue<int>().ShouldBe(1);
        bundle["agents"]!.AsArray().Select(agent => agent!["definition"]!["name"]!.GetValue<string>()).Order(StringComparer.Ordinal).ToArray()
            .ShouldBe(["triage", "worker"]);
        bundle["skills"]!.AsArray().Count.ShouldBe(1);

        // A dry run says what would happen and writes nothing.
        var dry = await target.ImportOkAsync(bundle, dryRun: true);

        dry["valid"]!.GetValue<bool>().ShouldBeTrue();
        dry["applied"]!.GetValue<bool>().ShouldBeFalse();
        TransferHost.Actions(dry, "agent").ShouldBe(["worker:create", "triage:create"]);
        TransferHost.Actions(dry, "skill").ShouldBe(["policy:create"]);
        (await target.AgentExistsAsync("triage")).ShouldBeFalse();

        var applied = await target.ImportOkAsync(bundle, dryRun: false);

        applied["applied"]!.GetValue<bool>().ShouldBeTrue();
        applied["written"]!.AsArray().Select(entry => entry!.GetValue<string>()).ToArray()
            .ShouldBe(["skill:policy", "agent:worker", "agent:triage"]);

        var echoed = await target.ExportAsync("?agent=triage");

        TransferHost.WithoutExportTime(echoed).ShouldBe(TransferHost.WithoutExportTime(bundle));

        // The same file again: every item is what the target already holds, so nothing is written.
        var again = await target.ImportOkAsync(bundle, dryRun: false);

        TransferHost.Actions(again, "agent").ShouldBe(["worker:unchanged", "triage:unchanged"]);
        TransferHost.Actions(again, "skill").ShouldBe(["policy:unchanged"]);
        again["written"]!.AsArray().Count.ShouldBe(0);
        (await target.AgentVersionCountAsync("triage")).ShouldBe(1);
    }

    [Fact]
    public async Task A_call_that_does_not_say_dryRun_writes_nothing()
    {
        await using var source = await TraconTestHost.StartAsync();
        await using var target = await TraconTestHost.StartAsync();

        await source.CreateAgentAsync(TestData.Request("only", "Alone."));

        var (response, body) = await target.ImportAsync(await source.ExportAsync());

        using (response)
        {
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        body!["dryRun"]!.GetValue<bool>().ShouldBeTrue();
        (await target.AgentExistsAsync("only")).ShouldBeFalse();
    }
}
