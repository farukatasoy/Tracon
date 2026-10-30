using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using static Tracon.AspNetCore.FunctionalTests.Infrastructure.NodeUsageHost;
using FakeModelProvider = Tracon.Testing.FakeModelProvider;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// Every decision about money — the agent's quota entry and webhook, the workflow's quota entry
/// and webhook, and the tree's budget — counts what a run spent beyond tokens.
/// </summary>
/// <remarks>
/// <para>
/// And the class of that decision: the money a decision reads is a SUM, and a place that
/// reads only the token term under-reports every run that reported something else. Nothing
/// fails when it happens; a tenant's ceiling is simply passed. One function adds the two
/// (<see cref="RunSpend"/>), and this class has one test PER PLACE that takes a decision, so a
/// place that stops calling it is a red test and not a quiet leak.
/// </para>
/// <para>
/// Each scenario spends 0.016 on tokens (one turn of 4 input and 6 output tokens) and a known
/// amount beyond them, in the real host: the agent path through a real tool call, the workflow
/// path through a real function node.
/// </para>
/// </remarks>
public sealed class NonTokenSpendTests
{
    private const string SpeakTool = "speak-test";

    // --- The agent path: a tool's measurement ---

    [Fact]
    public async Task The_agents_quota_entry_counts_the_amount_its_tool_reported()
    {
        var publisher = new RecordingPublisher();

        await using var host = await StartAgentAsync(publisher, toolAmount: 0.5m);

        await RunAgentAsync(host);

        var counter = await DailyCounterAsync(host, "narrator");

        // The turn's tokens and the speech: 0.016 + 0.5.
        counter.Cost.ShouldBe(0.516m);
        counter.Runs.ShouldBe(1);
    }

    [Fact]
    public async Task The_agents_run_completed_webhook_carries_the_combined_cost_and_the_non_token_part()
    {
        var publisher = new RecordingPublisher();

        await using var host = await StartAgentAsync(publisher, toolAmount: 0.5m);

        await RunAgentAsync(host);

        var run = publisher.Completed().ShouldHaveSingleItem();

        run.Cost.ShouldBe(0.516m);
        run.NonTokenCost.ShouldBe(0.5m);
        run.Currency.ShouldBe("USD");
    }

    [Fact]
    public async Task The_agents_run_record_holds_the_tools_amount_beside_the_token_cost()
    {
        await using var host = await StartAgentAsync(new RecordingPublisher(), toolAmount: 0.5m);

        await RunAgentAsync(host);

        var run = (await host.Services.GetRequiredService<IRunStore>().QueryRunsAsync(new RunQuery()))
            .Single(static candidate => string.Equals(candidate.AgentName, "narrator", StringComparison.Ordinal));

        run.Cost.ShouldNotBeNull().Total().ShouldBe(0.016m);
        run.NonTokenCost.ShouldNotBeNull().Amount.ShouldBe(0.5m);
        RunSpend.Total(run.Cost, run.NonTokenCost).ShouldBe(0.516m);
    }

    [Fact]
    public async Task A_tools_amount_stops_the_agents_next_model_call_in_a_non_streaming_run()
    {
        // 🚨 A non-streaming run reads its tool records only after the LAST model turn. The
        // budget has to hear about the amount when the tool reports it, or it can never refuse
        // the next turn: this fails if the amount reaches the budget only at the end.
        await using var host = await StartAgentAsync(
            new RecordingPublisher(),
            toolAmount: 5m,
            configureOptions: options => options.AgentGraph.MaxTotalCost = 1m);

        var catalog = host.Services.GetRequiredService<IAgentCatalog>();
        var narrator = (await catalog.ResolveAsync("narrator", culture: null, CancellationToken.None)).ShouldNotBeNull();

        await Should.ThrowAsync<TraconRunBudgetExceededException>(() => narrator.RunAsync("go"));
    }

    /// <summary>
    /// A tool's measurement that its columns cannot hold is refused when it is
    /// reported, as a function node's item is, instead of reaching the insert.
    /// </summary>
    /// <remarks>
    /// The unit, provider and model columns hold 64, 100 and 200 characters. A
    /// node's item was checked, a tool's was not: PostgreSQL and SQL Server refuse
    /// the oversize value, and the insert that fails takes the call's whole record
    /// with it, arguments and result included.
    /// </remarks>
    [Fact]
    public async Task A_tool_measurement_its_columns_cannot_hold_is_refused_when_it_is_reported()
    {
        bool? reported = null;
        var provider = new FakeModelProvider(Provider).WithModel(new ModelDescriptor { Name = Model });
        provider.ForModel(Model, cfg => cfg.CallsTool(SpeakTool).EchoesLastToolResult("Done: "));

        await using var host = await StartAsync(
            builder => builder
                .AddModelProvider(provider)
                .AddTool(
                    (Func<string>)(() =>
                    {
                        reported = TraconToolUsage.Report(new ToolCallUsage
                        {
                            Unit = new string('u', 65),
                            Quantity = 100m,
                            Cost = 0.5m,
                            Currency = "USD",
                        });

                        return "spoken";
                    }),
                    name: SpeakTool)
                .AddAgent(new AgentDefinition
                {
                    Name = "narrator",
                    Description = "Narrates.",
                    Instructions = "Narrate.",
                    Model = new ModelBinding { Provider = Provider, Model = Model },
                    ToolNames = [SpeakTool],
                    Origin = AgentDefinitionOrigin.Code,
                }));

        await RunAgentAsync(host);

        reported.ShouldBe(false, "a unit longer than its column was accepted");

        var runs = host.Services.GetRequiredService<IRunStore>();
        var run = (await runs.QueryRunsAsync(new RunQuery()))
            .Single(static candidate => string.Equals(candidate.AgentName, "narrator", StringComparison.Ordinal));
        var call = (await runs.ListToolInvocationsAsync(run.Id)).ShouldHaveSingleItem();
        call.Usage.ShouldBeNull();
        run.NonTokenCost.ShouldBeNull();
    }

    // --- The workflow path: a function node's item ---

    [Fact]
    public async Task The_workflows_quota_entry_counts_the_amount_its_function_node_reported()
    {
        var publisher = new RecordingPublisher();

        await using var host = await StartWorkflowAsync(publisher, nodeAmount: 2m);

        await SaveAndRunAsync(host);

        var counter = await DailyCounterAsync(host, Workflow);

        // The agent step's turn (0.016) and the function node's 2.
        counter.Cost.ShouldBe(2.016m);
        counter.Runs.ShouldBe(1);
    }

    [Fact]
    public async Task The_workflows_run_completed_webhook_carries_the_combined_cost_and_the_non_token_part()
    {
        var publisher = new RecordingPublisher();

        await using var host = await StartWorkflowAsync(publisher, nodeAmount: 2m);

        await SaveAndRunAsync(host);

        var run = publisher.Completed().ShouldHaveSingleItem();

        run.Cost.ShouldBe(2.016m);
        run.NonTokenCost.ShouldBe(2m);
        run.Currency.ShouldBe("USD");
    }

    [Fact]
    public async Task A_function_nodes_amount_stops_the_trees_next_model_call()
    {
        var provider = PricedProvider();

        await using var host = await StartAsync(
            builder => builder
                .AddModelProvider(provider)
                .AddAgent(Writer())
                .AddWorkflowFunction<List<ChatMessage>, List<ChatMessage>>(
                    "narrate",
                    _ => async (messages, context, _) =>
                    {
                        await context.ReportUsageAsync(Speech(5m));

                        return messages;
                    }),
            configureOptions: options => options.AgentGraph.MaxTotalCost = 1m);

        await SaveAsync(host, Workflow, Function("narrate"), Agent("writer"));

        var runId = await RunAsync(host);

        var run = await RecordAsync(host, runId);

        // The agent step never reached the model: the node had already spent the ceiling.
        run.Status.ShouldBe(RunStatus.Failed);
        run.Error.ShouldNotBeNull().Class.ShouldBe(RunErrorClass.QuotaExceeded);
        provider.Requests.Count.ShouldBe(0);
    }

    // --- What does NOT add ---

    [Fact]
    public async Task An_amount_in_another_currency_is_in_neither_the_quota_nor_the_webhook()
    {
        var publisher = new RecordingPublisher();

        await using var host = await StartAsync(
            builder => builder
                .AddModelProvider(PricedProvider())
                .AddAgent(Writer())
                .AddWorkflowFunction<List<ChatMessage>, List<ChatMessage>>(
                    "narrate",
                    _ => async (messages, context, _) =>
                    {
                        await context.ReportUsageAsync(Speech(1m));
                        await context.ReportUsageAsync(Speech(100m, currency: "EUR"));

                        return messages;
                    }),
            services => services.AddSingleton<IWebhookPublisher>(publisher));

        await SaveAsync(host, Workflow, Function("narrate"), Agent("writer"));

        var runId = await RunAsync(host);

        (await DailyCounterAsync(host, Workflow)).Cost.ShouldBe(1.016m);

        var run = publisher.Completed().ShouldHaveSingleItem();

        run.Cost.ShouldBe(1.016m);
        run.NonTokenCost.ShouldBe(1m);

        // It is listed, and the run says one item was not added.
        (await UsageAsync(host, runId)).Count.ShouldBe(2);
        (await RecordAsync(host, runId)).NonTokenCost.ShouldNotBeNull().UnsummedItemCount.ShouldBe(1);
    }

    [Fact]
    public async Task A_run_that_reported_nothing_leaves_the_quota_and_the_webhook_as_they_were()
    {
        var publisher = new RecordingPublisher();

        await using var host = await StartWorkflowAsync(publisher, nodeAmount: null);

        await SaveAndRunAsync(host);

        (await DailyCounterAsync(host, Workflow)).Cost.ShouldBe(0.016m);

        var run = publisher.Completed().ShouldHaveSingleItem();

        run.Cost.ShouldBe(0.016m);
        run.NonTokenCost.ShouldBeNull();
    }

    // --- Hosts ---

    private static Task<TraconTestHost> StartAgentAsync(
        RecordingPublisher publisher,
        decimal toolAmount,
        Action<TraconOptions>? configureOptions = null)
    {
        var provider = new FakeModelProvider(Provider)
            .WithModel(new ModelDescriptor
            {
                Name = Model,
                InputCostPerMillionTokens = 1_000m,
                OutputCostPerMillionTokens = 2_000m,
            });

        provider.ForModel(Model, cfg => cfg
            .CallsTool(SpeakTool)
            .EchoesLastToolResult("Done: ", inputTokens: 4, outputTokens: 6));

        return StartAsync(
            builder => builder
                .AddModelProvider(provider)
                .AddTool(
                    (Func<Task<string>>)(() => Task.Run(() =>
                    {
                        TraconToolUsage.Report(new ToolCallUsage
                        {
                            Unit = ToolUsageUnits.Characters,
                            Quantity = 100m,
                            Cost = toolAmount,
                            Currency = "USD",
                        }).ShouldBeTrue();

                        return "spoken";
                    })),
                    name: SpeakTool)
                .AddAgent(new AgentDefinition
                {
                    Name = "narrator",
                    Description = "Narrates.",
                    Instructions = "Narrate.",
                    Model = new ModelBinding { Provider = Provider, Model = Model },
                    ToolNames = [SpeakTool],
                    Origin = AgentDefinitionOrigin.Code,
                }),
            services => services.AddSingleton<IWebhookPublisher>(publisher),
            configureOptions);
    }

    private static Task<TraconTestHost> StartWorkflowAsync(RecordingPublisher publisher, decimal? nodeAmount)
        => StartAsync(
            builder => builder
                .AddModelProvider(PricedProvider())
                .AddAgent(Writer())
                .AddWorkflowFunction<List<ChatMessage>, List<ChatMessage>>(
                    "narrate",
                    _ => async (messages, context, _) =>
                    {
                        if (nodeAmount is { } amount)
                        {
                            await context.ReportUsageAsync(Speech(amount));
                        }

                        return messages;
                    }),
            services => services.AddSingleton<IWebhookPublisher>(publisher));

    private static async Task<Guid> SaveAndRunAsync(TraconTestHost host)
    {
        await SaveAsync(host, Workflow, Agent("writer"), Function("narrate"));

        return await RunAsync(host);
    }

    private static async Task RunAgentAsync(TraconTestHost host)
    {
        var catalog = host.Services.GetRequiredService<IAgentCatalog>();
        var narrator = (await catalog.ResolveAsync("narrator", culture: null, CancellationToken.None)).ShouldNotBeNull();

        var response = await narrator.RunAsync("go");

        response.Text.ShouldContain("Done", Case.Sensitive);
    }

    private sealed class RecordingPublisher : IWebhookPublisher
    {
        private readonly List<(string EventType, WebhookEventPayload Payload)> _events = [];

        public IReadOnlyList<WebhookRunSummary> Completed()
        {
            lock (_events)
            {
                return
                [
                    .. _events
                        .Where(static entry => string.Equals(entry.EventType, WebhookEvents.RunCompleted, StringComparison.Ordinal))
                        .Select(static entry => entry.Payload.Run.ShouldNotBeNull()),
                ];
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
