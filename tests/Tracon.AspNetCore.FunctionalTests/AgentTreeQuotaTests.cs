using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using FakeModelProvider = Tracon.Testing.FakeModelProvider;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A root agent run counts its whole call tree — its own tokens and cost and
/// those of every callable child agent — in the tenant's quota counters and in
/// its <c>run.*</c> webhook.
/// </summary>
/// <remarks>
/// <para>
/// 🚨 F-383: the root wrote only its own usage (and its side-channel share) to
/// the quota counter, while a child agent run, by design, writes nothing (only
/// a root accounts, so a tree is not counted once per depth). The children's
/// tokens and cost reached no counter at all, so a tenant's token or cost quota
/// was exceeded through sub-agents without a single <c>429</c>. A workflow root
/// already counted its tree (<c>WorkflowRunner.RecordQuotaAsync</c>); the site
/// says "consumption is counted once at the tree's root".
/// </para>
/// <para>
/// The same gap reached the root's <c>run.completed</c> webhook: child runs
/// publish nothing, and the root's payload carried only its own tokens, so a
/// subscriber billing from webhooks under-billed every agent that delegates.
/// A workflow's payload already carries the tree (class sweep, same fix).
/// </para>
/// <para>
/// The scenario runs through the real host with Microsoft Agent Framework's
/// real background-task tools, the same tree as <see cref="AgentDelegationTests"/>:
/// each of the two runs spends 10 tokens and is priced.
/// </para>
/// </remarks>
public sealed class AgentTreeQuotaTests
{
    private const string Provider = "routing";
    private const string RouterModel = "router-model";
    private const string ResearcherModel = "researcher-model";

    private const string StartTask = "background_agents_start_task";
    private const string WaitForCompletion = "background_agents_wait_for_first_completion";
    private const string GetResults = "background_agents_get_task_results";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task The_root_counts_the_child_agents_tokens_and_cost(bool streaming)
    {
        var publisher = new RecordingPublisher();

        await using var host = await StartAsync(publisher);

        await RunRouterAsync(host, streaming);

        var counter = await DailyCounterAsync(host, "yonlendirici");

        // The root's own 10 tokens and the child's 10: the tree.
        counter.Tokens.ShouldBe(20);
        counter.Runs.ShouldBe(1);

        // Router: 4 in × 1000 + 6 out × 2000 per million = 0.016;
        // researcher: 4 × 3000 + 6 × 5000 per million = 0.042.
        counter.Cost.ShouldBe(0.058m);

        // A child run never accounts on its own (the Depth == 0 rule stays).
        (await CountersAsync(host, "arastirmaci")).ShouldBeEmpty();
    }

    [Fact]
    public async Task The_roots_run_completed_webhook_carries_the_tree()
    {
        var publisher = new RecordingPublisher();

        await using var host = await StartAsync(publisher);

        await RunRouterAsync(host, streaming: false);

        var completed = publisher.Events
            .Where(static entry => string.Equals(entry.EventType, WebhookEvents.RunCompleted, StringComparison.Ordinal))
            .Select(static entry => entry.Payload.Run.ShouldNotBeNull())
            .ToList();

        // Only the root publishes, and its summary is the tree's.
        var run = completed.ShouldHaveSingleItem();

        run.AgentName.ShouldBe("yonlendirici");
        run.InputTokens.ShouldBe(8);
        run.OutputTokens.ShouldBe(12);
        run.Cost.ShouldBe(0.058m);
    }

    /// <summary>
    /// F-419: a child priced in another currency. The tree has no total, so the root
    /// counts its own cost and the whole tree's tokens; before, 0.016 USD and 0.042 EUR
    /// went to the quota as 0.058.
    /// </summary>
    [Fact]
    public async Task A_tree_priced_in_two_currencies_counts_only_the_roots_own_cost()
    {
        var publisher = new RecordingPublisher();

        await using var host = await StartAsync(publisher, new TwoCurrencyPricing());

        await RunRouterAsync(host, streaming: false);

        var counter = await DailyCounterAsync(host, "yonlendirici");

        counter.Tokens.ShouldBe(20);
        counter.Cost.ShouldBe(0.016m);

        var run = publisher.Events
            .Where(static entry => string.Equals(entry.EventType, WebhookEvents.RunCompleted, StringComparison.Ordinal))
            .Select(static entry => entry.Payload.Run.ShouldNotBeNull())
            .ShouldHaveSingleItem();

        run.Cost.ShouldBe(0.016m);
        string.Equals(run.Currency, "USD", StringComparison.Ordinal).ShouldBeTrue();

        var root = (await host.Services.GetRequiredService<IRunStore>().QueryRunsAsync(new RunQuery { AgentName = "yonlendirici" }))
            .ShouldHaveSingleItem();

        root.TreeCost.ShouldNotBeNull().CurrencyCount.ShouldBe(2);
        root.TreeCost.Total().ShouldBeNull();
    }

    [Fact]
    public async Task A_root_without_children_counts_only_itself()
    {
        var publisher = new RecordingPublisher();

        await using var host = await StartAsync(publisher);

        var catalog = host.Services.GetRequiredService<IAgentCatalog>();
        var researcher = (await catalog.ResolveAsync("arastirmaci", culture: null, CancellationToken.None)).ShouldNotBeNull();

        await researcher.RunAsync("direct");

        var counter = await DailyCounterAsync(host, "arastirmaci");

        counter.Tokens.ShouldBe(10);
        counter.Cost.ShouldBe(0.042m);
    }

    private static async Task RunRouterAsync(TraconTestHost host, bool streaming)
    {
        var catalog = host.Services.GetRequiredService<IAgentCatalog>();
        var router = (await catalog.ResolveAsync("yonlendirici", culture: null, CancellationToken.None)).ShouldNotBeNull();

        if (streaming)
        {
            await foreach (var update in router.RunStreamingAsync("start"))
            {
                _ = update;
            }

            return;
        }

        var response = await router.RunAsync("start");

        response.Text.ShouldContain("Delegated", Case.Sensitive);
    }

    private static async Task<QuotaUsageRecord> DailyCounterAsync(TraconTestHost host, string agentName)
        => (await CountersAsync(host, agentName))
            .Single(static record => record.Period == QuotaPeriod.Daily);

    private static async Task<IReadOnlyList<QuotaUsageRecord>> CountersAsync(TraconTestHost host, string agentName)
        => await host.Services.GetRequiredService<IQuotaStore>().GetUsageAsync(new QuotaUsageQuery
        {
            TenantId = host.Services.GetRequiredService<IOptions<TraconOptions>>().Value.DefaultTenantId,
            AgentName = agentName,
        });

    private static Task<TraconTestHost> StartAsync(RecordingPublisher publisher, IRunPricingResolver? pricing = null)
        => TraconTestHost.StartAsync(
            configureTracon: builder =>
            {
                var provider = new FakeModelProvider(Provider)
                    .WithModel(new ModelDescriptor
                    {
                        Name = RouterModel,
                        InputCostPerMillionTokens = 1_000m,
                        OutputCostPerMillionTokens = 2_000m,
                    })
                    .WithModel(new ModelDescriptor
                    {
                        Name = ResearcherModel,
                        InputCostPerMillionTokens = 3_000m,
                        OutputCostPerMillionTokens = 5_000m,
                    });

                provider
                    .ForModel(RouterModel, cfg => cfg
                        .CallsTool(StartTask, new { agentName = "arastirmaci", input = "sub task", description = "sub task" })
                        .CallsTool(WaitForCompletion, new { taskIds = new[] { 1 } })
                        .CallsTool(GetResults, new { taskId = 1 })
                        .EchoesLastToolResult("Delegated: ", inputTokens: 4, outputTokens: 6))
                    .ForModel(ResearcherModel, cfg => cfg
                        .RespondsWith("Sub task done", inputTokens: 4, outputTokens: 6));

                builder.AddModelProvider(provider);

                builder.AddAgent(new AgentDefinition
                {
                    Name = "arastirmaci",
                    Description = "Performs research.",
                    Instructions = "Research.",
                    Model = Binding(ResearcherModel),
                    Origin = AgentDefinitionOrigin.Code,
                });

                builder.AddAgent(new AgentDefinition
                {
                    Name = "yonlendirici",
                    Description = "Delegates the work.",
                    Instructions = "Delegate.",
                    Model = Binding(RouterModel),
                    CallableAgentNames = ["arastirmaci"],
                    Origin = AgentDefinitionOrigin.Code,
                });
            },
            configureServices: services =>
            {
                services.AddSingleton<IWebhookPublisher>(publisher);

                if (pricing is not null)
                {
                    services.AddSingleton(pricing);
                }
            });

    private static ModelBinding Binding(string model) => new() { Provider = Provider, Model = model };

    /// <summary>Prices the router in USD and the researcher in EUR, at the catalog rates above.</summary>
    private sealed class TwoCurrencyPricing : IRunPricingResolver
    {
        public RunCost? Resolve(string? provider, string? model, RunUsage? usage)
        {
            if (usage is null || model is null)
            {
                return null;
            }

            var (input, output, currency) = string.Equals(model, RouterModel, StringComparison.Ordinal)
                ? (1_000m, 2_000m, "USD")
                : (3_000m, 5_000m, "EUR");

            return new RunCost
            {
                InputCost = (usage.InputTokens ?? 0) * input / 1_000_000m,
                OutputCost = (usage.OutputTokens ?? 0) * output / 1_000_000m,
                Currency = currency,
                Source = PricingSource.Catalog,
            };
        }
    }

    private sealed class RecordingPublisher : IWebhookPublisher
    {
        private readonly List<(string EventType, WebhookEventPayload Payload)> _events = [];

        public IReadOnlyList<(string EventType, WebhookEventPayload Payload)> Events
        {
            get
            {
                lock (_events)
                {
                    return [.. _events];
                }
            }
        }

        public ValueTask<int> PublishAsync(
            string tenantId,
            string eventType,
            WebhookEventPayload payload,
            CancellationToken cancellationToken = default)
        {
            lock (_events)
            {
                _events.Add((eventType, payload));
            }

            return new ValueTask<int>(1);
        }
    }
}
