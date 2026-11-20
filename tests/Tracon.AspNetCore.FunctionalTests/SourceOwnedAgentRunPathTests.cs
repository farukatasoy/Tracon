using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using FakeModelProvider = Tracon.Testing.FakeModelProvider;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// An agent a <see cref="IAgentDefinitionSource"/> owns, on the paths that compile its definition again: a replay,
/// an automatic continuation and the reconciler that queues one.
/// </summary>
/// <remarks>
/// <para>
/// A source that outranks the store wins the name in the catalog. These paths used to read the store by name, so
/// the run of a source-owned agent was replayed from an older stored copy of the same name, and a source-only agent
/// had no definition to continue. They read the definition the catalog runs now.
/// </para>
/// <para>
/// The recorded values of <c>/run</c> come from the agent the catalog compiled, not from the definition the gate
/// read a moment earlier: a source that changes its definition between the two reads must not leave a record of a
/// value set the run was never bound with.
/// </para>
/// </remarks>
public sealed class SourceOwnedAgentRunPathTests
{
    private const string Agent = "owned-agent";
    private const string ProviderName = "owned-provider";
    private const string ModelName = "owned-model";
    private const string SessionId = "owned-session";
    private const string FromSource = "The source answers.";
    private const string FromStore = "The store answers.";

    [Fact]
    public async Task A_replay_compiles_the_definition_the_source_returns_and_not_an_older_stored_copy()
    {
        var provider = Provider();
        await using var host = await StartAsync(provider, source: new InMemoryDefinitionSource().Add(Definition(FromSource)));

        await SaveStoredAsync(host, Definition(FromStore));

        var runId = await RunAsync(host);

        provider.Requests[^1].Options!.Instructions.ShouldBe(FromSource);

        using var replay = await host.Client.PostAsJsonAsync($"/tracon/api/runs/{runId}/replay", new { toolMode = "LiveTools" });

        replay.StatusCode.ShouldBe(HttpStatusCode.OK, await replay.Content.ReadAsStringAsync());
        provider.Requests.Count.ShouldBe(2);
        provider.Requests[^1].Options!.Instructions.ShouldBe(FromSource, "the replay runs the agent the source owns");
    }

    [Fact]
    public async Task A_continuation_compiles_the_definition_the_source_returns_and_not_an_older_stored_copy()
    {
        var provider = Provider();
        await using var host = await StartAsync(provider, source: new InMemoryDefinitionSource().Add(Definition(FromSource)), continuation: true);

        // The stored copy goes in before the interrupted run exists, so the reconciler (it scans every 30 ms) cannot
        // find the run first.
        await SaveStoredAsync(host, Definition(FromStore));

        var sourceRunId = await SeedInterruptedRunAsync(host);
        var continuation = await WaitForContinuationAsync(host, sourceRunId);

        continuation.Status.ShouldBe(RunStatus.Completed, continuation.Error?.Message);
        provider.Requests[^1].Options!.Instructions.ShouldBe(FromSource, "the continued turn runs the agent the source owns");
    }

    [Fact]
    public async Task An_agent_only_a_source_holds_is_continued_from_the_definition_the_source_returns()
    {
        var provider = Provider();
        await using var host = await StartAsync(provider, source: new InMemoryDefinitionSource().Add(Definition(FromSource)), continuation: true);

        var sourceRunId = await SeedInterruptedRunAsync(host);
        var continuation = await WaitForContinuationAsync(host, sourceRunId);

        continuation.Status.ShouldBe(RunStatus.Completed, continuation.Error?.Message);
        provider.Requests[^1].Options!.Instructions.ShouldBe(FromSource);
    }

    [Fact]
    public async Task A_run_records_the_values_the_catalog_bound_when_the_source_changed_its_definition_after_the_gate_read_it()
    {
        // The gate reads the first definition (no default for "tone"); the catalog resolves with the second one, which
        // adds the optional "tone" parameter. The run is bound with both values, and that is the set it records.
        var first = Definition("Hello {{customer}}.") with
        {
            Parameters = [new AgentParameter { Name = "customer", Kind = AgentParameterKind.Text, Required = true }],
        };

        var later = Definition("Hello {{customer}}, in a {{tone}} tone.") with
        {
            Parameters =
            [
                new AgentParameter { Name = "customer", Kind = AgentParameterKind.Text, Required = true },
                new AgentParameter { Name = "tone", Kind = AgentParameterKind.Text, DefaultValue = "formal" },
            ],
        };

        var provider = Provider();
        await using var host = await StartAsync(provider, source: null, extra: builder => builder.AddAgentDefinitionSource(new ChangingSource(first, later)));

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/tracon/api/agents/{Agent}/run")
        {
            Content = JsonContent.Create(new AgentRunRequest
            {
                Message = "go",
                Parameters = new Dictionary<string, string>(StringComparer.Ordinal) { ["customer"] = "Ada" },
            }),
        };

        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));

        using var response = await host.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        provider.Requests[^1].Options!.Instructions.ShouldBe("Hello Ada, in a formal tone.");

        var runId = (await TraconTestHost.ReadJsonAsync(response)).GetProperty("runId").GetGuid();
        var record = await host.Services.GetRequiredService<IRunInputStore>().GetAsync(
            host.Services.GetRequiredService<ITenantContext>().TenantId,
            runId,
            TestContext.Current.CancellationToken);

        record.ShouldNotBeNull().Parameters.ShouldNotBeNull().OrderBy(static pair => pair.Key, StringComparer.Ordinal)
            .Select(static pair => $"{pair.Key}={pair.Value}")
            .ShouldBe(["customer=Ada", "tone=formal"]);
    }

    private static AgentDefinition Definition(string instructions) => new()
    {
        Name = Agent,
        Instructions = instructions,
        Model = new ModelBinding { Provider = ProviderName, Model = ModelName },
    };

    private static FakeModelProvider Provider()
        => new FakeModelProvider(ProviderName)
            .WithModel(new ModelDescriptor { Name = ModelName })
            .EchoesUserMessage();

    private static Task<TraconTestHost> StartAsync(
        FakeModelProvider provider,
        InMemoryDefinitionSource? source,
        bool continuation = false,
        Action<ITraconBuilder>? extra = null)
    {
        Action<IServiceCollection>? configureServices = null;

        if (continuation)
        {
            configureServices = static services =>
            {
                services.Configure<RunReconciliationOptions>(static options =>
                {
                    options.Enabled = true;
                    options.ScanInterval = TimeSpan.FromMilliseconds(30);
                    options.OrphanThreshold = TimeSpan.FromMinutes(5);
                    options.HeartbeatInterval = TimeSpan.FromSeconds(30);
                });
                services.Configure<TraconRunContinuationOptions>(static options =>
                {
                    options.Enabled = true;
                    options.MaxAttempts = 1;
                });
            };
        }

        return TraconTestHost.StartAsync(
            builder =>
            {
                builder.AddModelProvider(provider);

                if (source is not null)
                {
                    builder.AddAgentDefinitionSource(source);
                }

                extra?.Invoke(builder);
                builder.UseScheduling(static options => options.PollInterval = TimeSpan.FromMilliseconds(20));
            },
            configureServices: configureServices);
    }

    // Saved straight into the store: the management API refuses a name a source already lists, and the point here is a
    // stored copy that a source came to outrank.
    private static async Task SaveStoredAsync(TraconTestHost host, AgentDefinition definition)
        => await host.Services.GetRequiredService<IAgentDefinitionStore>().SaveAsync(definition, TestContext.Current.CancellationToken);

    private static async Task<Guid> RunAsync(TraconTestHost host)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/tracon/api/agents/{Agent}/run")
        {
            Content = JsonContent.Create(new AgentRunRequest { Message = "hello" }),
        };

        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));

        using var response = await host.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        return (await TraconTestHost.ReadJsonAsync(response)).GetProperty("runId").GetGuid();
    }

    /// <summary>
    /// Seeds a run that looks interrupted: Running, no heartbeat, started ten minutes ago, with its input recorded.
    /// A real crash cannot be made in-process; the queue, the worker and the compiled agent that follow are real.
    /// </summary>
    private static async Task<Guid> SeedInterruptedRunAsync(TraconTestHost host)
    {
        var runs = host.Services.GetRequiredService<IRunStore>();
        var inputs = host.Services.GetRequiredService<IRunInputStore>();
        var tenant = host.Services.GetRequiredService<ITenantContext>().TenantId;
        var runId = TraconId.NewId();

        await runs.StartRunAsync(new RunStartInfo
        {
            RunId = runId,
            AgentName = Agent,
            Status = RunStatus.Running,
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
            TenantId = tenant,
            SessionId = SessionId,
            ModelId = ModelName,
        });

        await inputs.SaveAsync(new RunInputRecord
        {
            RunId = runId,
            TenantId = tenant,
            Messages = [new ChatMessage(ChatRole.User, "where did we stop?")],
            CreatedAt = DateTimeOffset.UtcNow,
        });

        return runId;
    }

    private static async Task<RunRecord> WaitForContinuationAsync(TraconTestHost host, Guid sourceRunId)
    {
        var runs = host.Services.GetRequiredService<IRunStore>();

        var continuation = await WaitUntil.ValueAsync(
            async () => (await runs.QueryRunsAsync(new RunQuery { SessionId = SessionId, OnlyRootRuns = false }))
                .FirstOrDefault(record => record.Id != sourceRunId),
            static record => record is { Status: RunStatus.Completed or RunStatus.Failed },
            $"a continuation of run '{sourceRunId}' to settle");

        return continuation!;
    }

    /// <summary>A source that hands out one definition on its first read and another on every later one.</summary>
    private sealed class ChangingSource(AgentDefinition first, AgentDefinition later) : IAgentDefinitionSource
    {
        private int _reads;

        public string Name => "changing-source";

        public int Priority => AgentSourcePriority.Database - 50;

        public ValueTask<IReadOnlyList<AgentDefinition>> ListAsync(CancellationToken cancellationToken = default)
            => new((IReadOnlyList<AgentDefinition>)[later]);

        public ValueTask<AgentDefinition?> GetAsync(string agentName, CancellationToken cancellationToken = default)
            => new(string.Equals(agentName, first.Name, StringComparison.Ordinal)
                ? Interlocked.Increment(ref _reads) == 1 ? first : later
                : null);
    }
}
