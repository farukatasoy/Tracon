using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using Tracon.Tests.Common;

namespace Tracon.AspNetCore.FunctionalTests.DefinitionTransfer;

/// <summary>
/// Validation is complete before the first write, the audit trail records an apply and only an apply, and what
/// happens when a store, a connection or a second import gets in the way.
/// </summary>
public sealed class DefinitionImportAtomicityTests
{
    [Fact]
    public async Task A_file_with_one_refused_item_writes_nothing_and_leaves_no_audit_line()
    {
        await using var target = await TraconTestHost.StartAsync();

        var bundle = TransferHost.NewBundle()
            .WithSkill("fine-skill")
            .WithAgent(TestData.Request("fine", "Fine."))
            .WithAgent(TestData.Request("last", "Refused, and written last.") with { ToolNames = ["nope"] });

        var before = await AuditCountAsync(target);

        var dry = await target.ImportOkAsync(bundle, dryRun: true);

        dry["valid"]!.GetValue<bool>().ShouldBeFalse();
        TransferHost.Actions(dry, "agent").ShouldBe(["fine:create", "last:rejected"]);

        var (response, body) = await target.ImportAsync(bundle, dryRun: false);

        using (response)
        {
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            body!["written"]!.AsArray().Count.ShouldBe(0);
        }

        // The agent and the skill that were fine are not in the target: one refusal stops the whole file.
        (await target.AgentExistsAsync("fine")).ShouldBeFalse();

        using (var skills = await target.Client.GetAsync(TransferHost.Api("skills")))
        {
            (await TraconTestHost.ReadJsonAsync(skills)).GetArrayLength().ShouldBe(0);
        }

        (await AuditCountAsync(target)).ShouldBe(before);
    }

    [Fact]
    public async Task A_dry_run_writes_no_audit_line()
    {
        await using var target = await TraconTestHost.StartAsync();

        var before = await AuditCountAsync(target);

        await target.ImportOkAsync(TransferHost.NewBundle().WithAgent(TestData.Request("fine", "Fine.")), dryRun: true);

        (await AuditCountAsync(target)).ShouldBe(before);
    }

    [Fact]
    public async Task An_applied_import_writes_a_summary_with_counts_and_a_hash_and_never_the_content()
    {
        await using var target = await TraconTestHost.StartAsync();

        var bundle = TransferHost.NewBundle()
            .WithSkill("policy", "Secret skill wording.")
            .WithAgent(TestData.Request("fine", "Secret instruction wording."));

        await target.ImportOkAsync(bundle, dryRun: false);

        using var response = await target.Client.GetAsync(TransferHost.Api("audit?limit=100"));
        var entries = (await TraconTestHost.ReadJsonAsync(response)).EnumerateArray().ToList();
        var summary = entries.Single(static entry => string.Equals(entry.GetProperty("action").GetString(), "definitions.import", StringComparison.Ordinal));
        var after = summary.GetProperty("after").GetString()!;

        after.ShouldContain("sha256");
        after.ShouldContain("\"agents\":1");
        after.ShouldContain("\"skills\":1");
        after.ShouldNotContain("Secret");

        // The stores' own lines are there too: the import goes through the same save the endpoints use.
        entries.ShouldContain(static entry => entry.GetProperty("action").GetString()!.StartsWith("agent", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_store_that_fails_part_way_leaves_what_it_wrote_and_says_so()
    {
        await using var target = await TraconTestHost.StartAsync(
            configureTracon: static builder => builder.Services.ThrowFrom<IAgentDefinitionStore>(
                nameof(IAgentDefinitionStore.SaveAsync),
                static () => new InvalidOperationException("connection string leaked here"),
                times: 1,
                when: static arguments => arguments[0] is AgentDefinition { Name: "second" }));

        var bundle = TransferHost.NewBundle()
            .WithAgent(TestData.Request("first", "One."))
            .WithAgent(TestData.Request("second", "Two."));

        var (response, body) = await target.ImportAsync(bundle, dryRun: false);

        using (response)
        {
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        body!["applied"]!.GetValue<bool>().ShouldBeFalse();
        body["partial"]!.GetValue<bool>().ShouldBeTrue();
        body["written"]!.AsArray().Select(entry => entry!.GetValue<string>()).ToArray().ShouldBe(["agent:first"]);

        var failed = TransferHost.Item(body, "agent", "second");
        var message = failed["messages"]!.AsArray().Single()!;

        message["code"]!.GetValue<string>().ShouldBe("import_store_failed");
        message["message"]!.GetValue<string>().ShouldNotContain("connection string");

        (await target.AgentExistsAsync("first")).ShouldBeTrue();
        (await target.AgentExistsAsync("second")).ShouldBeFalse();

        // The fault is spent. Run again: the agent that was written reads as unchanged, and the rest is written.
        var again = await target.ImportOkAsync(bundle, dryRun: false);

        TransferHost.Actions(again, "agent").ShouldBe(["first:unchanged", "second:create"]);
        again["applied"]!.GetValue<bool>().ShouldBeTrue();
        (await target.AgentVersionCountAsync("first")).ShouldBe(1);
    }

    [Fact]
    public async Task A_dropped_connection_does_not_stop_a_write_that_has_started()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var target = await TraconTestHost.StartAsync(
            configureTracon: builder => builder.Services.HoldBefore<IAgentDefinitionStore>(
                nameof(IAgentDefinitionStore.SaveAsync),
                arguments => arguments[0] is AgentDefinition { Name: "second" },
                reached,
                gate));

        var bundle = TransferHost.NewBundle()
            .WithAgent(TestData.Request("first", "One."))
            .WithAgent(TestData.Request("second", "Two."));

        using var dropped = new CancellationTokenSource();

        try
        {
            var import = target.Client.PostAsync(
                TransferHost.Api("definitions/import?dryRun=false"),
                new StringContent(bundle.ToJsonString(), Encoding.UTF8, "application/json"),
                dropped.Token);

            // The first item is written and the second is on its way; now the caller goes away.
            await reached.Task.WaitAsync(TimeSpan.FromSeconds(20));
            await dropped.CancelAsync();

            // The write is let go before the caller's call is awaited: the test server may not end that call until the
            // server side is done with the request.
            gate.TrySetResult();

            try
            {
                using var abandoned = await import;
            }
            catch (OperationCanceledException)
            {
                // The caller's call ends; the import it started does not.
            }
        }
        finally
        {
            // A test that fails above must not leave the write waiting: the host would wait for it when it is disposed.
            gate.TrySetResult();
        }

        await WaitUntil.ValueAsync(
            async () => await target.AgentExistsAsync("second"),
            static exists => exists,
            "the second agent to be written after the caller dropped the connection");

        // The summary line is written too, once the work is done.
        await WaitUntil.ValueAsync(
            async () => await AuditActionsAsync(target),
            static actions => actions.Contains("definitions.import", StringComparer.Ordinal),
            "the import summary to be audited");
    }

    [Fact]
    public async Task Two_imports_that_run_together_are_both_written_and_the_history_stays_consistent()
    {
        await using var target = await TraconTestHost.StartAsync();

        var one = TransferHost.NewBundle().WithAgent(TestData.Request("shared", "Version from one."));
        var two = TransferHost.NewBundle().WithAgent(TestData.Request("shared", "Version from two."));

        var results = await Task.WhenAll(target.ImportAsync(one, dryRun: false), target.ImportAsync(two, dryRun: false));

        foreach (var (response, _) in results)
        {
            using (response)
            {
                response.StatusCode.ShouldBe(HttpStatusCode.OK);
            }
        }

        // There is no lock: each import wrote its own version, and the two versions are both in the history.
        (await target.AgentVersionCountAsync("shared")).ShouldBe(2);
    }

    private static async Task<int> AuditCountAsync(TraconTestHost host)
        => (await AuditActionsAsync(host)).Count;

    private static async Task<List<string>> AuditActionsAsync(TraconTestHost host)
    {
        using var response = await host.Client.GetAsync(TransferHost.Api("audit?limit=500"));

        return [.. (await TraconTestHost.ReadJsonAsync(response)).EnumerateArray().Select(static entry => entry.GetProperty("action").GetString()!)];
    }
}
