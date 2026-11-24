using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
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

    /// <summary>
    /// A name that was saved and deleted continues its numbering, so a "new" agent is not at version 1. The dry run
    /// used to say 1 while the write gave the next number; the pins that point at it followed the wrong number too.
    /// </summary>
    [Fact]
    public async Task A_dry_run_names_the_version_a_deleted_name_will_get_and_the_write_gives_exactly_that()
    {
        await using var source = await TraconTestHost.StartAsync();
        await using var target = await TraconTestHost.StartAsync();

        await source.CreateAgentAsync(TestData.Request("x", "x one"));
        await source.CreateAgentAsync(TestData.Request("a", "uses x") with
        {
            CallableAgents = [new CallableAgentReference { Name = "x", Version = 1 }],
        });

        // The target saved "x" twice and deleted it: its numbering is at 2.
        await target.CreateAgentAsync(TestData.Request("x", "an old era"));
        await target.UpdateAgentAsync(TestData.Request("x", "an old era, changed"));
        using (var deleted = await target.Client.DeleteAsync(TransferHost.Api("agents/x")))
        {
            deleted.IsSuccessStatusCode.ShouldBeTrue();
        }

        var bundle = await source.ExportAsync("?agent=a");
        var dry = await target.ImportOkAsync(bundle, dryRun: true);

        dry["valid"]!.GetValue<bool>().ShouldBeTrue(dry.ToJsonString());

        var forecast = TransferHost.Item(dry, "agent", "x");

        forecast["action"]!.GetValue<string>().ShouldBe("create");
        forecast["targetVersion"]!.GetValue<int>().ShouldBe(3);

        await target.ImportOkAsync(bundle, dryRun: false);

        using var written = await target.Client.GetAsync(TransferHost.Api("agents/x"));

        (await TraconTestHost.ReadJsonAsync(written)).GetProperty("definition").GetProperty("version").GetInt32().ShouldBe(3);
        (await PinOfAsync(target, "a")).ShouldBe(3);
    }

    [Fact]
    public async Task A_dry_run_names_the_version_a_deleted_workflow_will_get()
    {
        await using var target = await TraconTestHost.StartAsync();

        await target.CreateAgentAsync(TestData.Request("rev", "reviewer"));
        await target.CreateWorkflowAsync("flow", TransferHost.SequentialWorkflow("rev"));
        await target.CreateWorkflowAsync("flow", TransferHost.SequentialWorkflow("rev"));

        using (var deleted = await target.Client.DeleteAsync(TransferHost.Api("workflows/flow")))
        {
            deleted.IsSuccessStatusCode.ShouldBeTrue();
        }

        var bundle = TransferHost.NewBundle()
            .WithAgent(TestData.Request("rev", "reviewer"))
            .WithWorkflow("flow", TransferHost.SequentialWorkflow("rev"));

        var dry = await target.ImportOkAsync(bundle, dryRun: true);

        var forecast = TransferHost.Item(dry, "workflow", "flow");

        forecast["action"]!.GetValue<string>().ShouldBe("create");
        forecast["targetVersion"]!.GetValue<int>().ShouldBe(3);

        await target.ImportOkAsync(bundle, dryRun: false);

        using var written = await target.Client.GetAsync(TransferHost.Api("workflows/flow"));

        (await TraconTestHost.ReadJsonAsync(written)).GetProperty("version").GetInt32().ShouldBe(3);
    }

    /// <summary>
    /// A store written before the forecast existed answers "unknown". The dry run says so in the report and carries
    /// on: it neither crashes nor names a number the store never gave.
    /// </summary>
    [Fact]
    public async Task A_store_that_gives_no_forecast_leaves_the_target_version_empty_and_the_write_still_works()
    {
        await using var target = await TraconTestHost.StartAsync(
            configureServices: static services => services.AddSingleton<IAgentDefinitionStore>(static _ => new ForecastlessAgentStore()));

        var bundle = TransferHost.NewBundle().WithAgent(TestData.Request("x", "x one"));
        var dry = await target.ImportOkAsync(bundle, dryRun: true);

        dry["valid"]!.GetValue<bool>().ShouldBeTrue(dry.ToJsonString());

        var forecast = TransferHost.Item(dry, "agent", "x");

        forecast["action"]!.GetValue<string>().ShouldBe("create");
        forecast["targetVersion"].ShouldBeNull();
        forecast["messages"]!.AsArray().ShouldContain(static message =>
            string.Equals(message!["code"]!.GetValue<string>(), "import_target_version_unknown", StringComparison.Ordinal)
            && string.Equals(message["severity"]!.GetValue<string>(), "Warning", StringComparison.OrdinalIgnoreCase));

        await target.ImportOkAsync(bundle, dryRun: false);

        (await target.AgentVersionCountAsync("x")).ShouldBe(1);
    }

    /// <summary>
    /// A dry run that cannot forecast carries the pin as 1 for want of a number; the write numbers the callee and
    /// translates the pin of the agent that points at it with the number the store really gave.
    /// </summary>
    [Fact]
    public async Task Without_a_forecast_the_write_still_translates_the_pin_to_the_number_the_store_gave()
    {
        await using var source = await TraconTestHost.StartAsync();
        await using var target = await TraconTestHost.StartAsync(
            configureServices: static services => services.AddSingleton<IAgentDefinitionStore>(static _ => new ForecastlessAgentStore()));

        await source.CreateAgentAsync(TestData.Request("x", "x one"));
        await source.CreateAgentAsync(TestData.Request("a", "uses x") with
        {
            CallableAgents = [new CallableAgentReference { Name = "x", Version = 1 }],
        });

        // The target saved "x" twice and deleted it: its numbering is at 2, and its store gives no forecast.
        await target.CreateAgentAsync(TestData.Request("x", "an old era"));
        await target.UpdateAgentAsync(TestData.Request("x", "an old era, changed"));
        using (var deleted = await target.Client.DeleteAsync(TransferHost.Api("agents/x")))
        {
            deleted.IsSuccessStatusCode.ShouldBeTrue();
        }

        var bundle = await source.ExportAsync("?agent=a");
        var dry = await target.ImportOkAsync(bundle, dryRun: true);

        TransferHost.Item(dry, "agent", "x")["targetVersion"].ShouldBeNull();

        await target.ImportOkAsync(bundle, dryRun: false);

        using var written = await target.Client.GetAsync(TransferHost.Api("agents/x"));

        (await TraconTestHost.ReadJsonAsync(written)).GetProperty("definition").GetProperty("version").GetInt32().ShouldBe(3);
        (await PinOfAsync(target, "a")).ShouldBe(3, "the pin follows the number the write gave, not the 1 the dry run carried");
    }

    /// <summary>A forecast is a convenience of the report: a store that fails while giving one leaves the version unknown.</summary>
    [Fact]
    public async Task A_store_whose_forecast_throws_leaves_the_version_unknown_and_the_import_goes_on()
    {
        await using var target = await TraconTestHost.StartAsync(
            configureServices: static services => services.AddSingleton<IAgentDefinitionStore>(static _ => new ForecastlessAgentStore(throwOnForecast: true)));

        var bundle = TransferHost.NewBundle().WithAgent(TestData.Request("x", "x one"));
        var dry = await target.ImportOkAsync(bundle, dryRun: true);

        dry["valid"]!.GetValue<bool>().ShouldBeTrue(dry.ToJsonString());

        var forecast = TransferHost.Item(dry, "agent", "x");

        forecast["targetVersion"].ShouldBeNull();
        forecast["messages"]!.AsArray().ShouldContain(static message =>
            string.Equals(message!["code"]!.GetValue<string>(), "import_target_version_unknown", StringComparison.Ordinal));
        target.Logs.Entries.ShouldContain(static entry => entry.Contains("could not forecast the next version", StringComparison.Ordinal));

        await target.ImportOkAsync(bundle, dryRun: false);

        (await target.AgentVersionCountAsync("x")).ShouldBe(1);
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

    /// <summary>A store written before the forecast existed: every member of the original contract, and no forecast.</summary>
    private sealed class ForecastlessAgentStore(bool throwOnForecast = false) : IAgentDefinitionStore
    {
        private readonly InMemoryAgentDefinitionStore _inner = new();

        // Declared only to fail on purpose; without it the member is the interface's default and says "unknown".
        public ValueTask<int?> PeekNextVersionAsync(string name, CancellationToken cancellationToken = default)
            => throwOnForecast
                ? throw new InvalidOperationException("The store cannot count right now.")
                : ValueTask.FromResult<int?>(null);

        public ValueTask<AgentDefinition?> GetAsync(string name, CancellationToken cancellationToken = default)
            => _inner.GetAsync(name, cancellationToken);

        public ValueTask<AgentDefinition?> GetVersionAsync(string name, int version, CancellationToken cancellationToken = default)
            => _inner.GetVersionAsync(name, version, cancellationToken);

        public ValueTask<IReadOnlyList<AgentDefinition>> ListAsync(CancellationToken cancellationToken = default)
            => _inner.ListAsync(cancellationToken);

        public ValueTask<AgentDefinition> SaveAsync(AgentDefinition definition, CancellationToken cancellationToken = default)
            => _inner.SaveAsync(definition, cancellationToken);

        public ValueTask<bool> DeleteAsync(string name, CancellationToken cancellationToken = default)
            => _inner.DeleteAsync(name, cancellationToken);

        public ValueTask<IReadOnlyList<AgentDefinition>> ListVersionsAsync(string name, CancellationToken cancellationToken = default)
            => _inner.ListVersionsAsync(name, cancellationToken);

        public ValueTask<AgentDefinition> RollbackAsync(string name, int version, CancellationToken cancellationToken = default)
            => _inner.RollbackAsync(name, version, cancellationToken);
    }
}
