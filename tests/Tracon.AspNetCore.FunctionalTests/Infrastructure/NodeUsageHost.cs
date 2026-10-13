using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using FakeModelProvider = Tracon.Testing.FakeModelProvider;

namespace Tracon.AspNetCore.FunctionalTests.Infrastructure;

/// <summary>
/// The shared setup of the tests that run workflow function nodes which report non-token usage.
/// </summary>
internal static class NodeUsageHost
{
    /// <summary>The workflow every test saves unless it names another.</summary>
    public const string Workflow = "podcast";

    /// <summary>The priced model provider's name.</summary>
    public const string Provider = "spend-provider";

    /// <summary>The model the <c>writer</c> agent runs on: 4 × 1000 + 6 × 2000 per million = 0.016.</summary>
    public const string Model = "spend-model";

    /// <summary>What one turn of <see cref="Model"/> costs with 4 input and 6 output tokens.</summary>
    public const decimal TurnCost = 0.016m;

    /// <summary>A priced provider whose every reply spends 4 input and 6 output tokens.</summary>
    public static FakeModelProvider PricedProvider()
        => new FakeModelProvider(Provider)
            .WithModel(new ModelDescriptor
            {
                Name = Model,
                InputCostPerMillionTokens = 1_000m,
                OutputCostPerMillionTokens = 2_000m,
            })
            .RespondsWith("written", inputTokens: 4, outputTokens: 6);

    /// <summary>An agent that runs on the priced model.</summary>
    public static AgentDefinition Writer(string name = "writer") => new()
    {
        Name = name,
        Description = "Writes.",
        Instructions = "Write.",
        Model = new ModelBinding { Provider = Provider, Model = Model },
        Origin = AgentDefinitionOrigin.Code,
    };

    /// <summary>Starts a host that has workflows on and <c>Tracon:Pricing:Currency</c> set.</summary>
    public static Task<TraconTestHost> StartAsync(
        Action<ITraconBuilder> configure,
        Action<IServiceCollection>? configureServices = null,
        Action<TraconOptions>? configureOptions = null,
        string? currency = "USD")
        => TraconTestHost.StartAsync(
            builder =>
            {
                builder.UseWorkflows();
                configure(builder);
            },
            configureServices: services =>
            {
                services.Configure<TraconOptions>(options =>
                {
                    options.Pricing.Currency = currency;
                    configureOptions?.Invoke(options);
                });

                configureServices?.Invoke(services);
            });

    /// <summary>A function node of a sequential chain.</summary>
    public static WorkflowNodeReference Function(string name, TimeSpan? timeout = null)
        => new() { Name = name, Kind = WorkflowNodeKind.Function, Timeout = timeout };

    /// <summary>An agent node of a sequential chain.</summary>
    public static WorkflowNodeReference Agent(string name)
        => new() { Name = name, Kind = WorkflowNodeKind.Agent };

    /// <summary>Saves a sequential workflow made of the given nodes.</summary>
    public static async Task SaveAsync(
        TraconTestHost host,
        string workflow,
        params WorkflowNodeReference[] nodes)
    {
        using var saved = await host.Client.PutAsJsonAsync(
            $"/tracon/api/workflows/{workflow}",
            new WorkflowSaveRequest { Kind = WorkflowKind.Sequential, Nodes = nodes });

        saved.StatusCode.ShouldBe(System.Net.HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
    }

    /// <summary>Runs a workflow over HTTP, reads the stream to its end and returns the run's id.</summary>
    public static async Task<Guid> RunAsync(
        TraconTestHost host,
        string workflow = Workflow,
        string message = "go",
        string? tenant = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/tracon/api/workflows/{workflow}/run")
        {
            Content = JsonContent.Create(new WorkflowRunHttpRequest { Message = message }),
        };

        if (tenant is not null)
        {
            request.Headers.Add("X-Tracon-Tenant", tenant);
        }

        using var response = await host.Client.SendAsync(request);

        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        var frames = await SseReader.ReadAllAsync(await response.Content.ReadAsStreamAsync());
        var accepted = frames.First(static frame => string.Equals(frame.Event, "run", StringComparison.Ordinal));

        return JsonDocument.Parse(accepted.Data).RootElement.GetProperty("runId").GetGuid();
    }

    /// <summary>The run's record, read from the store.</summary>
    public static async Task<RunRecord> RecordAsync(TraconTestHost host, Guid runId)
        => (await host.Services.GetRequiredService<IRunStore>().GetRunAsync(runId)).ShouldNotBeNull();

    /// <summary>The run's non-token items, read over HTTP.</summary>
    public static async Task<IReadOnlyList<NonTokenUsageRecord>> UsageAsync(
        TraconTestHost host,
        Guid runId,
        string? tenant = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/tracon/api/runs/{runId}/usage");

        if (tenant is not null)
        {
            request.Headers.Add("X-Tracon-Tenant", tenant);
        }

        using var response = await host.Client.SendAsync(request);

        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        return (await response.Content.ReadFromJsonAsync<List<NonTokenUsageRecord>>())!;
    }

    /// <summary>The tenant's daily quota counter of an agent or workflow name.</summary>
    public static async Task<QuotaUsageRecord> DailyCounterAsync(TraconTestHost host, string name)
        => (await host.Services.GetRequiredService<IQuotaStore>().GetUsageAsync(new QuotaUsageQuery
        {
            TenantId = host.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<TraconOptions>>().Value.DefaultTenantId,
            AgentName = name,
        })).Single(static record => record.Period == QuotaPeriod.Daily);

    /// <summary>An item a node reports: characters with an amount in USD.</summary>
    public static ToolCallUsage Speech(decimal cost, decimal characters = 100m, string? currency = "USD")
        => new()
        {
            Unit = ToolUsageUnits.Characters,
            Quantity = characters,
            Cost = cost,
            Currency = currency,
            Provider = "elevenlabs",
            Model = "eleven_multilingual_v2",
        };

    /// <summary>The identity function every test node shares: it takes and passes on the messages.</summary>
    public static ValueTask<List<ChatMessage>> Pass(List<ChatMessage> messages) => new(messages);
}
