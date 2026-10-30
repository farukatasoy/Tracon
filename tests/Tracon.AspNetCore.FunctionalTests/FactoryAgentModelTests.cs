using System.Net;
using System.Net.Http.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
// Same rationale as AgentExtensionRunTests.cs.
using FakeModelProvider = Tracon.Testing.FakeModelProvider;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A factory agent that declares its model keeps what a definition agent keeps -
/// the tenant's own provider key, the tenant's egress policy, the model in the run record, the cost
/// and the cost quota.
/// </summary>
/// <remarks>
/// <para>
/// These are HTTP tests on purpose. The loss they close lived in the catalog path: the factory used to
/// build its client with the synchronous registry call, which resolves no tenant state. Only the
/// real <c>AddTracon()</c> registration, the real tenancy middleware and a real run through the decorators
/// build every part of that path the way a consumer's host does.
/// </para>
/// <para>
/// Failure modes asked: cancellation (a canceled resolve is not a source failure), concurrency (one
/// warning, in <c>FactoryAgentDiagnosticsTests</c>), empty input (a null agent, a request field), another
/// tenant (two tenants, two keys, one agent), subsystem failure (the egress refusal is the same as a
/// definition agent's).
/// </para>
/// </remarks>
public sealed class FactoryAgentModelTests
{
    private const string ProviderName = "factory-model";
    private const string ModelName = "factory-1";
    private const string FactoryAgentName = "factory-agent";
    private const string DefinitionAgentName = "definition-agent";
    private const string TenantHeader = "X-Tracon-Tenant";
    private const string KeyName = "Tracon:ProviderKeys:tenant-a:FactoryModel";
    private const string TenantKey = "tenant-a-key";

    private static readonly ModelBinding Binding = new() { Provider = ProviderName, Model = ModelName };

    [Fact]
    public async Task Each_tenants_run_uses_its_own_key()
    {
        var provider = new KeyRecordingProvider(ProviderName);

        await using var host = await StartAsync(provider, withTenantKey: true);

        foreach (var tenant in new[] { "tenant-a", "tenant-b", "tenant-a", "tenant-b" })
        {
            using var response = await RunAsync(host, FactoryAgentName, tenant);
            response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        }

        // The tenant with a key gets an agent built with the key on every run; the other tenant gets the key
        // set up at start. Tracon keeps no agent between runs, so no run reuses another tenant's client.
        provider.Keys.ShouldBe([TenantKey, KeyRecordingProvider.SetupKey, TenantKey, KeyRecordingProvider.SetupKey]);
    }

    [Fact]
    public async Task Two_tenants_resolving_at_once_never_swap_keys()
    {
        // The tenant of a run is ambient. Runs of two tenants that overlap must still build each client with the
        // key of its own tenant: Tracon keeps no agent and no client between calls.
        var provider = new KeyRecordingProvider(ProviderName);

        await using var host = await StartAsync(provider, withTenantKey: true);

        const int pairs = 20;

        var responses = await Task.WhenAll(Enumerable.Range(0, pairs).SelectMany(static _ => new[] { "tenant-a", "tenant-b" }).Select(
            async tenant =>
            {
                using var response = await RunAsync(host, FactoryAgentName, tenant);
                return response.StatusCode;
            }));

        responses.ShouldAllBe(static status => status == HttpStatusCode.OK);
        provider.Keys.Count(static key => string.Equals(key, TenantKey, StringComparison.Ordinal)).ShouldBe(pairs);
        provider.Keys.Count(static key => string.Equals(key, KeyRecordingProvider.SetupKey, StringComparison.Ordinal)).ShouldBe(pairs);
    }

    [Fact]
    public async Task Egress_refuses_a_factory_agent_as_it_refuses_a_definition_agent()
    {
        var provider = new KeyRecordingProvider(ProviderName);

        await using var host = await StartAsync(provider);

        // The tenant may call one provider only, and it is not the one these agents use.
        await host.Services.GetRequiredService<ITenantEgressPolicyStore>().UpsertAsync(
            DefaultTenant(host),
            ["allowed-provider"],
            TestContext.Current.CancellationToken);

        var definition = await OutcomeAsync(host, DefinitionAgentName);
        var factory = await OutcomeAsync(host, FactoryAgentName);

        // The definition agent must be refused, or this test proves nothing; the factory agent is then
        // refused with the very same result.
        string.Equals(definition, "200", StringComparison.Ordinal).ShouldBeFalse();
        factory.ShouldBe(definition);
        provider.Keys.ShouldBeEmpty("a refused provider is not built, so no key is looked up");

        // Straight through the catalog, the type is the same too.
        var catalog = host.Services.GetRequiredService<IAgentCatalog>();

        await Should.ThrowAsync<TraconTenantProviderRefusedException>(async () =>
            await catalog.ResolveAsync(DefinitionAgentName, culture: null, TestContext.Current.CancellationToken));
        await Should.ThrowAsync<TraconTenantProviderRefusedException>(async () =>
            await catalog.ResolveAsync(FactoryAgentName, culture: null, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task The_run_record_carries_the_declared_model_and_its_cost()
    {
        await using var host = await StartAsync(new KeyRecordingProvider(ProviderName));

        using var response = await RunAsync(host, FactoryAgentName, tenant: null);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        var run = await SingleRunAsync(host, FactoryAgentName);

        run.ModelId.ShouldBe(ModelName);

        // 1000 input tokens at 3 per million and 1000 output tokens at 15 per million.
        run.Cost.ShouldNotBeNull().InputCost.ShouldBe(0.003m);
        run.Cost.OutputCost.ShouldBe(0.015m);
    }

    [Fact]
    public async Task A_cost_quota_stops_a_factory_agent()
    {
        await using var host = await StartAsync(new KeyRecordingProvider(ProviderName));

        using (var saved = await host.Client.PutAsJsonAsync(
                   new Uri("/tracon/api/quotas", UriKind.Relative),
                   new QuotaSaveRequest { AgentName = FactoryAgentName, Period = QuotaPeriod.Daily, MaxCost = 0.01m, Enabled = true },
                   TestContext.Current.CancellationToken))
        {
            saved.StatusCode.ShouldBeOneOf(HttpStatusCode.OK, HttpStatusCode.Created);
        }

        // The first run costs 0.018, over the ceiling. The second one is the one the quota stops.
        using (var first = await RunAsync(host, FactoryAgentName, tenant: null))
        {
            first.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        using var second = await RunAsync(host, FactoryAgentName, tenant: null);

        second.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task The_catalog_lists_the_declared_model_and_estimate_answers()
    {
        await using var host = await StartAsync(new KeyRecordingProvider(ProviderName));

        var detail = await TraconTestHost.ReadJsonAsync(await host.Client.GetAsync(
            new Uri($"/tracon/api/agents/{FactoryAgentName}", UriKind.Relative),
            TestContext.Current.CancellationToken));

        var model = detail.GetProperty("descriptor").GetProperty("model");
        model.GetProperty("provider").GetString().ShouldBe(ProviderName);
        model.GetProperty("model").GetString().ShouldBe(ModelName);

        using var estimate = await host.Client.PostAsJsonAsync(
            new Uri($"/tracon/api/agents/{FactoryAgentName}/estimate", UriKind.Relative),
            new AgentRunRequest { Message = "hi" },
            TestContext.Current.CancellationToken);

        estimate.StatusCode.ShouldBe(HttpStatusCode.OK, await estimate.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Estimate_of_a_factory_agent_that_declares_no_model_names_the_overload_that_fixes_it()
    {
        await using var host = await StartAsync(new KeyRecordingProvider(ProviderName));

        using var estimate = await host.Client.PostAsJsonAsync(
            new Uri("/tracon/api/agents/modelless-agent/estimate", UriKind.Relative),
            new AgentRunRequest { Message = "hi" },
            TestContext.Current.CancellationToken);

        estimate.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var body = await estimate.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        body.ShouldContain("declares no model");
        body.ShouldContain("AddAgent(name, model, factory)");
    }

    [Fact]
    public async Task The_detail_view_reads_instructions_from_a_model_declaring_factory()
    {
        await using var host = await StartAsync(new KeyRecordingProvider(ProviderName));

        var detail = await TraconTestHost.ReadJsonAsync(await host.Client.GetAsync(
            new Uri($"/tracon/api/agents/{FactoryAgentName}", UriKind.Relative),
            TestContext.Current.CancellationToken));

        detail.GetProperty("factoryInstructions").GetString().ShouldBe("Answer briefly.");
    }

    [Fact]
    public async Task The_detail_view_of_a_model_declaring_factory_that_throws_is_not_a_500()
    {
        await using var host = await StartAsync(new KeyRecordingProvider(ProviderName));

        using var response = await host.Client.GetAsync(
            new Uri("/tracon/api/agents/throwing-agent", UriKind.Relative),
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var detail = await TraconTestHost.ReadJsonAsync(response);
        detail.GetProperty("factoryInstructions").ValueKind.ShouldBe(System.Text.Json.JsonValueKind.Null);
        host.Logs.AllText.ShouldContain("Reading instructions from factory agent 'throwing-agent' failed.");
    }

    [Fact]
    public async Task A_canceled_resolve_is_not_a_source_failure()
    {
        await using var host = await StartAsync(new KeyRecordingProvider(ProviderName));

        using var canceled = new CancellationTokenSource();
        var catalog = host.Services.GetRequiredService<IAgentCatalog>();

        var resolving = catalog.ResolveAsync("waiting-agent", culture: null, canceled.Token).AsTask();

        await canceled.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(async () => await resolving);
        host.Logs.AllText.ShouldNotContain("failed during resolve", Case.Insensitive);
    }

    [Fact]
    public async Task A_throwing_async_factory_fails_like_a_sync_one()
    {
        await using var host = await StartAsync(new KeyRecordingProvider(ProviderName));
        var catalog = host.Services.GetRequiredService<IAgentCatalog>();

        var sync = await Should.ThrowAsync<TraconAgentSourceException>(async () =>
            await catalog.ResolveAsync("throwing-agent", culture: null, TestContext.Current.CancellationToken));
        var asynchronous = await Should.ThrowAsync<TraconAgentSourceException>(async () =>
            await catalog.ResolveAsync("throwing-async-agent", culture: null, TestContext.Current.CancellationToken));

        asynchronous.Message.ShouldBe(sync.Message);
        asynchronous.InnerException.ShouldBeOfType<InvalidOperationException>();
        sync.InnerException.ShouldBeOfType<InvalidOperationException>();
    }

    [Fact]
    public async Task A_factory_that_returns_null_fails_with_the_agent_name()
    {
        await using var host = await StartAsync(new KeyRecordingProvider(ProviderName));
        var catalog = host.Services.GetRequiredService<IAgentCatalog>();

        var exception = await Should.ThrowAsync<TraconException>(async () =>
            await catalog.ResolveAsync("null-agent", culture: null, TestContext.Current.CancellationToken));

        exception.Message.ShouldContain("null-agent");
    }

    private static Task<TraconTestHost> StartAsync(KeyRecordingProvider provider, bool withTenantKey = false)
        => TraconTestHost.StartAsync(
            configureTracon: builder => builder
                .AddModelProvider(provider)
                .UseTenancy(static options =>
                {
                    options.Enabled = true;
                    options.AllowHeaderResolution = true;
                })
                .AddAgent(new AgentDefinition
                {
                    Name = DefinitionAgentName,
                    Instructions = "Answer briefly.",
                    Model = Binding,
                    Origin = AgentDefinitionOrigin.Code,
                })
                .AddAgent(
                    FactoryAgentName,
                    Binding,
                    static context => context.ChatClient.AsAIAgent(instructions: "Answer briefly.", name: context.AgentName))
                .AddAgent(
                    "modelless-agent",
                    static services => services.GetRequiredService<IModelProviderRegistry>()
                        .CreateChatClient(Binding)
                        .AsAIAgent(instructions: "Answer briefly.", name: "modelless-agent"))
                .AddAgent(
                    "throwing-agent",
                    Binding,
                    static (AgentFactoryContext _) => throw new InvalidOperationException("The factory failed."))
                .AddAgent(
                    "throwing-async-agent",
                    Binding,
                    static (AgentFactoryContext _, CancellationToken _) => throw new InvalidOperationException("The factory failed."))
                .AddAgent(
                    "null-agent",
                    Binding,
                    static (AgentFactoryContext _) => null!)
                .AddAgent(
                    "waiting-agent",
                    Binding,
                    static async (context, cancellationToken) =>
                    {
                        await Task.Delay(Timeout.Infinite, cancellationToken); // delay: simulated
                        return context.ChatClient.AsAIAgent(name: context.AgentName);
                    }),
            configureServices: withTenantKey
                ? static services => services.AddSingleton<IConfiguration>(
                    new ConfigurationBuilder().AddInMemoryCollection([new(KeyName, TenantKey)]).Build())
                : null)
            .ContinueWith(
                async started =>
                {
                    var host = await started;

                    if (withTenantKey)
                    {
                        await host.Services.GetRequiredService<ITenantProviderBindingStore>().UpsertAsync(
                            new TenantProviderBinding
                            {
                                TenantId = "tenant-a",
                                ProviderName = ProviderName,
                                ApiKeyConfigurationName = KeyName,
                                UpdatedAt = DateTimeOffset.UtcNow,
                            },
                            TestContext.Current.CancellationToken);
                    }

                    return host;
                },
                TaskScheduler.Default)
            .Unwrap();

    private static async Task<HttpResponseMessage> RunAsync(TraconTestHost host, string agentName, string? tenant)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"/tracon/api/agents/{agentName}/run", UriKind.Relative))
        {
            Content = JsonContent.Create(new AgentRunRequest { Message = "hello" }),
        };

        if (tenant is not null)
        {
            request.Headers.Add(TenantHeader, tenant);
        }

        return await host.Client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    /// <summary>The status code of a run, or the type of the exception that left the pipeline.</summary>
    private static async Task<string> OutcomeAsync(TraconTestHost host, string agentName)
    {
        try
        {
            using var response = await RunAsync(host, agentName, tenant: null);
            var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

            return response.StatusCode == HttpStatusCode.OK
                ? "200"
                : $"{(int)response.StatusCode} {System.Text.Json.JsonDocument.Parse(body).RootElement.GetProperty("title").GetString()}";
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return exception.GetType().Name;
        }
    }

    private static async Task<RunRecord> SingleRunAsync(TraconTestHost host, string agentName)
        => (await host.Services.GetRequiredService<IRunStore>()
                .QueryRunsAsync(new RunQuery { AgentName = agentName }, TestContext.Current.CancellationToken))
            .ShouldHaveSingleItem();

    private static string DefaultTenant(TraconTestHost host)
        => host.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<TraconOptions>>().Value.DefaultTenantId;

    /// <summary>A scripted provider that records the key each chat client was built with and reports token usage.</summary>
    private sealed class KeyRecordingProvider(string name) : ITenantCredentialModelProvider
    {
        public const string SetupKey = "setup-key";

        private readonly FakeModelProvider _inner = new FakeModelProvider(name)
            .WithModel(new ModelDescriptor
            {
                Name = ModelName,
                ContextWindowTokens = 8_192,
                MaxOutputTokens = 1_024,
                InputCostPerMillionTokens = 3m,
                OutputCostPerMillionTokens = 15m,
            })
            .RespondsWith("hello back", inputTokens: 1_000, outputTokens: 1_000);

        private readonly List<string> _keys = [];

        public string Name { get; } = name;

        public IReadOnlyList<ModelDescriptor> Models => _inner.Models;

        public IReadOnlyList<string> Keys
        {
            get
            {
                lock (_keys)
                {
                    return [.. _keys];
                }
            }
        }

        public IChatClient CreateChatClient(ModelBinding binding)
        {
            Record(SetupKey);
            return _inner.CreateChatClient(binding);
        }

        public IChatClient CreateChatClient(ModelBinding binding, ModelProviderCredential credential)
        {
            ArgumentNullException.ThrowIfNull(credential);
            Record(credential.ApiKey);
            return _inner.CreateChatClient(binding, credential);
        }

        private void Record(string key)
        {
            lock (_keys)
            {
                _keys.Add(key);
            }
        }
    }
}
