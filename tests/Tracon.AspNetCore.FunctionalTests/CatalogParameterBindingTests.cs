using System.Net;
using System.Net.Http.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using FakeModelProvider = Tracon.Testing.FakeModelProvider;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A .NET caller that resolves an agent from the catalog with parameter values and runs it in its own code.
/// </summary>
/// <remarks>
/// <para>
/// The values reach the model, are checked against the agent's schema before a provider is called, are
/// written to the run record, and are repeated by a replay. None of that was possible from code: the two
/// paths that bind a value were internal, and the one public compile method skipped the decorators.
/// </para>
/// <para>
/// Every case goes through the real host: the binding happens in the catalog, the record in the recording
/// decorator, and the replay in a service that reads the record back.
/// </para>
/// </remarks>
public sealed class CatalogParameterBindingTests
{
    private const string Required = "billing-agent";
    private const string DefaultsOnly = "greeting-agent";
    private const string InCode = "coded-agent";
    private const string ProviderName = "binding-provider";
    private const string ModelName = "binding-model";

    [Fact]
    public async Task A_run_started_from_a_resolve_with_values_reaches_the_model_and_records_the_values()
    {
        var provider = Provider();
        await using var host = await StartAsync(provider);

        var agent = await ResolveAsync(host, Required, Values("customer", "Acme"));
        await agent.RunAsync("hi");

        provider.Requests[^1].Options!.Instructions.ShouldBe("Hello Acme, how can I help?");
        (await RecordedValuesAsync(host, (await RunIdsAsync(host)).ShouldHaveSingleItem())).ShouldBe(Values("customer", "Acme"));
    }

    [Fact]
    public async Task A_replay_of_that_run_repeats_the_values_it_recorded()
    {
        var provider = Provider();
        await using var host = await StartAsync(provider);

        var agent = await ResolveAsync(host, Required, Values("customer", "Acme"));
        await agent.RunAsync("hi");

        var run = (await host.Services.GetRequiredService<IRunStore>().QueryRunsAsync(new RunQuery())).ShouldHaveSingleItem();
        var calls = provider.Requests.Count;

        using var replay = await host.Client.PostAsJsonAsync($"/tracon/api/runs/{run.Id}/replay", new { toolMode = "NoTools" });

        replay.StatusCode.ShouldBe(HttpStatusCode.OK, await replay.Content.ReadAsStringAsync());
        provider.Requests.Count.ShouldBe(calls + 1);
        provider.Requests[^1].Options!.Instructions.ShouldBe("Hello Acme, how can I help?");
    }

    [Fact]
    public async Task A_code_agent_resolved_with_values_binds_and_replays_them_too()
    {
        // A code-registered definition has no stored copy, so its replay takes the catalog path: the agent is
        // rebuilt with the recorded values through the parameter factory.
        var provider = Provider();
        await using var host = await StartAsync(provider);

        var agent = await ResolveAsync(host, InCode, Values("customer", "Globex"));
        await agent.RunAsync("hi");

        provider.Requests[^1].Options!.Instructions.ShouldBe("Hello Globex, how can I help?");

        var run = (await host.Services.GetRequiredService<IRunStore>().QueryRunsAsync(new RunQuery())).ShouldHaveSingleItem();

        using var replay = await host.Client.PostAsJsonAsync($"/tracon/api/runs/{run.Id}/replay", new { toolMode = "LiveTools" });

        replay.StatusCode.ShouldBe(HttpStatusCode.OK, await replay.Content.ReadAsStringAsync());
        provider.Requests[^1].Options!.Instructions.ShouldBe("Hello Globex, how can I help?");
    }

    [Fact]
    public async Task A_missing_required_value_is_refused_before_the_provider_and_names_the_parameter()
    {
        var provider = Provider();
        await using var host = await StartAsync(provider);

        var failure = await Should.ThrowAsync<TraconParameterException>(
            async () => await ResolveAsync(host, Required, Values()));

        failure.MissingParameters.ShouldBe(["customer"]);
        failure.UnknownParameters.ShouldBeEmpty();
        failure.ErrorType.ShouldBe("invalid_parameters");
        await AssertNothingRanAsync(host, provider);
    }

    [Fact]
    public async Task A_name_the_schema_does_not_declare_is_refused_as_unknown()
    {
        var provider = Provider();
        await using var host = await StartAsync(provider);

        var failure = await Should.ThrowAsync<TraconParameterException>(
            async () => await ResolveAsync(host, Required, Values("customer", "Acme", "custmer", "typo")));

        failure.UnknownParameters.ShouldBe(["custmer"]);
        failure.MissingParameters.ShouldBeEmpty();
        await AssertNothingRanAsync(host, provider);
    }

    [Fact]
    public async Task A_value_over_the_length_limit_is_refused_and_the_error_never_repeats_the_value()
    {
        var provider = Provider();
        await using var host = await StartAsync(provider, configure: builder => builder.Configure(static options => options.MaxParameterValueLength = 8));
        const string secretLookingValue = "do-not-leak-this-value";

        var failure = await Should.ThrowAsync<TraconParameterException>(
            async () => await ResolveAsync(host, Required, Values("customer", secretLookingValue)));

        failure.TooLongParameters.ShouldBe(["customer"]);
        failure.Message.ShouldContain("customer", Case.Sensitive);
        failure.Message.ShouldNotContain(secretLookingValue, Case.Sensitive);
        await AssertNothingRanAsync(host, provider);
    }

    [Fact]
    public async Task A_value_for_an_agent_with_no_schema_is_refused_as_unknown()
    {
        var provider = Provider();
        await using var host = await StartAsync(provider, configure: builder =>
            builder.AddAgent(new AgentDefinition
            {
                Name = "plain-agent",
                Instructions = "No placeholders here.",
                Model = new ModelBinding { Provider = ProviderName, Model = ModelName },
            }));

        var failure = await Should.ThrowAsync<TraconParameterException>(
            async () => await ResolveAsync(host, "plain-agent", Values("customer", "Acme")));

        failure.UnknownParameters.ShouldBe(["customer"]);
        await AssertNothingRanAsync(host, provider);
    }

    [Fact]
    public async Task An_empty_set_binds_the_defaults_and_records_them_while_no_set_records_nothing()
    {
        var provider = Provider();
        await using var host = await StartAsync(provider);

        var bound = await ResolveAsync(host, DefaultsOnly, Values());
        await bound.RunAsync("hi");

        provider.Requests[^1].Options!.Instructions.ShouldBe("Hello friend, how can I help?");

        var boundRunId = (await RunIdsAsync(host)).ShouldHaveSingleItem();

        (await RecordedValuesAsync(host, boundRunId)).ShouldBe(Values("customer", "friend"));

        // No values at all: the cached, unbound path. The run records nothing, as it always did.
        var catalog = host.Services.GetRequiredService<IAgentCatalog>();
        var unbound = (await catalog.ResolveAsync(DefaultsOnly, cancellationToken: TestContext.Current.CancellationToken)).ShouldNotBeNull();
        await unbound.RunAsync("hi");

        var unboundRunId = (await RunIdsAsync(host)).Single(id => id != boundRunId);

        (await RecordedValuesAsync(host, unboundRunId)).ShouldBeNull();
    }

    [Fact]
    public async Task Two_resolves_with_different_values_never_share_a_compiled_agent()
    {
        var provider = Provider();
        await using var host = await StartAsync(provider);

        var acme = await ResolveAsync(host, Required, Values("customer", "Acme"));
        var globex = await ResolveAsync(host, Required, Values("customer", "Globex"));

        await Task.WhenAll(acme.RunAsync("hi"), globex.RunAsync("hi"));

        provider.Requests
            .Select(static request => request.Options!.Instructions)
            .Order(StringComparer.Ordinal)
            .ShouldBe(["Hello Acme, how can I help?", "Hello Globex, how can I help?"]);
    }

    [Fact]
    public async Task The_bound_agent_goes_through_the_same_decorators_as_every_other_resolve()
    {
        // The public compile method used to skip the decorator chain: no recording, no telemetry, no tool approval.
        var provider = Provider();
        var spy = new SpyDecorator();
        await using var host = await StartAsync(provider, configure: builder => builder.AddAgentDecorator(spy));

        var agent = await ResolveAsync(host, Required, Values("customer", "Acme"));

        spy.Decorated.ShouldBe([Required]);

        await agent.RunAsync("hi");

        (await host.Services.GetRequiredService<IRunStore>().QueryRunsAsync(new RunQuery()))
            .ShouldHaveSingleItem().AgentName.ShouldBe(Required);
    }

    [Fact]
    public async Task A_cancelled_resolve_with_values_throws_cancellation_and_leaves_no_run_row()
    {
        var provider = Provider();
        await using var host = await StartAsync(provider);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(
            async () => await host.Services.GetRequiredService<IAgentCatalog>().ResolveAsync(
                Required,
                new AgentResolveOptions { Parameters = Values("customer", "Acme") },
                cancelled.Token));

        await AssertNothingRanAsync(host, provider);
    }

    [Fact]
    public async Task A_name_no_source_holds_resolves_to_null_even_with_values()
    {
        var provider = Provider();
        await using var host = await StartAsync(provider);

        var catalog = host.Services.GetRequiredService<IAgentCatalog>();

        (await catalog.ResolveAsync(
            "no-such-agent",
            new AgentResolveOptions { Parameters = Values("customer", "Acme") },
            TestContext.Current.CancellationToken)).ShouldBeNull();
    }

    [Fact]
    public async Task Describing_an_agent_returns_the_parameters_it_declares_and_writes_no_run()
    {
        var provider = Provider();
        await using var host = await StartAsync(provider);

        var catalog = host.Services.GetRequiredService<IAgentCatalog>();

        var required = (await catalog.DescribeAsync(Required, TestContext.Current.CancellationToken)).ShouldNotBeNull();
        var defaults = (await catalog.DescribeAsync(DefaultsOnly, TestContext.Current.CancellationToken)).ShouldNotBeNull();
        var coded = (await catalog.DescribeAsync(InCode, TestContext.Current.CancellationToken)).ShouldNotBeNull();

        var parameter = required.Parameters.ShouldHaveSingleItem();

        parameter.Name.ShouldBe("customer");
        parameter.Required.ShouldBeTrue();
        defaults.Parameters.ShouldHaveSingleItem().DefaultValue.ShouldBe("friend");
        coded.Parameters.ShouldHaveSingleItem().Required.ShouldBeTrue();
        (await catalog.DescribeAsync("no-such-agent", TestContext.Current.CancellationToken)).ShouldBeNull();

        await AssertNothingRanAsync(host, provider);
    }

    [Fact]
    public async Task Describing_an_agent_gives_the_listing_the_same_descriptor()
    {
        var provider = Provider();
        await using var host = await StartAsync(provider);

        var catalog = host.Services.GetRequiredService<IAgentCatalog>();
        var listed = (await catalog.ListAsync(TestContext.Current.CancellationToken)).Single(static descriptor => string.Equals(descriptor.Name, Required, StringComparison.Ordinal));
        var described = (await catalog.DescribeAsync(Required, TestContext.Current.CancellationToken)).ShouldNotBeNull();

        described.SourceName.ShouldBe(listed.SourceName);
        described.Version.ShouldBe(listed.Version);
        described.Parameters.ShouldBe(listed.Parameters);
    }

    private static Dictionary<string, string> Values(params string[] pairs)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);

        for (var index = 0; index + 1 < pairs.Length; index += 2)
        {
            values[pairs[index]] = pairs[index + 1];
        }

        return values;
    }

    private static async ValueTask<AIAgent> ResolveAsync(TraconTestHost host, string name, IReadOnlyDictionary<string, string> values)
        => (await host.Services.GetRequiredService<IAgentCatalog>().ResolveAsync(
            name,
            new AgentResolveOptions { Parameters = values },
            TestContext.Current.CancellationToken)).ShouldNotBeNull();

    private static async Task<IReadOnlyList<Guid>> RunIdsAsync(TraconTestHost host)
        => [.. (await host.Services.GetRequiredService<IRunStore>().QueryRunsAsync(new RunQuery())).Select(static run => run.Id)];

    private static async Task<IReadOnlyDictionary<string, string>?> RecordedValuesAsync(TraconTestHost host, Guid runId)
    {
        var record = await host.Services.GetRequiredService<IRunInputStore>().GetAsync(
            host.Services.GetRequiredService<ITenantContext>().TenantId,
            runId,
            TestContext.Current.CancellationToken);

        return record.ShouldNotBeNull().Parameters;
    }

    private static async Task AssertNothingRanAsync(TraconTestHost host, FakeModelProvider provider)
    {
        provider.Requests.ShouldBeEmpty();
        (await host.Services.GetRequiredService<IRunStore>().QueryRunsAsync(new RunQuery())).ShouldBeEmpty();
    }

    private static AgentDefinition Definition(string name, bool required) => new()
    {
        Name = name,
        Instructions = "Hello {{customer}}, how can I help?",
        Parameters =
        [
            required
                ? new AgentParameter { Name = "customer", Kind = AgentParameterKind.Text, Required = true }
                : new AgentParameter { Name = "customer", Kind = AgentParameterKind.Text, DefaultValue = "friend" },
        ],
        Model = new ModelBinding { Provider = ProviderName, Model = ModelName },
    };

    private static FakeModelProvider Provider()
        => new FakeModelProvider(ProviderName)
            .WithModel(new ModelDescriptor { Name = ModelName })
            .EchoesUserMessage();

    private static async Task<TraconTestHost> StartAsync(FakeModelProvider provider, Action<ITraconBuilder>? configure = null)
    {
        var host = await TraconTestHost.StartAsync(
            configureTracon: builder =>
            {
                builder.AddModelProvider(provider);
                builder.AddAgent(Definition(InCode, required: true));
                configure?.Invoke(builder);
            });

        var store = host.Services.GetRequiredService<IAgentDefinitionStore>();
        await store.SaveAsync(Definition(Required, required: true));
        await store.SaveAsync(Definition(DefaultsOnly, required: false));

        return host;
    }

    private sealed class SpyDecorator : IAgentDecorator
    {
        public List<string> Decorated { get; } = [];

        public int Order => 5;

        public AIAgent Decorate(AIAgent agent, AgentDescriptor descriptor)
        {
            Decorated.Add(descriptor.Name);

            return agent;
        }
    }
}
