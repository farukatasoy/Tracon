using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using static Tracon.AspNetCore.FunctionalTests.Infrastructure.NodeUsageHost;
using FakeModelProvider = Tracon.Testing.FakeModelProvider;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A root agent run counts what its child agents' tools spent beyond tokens, in the tenant's
/// quota entry and in its <c>run.completed</c> webhook.
/// </summary>
/// <remarks>
/// The root reads its tree from the store when it closes and takes its own share from memory;
/// the subtraction that tells the two apart, and the currency rule beside it, is arithmetic
/// that only a child that reported something exercises. Each of the two runs spends 10 tokens:
/// the router 0.016, the researcher 0.042.
/// </remarks>
public sealed class NonTokenSpendAgentTreeTests
{
    private const string ProviderName = "tree-provider";
    private const string RouterModel = "router-model";
    private const string ResearcherModel = "researcher-model";
    private const string SpeakTool = "speak-tree";

    private const string StartTask = "background_agents_start_task";
    private const string WaitForCompletion = "background_agents_wait_for_first_completion";
    private const string GetResults = "background_agents_get_task_results";

    [Fact]
    public async Task A_child_agents_tool_amount_is_in_the_roots_quota_entry_and_webhook()
    {
        var publisher = new CompletedPublisher();

        await using var host = await StartTreeAsync(publisher, rootToolAmount: null, childToolAmount: 0.5m);

        await RunRouterAsync(host);

        var counter = await DailyCounterAsync(host, "yonlendirici");

        // The router's turn, the researcher's turn and the researcher's speech.
        counter.Cost.ShouldBe(0.558m);
        counter.Runs.ShouldBe(1);

        var run = publisher.Completed.ShouldHaveSingleItem();

        run.Cost.ShouldBe(0.558m);
        run.NonTokenCost.ShouldBe(0.5m);
        run.Currency.ShouldBe("USD");
    }

    [Fact]
    public async Task The_roots_own_tool_amount_and_its_childs_add_once_each()
    {
        var publisher = new CompletedPublisher();

        await using var host = await StartTreeAsync(publisher, rootToolAmount: 0.25m, childToolAmount: 0.5m);

        await RunRouterAsync(host);

        (await DailyCounterAsync(host, "yonlendirici")).Cost.ShouldBe(0.808m);

        var run = publisher.Completed.ShouldHaveSingleItem();

        run.Cost.ShouldBe(0.808m);
        run.NonTokenCost.ShouldBe(0.75m);
    }

    [Fact]
    public async Task A_child_amount_in_another_currency_is_in_neither_the_quota_nor_the_webhook()
    {
        var publisher = new CompletedPublisher();

        await using var host = await StartTreeAsync(publisher, rootToolAmount: 0.25m, childToolAmount: 100m, childCurrency: "EUR");

        await RunRouterAsync(host);

        // Only the root's own USD amount adds; the child's EUR is listed on its run and not summed.
        (await DailyCounterAsync(host, "yonlendirici")).Cost.ShouldBe(0.308m);

        var run = publisher.Completed.ShouldHaveSingleItem();

        run.Cost.ShouldBe(0.308m);
        run.NonTokenCost.ShouldBe(0.25m);
    }

    private static async Task RunRouterAsync(TraconTestHost host)
    {
        var catalog = host.Services.GetRequiredService<IAgentCatalog>();
        var router = (await catalog.ResolveAsync("yonlendirici", culture: null, CancellationToken.None)).ShouldNotBeNull();

        var response = await router.RunAsync("start");

        response.Text.ShouldContain("Delegated", Case.Sensitive);
    }

    private static Task<TraconTestHost> StartTreeAsync(
        CompletedPublisher publisher,
        decimal? rootToolAmount,
        decimal childToolAmount,
        string childCurrency = "USD")
    {
        var provider = new FakeModelProvider(ProviderName)
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
            .ForModel(RouterModel, cfg =>
            {
                if (rootToolAmount is not null)
                {
                    cfg.CallsTool(SpeakTool);
                }

                cfg
                    .CallsTool(StartTask, new { agentName = "arastirmaci", input = "sub task", description = "sub task" })
                    .CallsTool(WaitForCompletion, new { taskIds = new[] { 1 } })
                    .CallsTool(GetResults, new { taskId = 1 })
                    .EchoesLastToolResult("Delegated: ", inputTokens: 4, outputTokens: 6);
            })
            .ForModel(ResearcherModel, cfg => cfg
                .CallsTool(SpeakTool)
                .EchoesLastToolResult("Sub task done: ", inputTokens: 4, outputTokens: 6));

        // One tool, two agents: the amount it reports depends on who calls it.
        var amounts = new Dictionary<string, (decimal Amount, string Currency)>(StringComparer.Ordinal)
        {
            ["arastirmaci"] = (childToolAmount, childCurrency),
            ["yonlendirici"] = (rootToolAmount ?? 0m, "USD"),
        };

        return StartAsync(
            builder =>
            {
                builder.AddModelProvider(provider);

                builder.AddTool(
                    (Func<Task<string>>)(() => Task.Run(() =>
                    {
                        var (amount, currency) = amounts[TraconRunContext.Current!.AgentName!];

                        TraconToolUsage.Report(new ToolCallUsage
                        {
                            Unit = ToolUsageUnits.Characters,
                            Quantity = 100m,
                            Cost = amount,
                            Currency = currency,
                        }).ShouldBeTrue();

                        return "spoken";
                    })),
                    name: SpeakTool);

                builder.AddAgent(new AgentDefinition
                {
                    Name = "arastirmaci",
                    Description = "Performs research.",
                    Instructions = "Research.",
                    Model = new ModelBinding { Provider = ProviderName, Model = ResearcherModel },
                    ToolNames = [SpeakTool],
                    Origin = AgentDefinitionOrigin.Code,
                });

                builder.AddAgent(new AgentDefinition
                {
                    Name = "yonlendirici",
                    Description = "Delegates the work.",
                    Instructions = "Delegate.",
                    Model = new ModelBinding { Provider = ProviderName, Model = RouterModel },
                    CallableAgentNames = ["arastirmaci"],
                    ToolNames = [SpeakTool],
                    Origin = AgentDefinitionOrigin.Code,
                });
            },
            services => services.AddSingleton<IWebhookPublisher>(publisher));
    }

    private sealed class CompletedPublisher : IWebhookPublisher
    {
        private readonly List<WebhookRunSummary> _completed = [];

        public IReadOnlyList<WebhookRunSummary> Completed
        {
            get
            {
                lock (_completed)
                {
                    return [.. _completed];
                }
            }
        }

        public ValueTask<int> PublishAsync(
            string tenantId,
            string eventType,
            WebhookEventPayload payload,
            CancellationToken cancellationToken = default)
        {
            if (string.Equals(eventType, WebhookEvents.RunCompleted, StringComparison.Ordinal) && payload.Run is { } run)
            {
                lock (_completed)
                {
                    _completed.Add(run);
                }
            }

            return new ValueTask<int>(1);
        }
    }
}
