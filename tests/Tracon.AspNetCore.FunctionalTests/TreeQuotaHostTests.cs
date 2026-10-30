using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using static Tracon.AspNetCore.FunctionalTests.Infrastructure.NodeUsageHost;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A tenant's quota is read where a workflow's work begins, over the real host: a chain of paid nodes or
/// agent steps stops once the tenant's cost quota is spent, and another tenant's quota is another counter.
/// </summary>
/// <remarks>
/// <para>
/// The quota used to be read at the door only, so a workflow that waited for a person, or ran for hours, passed a
/// quota that was spent in its first minutes. The runner-level cases (<c>TreeQuotaGateTests</c>) prove the rules; these
/// prove the boundaries that carry them: the HTTP tenant, the container-built enforcer and store, the agent step's real
/// model pipeline, and the webhook the counter's threshold still publishes.
/// </para>
/// </remarks>
public sealed class TreeQuotaHostTests
{
    private const string TenantHeader = "X-Tracon-Tenant";

    [Fact]
    public async Task A_chain_of_paid_nodes_stops_at_the_tenants_cost_quota_and_the_threshold_webhook_still_comes()
    {
        var publisher = new RecordingPublisher();
        var synthesized = new Calls();

        await using var host = await StartAsync(
            builder => builder
                .AddWorkflowFunction<List<ChatMessage>, List<ChatMessage>>("speak", Speaks(5m))
                .AddWorkflowFunction<List<ChatMessage>, List<ChatMessage>>("synthesize", Counts(synthesized)),
            services => services.AddSingleton<IWebhookPublisher>(publisher));

        await SaveRuleAsync(host, DefaultTenant(host), maxCost: 5m);
        await SaveAsync(host, Workflow, Function("speak"), Function("synthesize"));

        var run = await RecordAsync(host, await RunAsync(host));

        run.Status.ShouldBe(RunStatus.Failed);

        var error = run.Error.ShouldNotBeNull();

        error.Type.ShouldBe(TraconTenantQuotaExceededException.TenantQuotaExceededErrorType);
        error.Class.ShouldBe(RunErrorClass.QuotaExceeded);
        error.Message.ShouldContain("Could not start function node 'synthesize'", Case.Sensitive);
        error.Message.ShouldContain("daily cost quota for the whole tenant has been exceeded (5/5)", Case.Sensitive);
        synthesized.Count.ShouldBe(0);

        // The refusal does not lose what the first node spent, and the counter is written once.
        var counter = await DailyCounterAsync(host, Workflow);

        counter.Cost.ShouldBe(5m);
        counter.Runs.ShouldBe(1);

        // The threshold the counter crossed is announced as it always was.
        publisher.Events.ShouldContain(static entry => string.Equals(entry.EventType, WebhookEvents.QuotaThreshold, StringComparison.Ordinal));
        publisher.Events.ShouldContain(static entry => string.Equals(entry.EventType, WebhookEvents.RunFailed, StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_agent_step_is_refused_once_the_step_before_it_spent_the_quota()
    {
        await using var host = await StartAsync(
            builder => builder
                .AddModelProvider(PricedProvider())
                .AddAgent(Writer("first-writer"))
                .AddAgent(Writer("second-writer")));

        // One turn of the priced model costs 0.016: the first step writes it when it ends.
        await SaveRuleAsync(host, DefaultTenant(host), maxCost: TurnCost);
        await SaveAsync(host, Workflow, Agent("first-writer"), Agent("second-writer"));

        var run = await RecordAsync(host, await RunAsync(host));

        run.Status.ShouldBe(RunStatus.Failed);

        var error = run.Error.ShouldNotBeNull();

        error.Type.ShouldBe(TraconTenantQuotaExceededException.TenantQuotaExceededErrorType);
        error.Message.ShouldContain("Could not call agent 'second-writer'", Case.Sensitive);

        var runs = await host.Services.GetRequiredService<IRunStore>().QueryRunsAsync(new RunQuery { OnlyRootRuns = false, Take = 100 });

        runs.ShouldNotContain(static record => string.Equals(record.AgentName, "second-writer", StringComparison.Ordinal), "the refused step opened a run");

        var counter = await DailyCounterAsync(host, Workflow);

        counter.Cost.ShouldBe(TurnCost, "the first step's turn, written once by the step that spent it");
        counter.Runs.ShouldBe(1);
    }

    [Fact]
    public async Task Another_tenants_quota_is_another_counter()
    {
        var synthesized = new Calls();

        await using var host = await StartAsync(
            builder =>
            {
                builder
                    .AddWorkflowFunction<List<ChatMessage>, List<ChatMessage>>("speak", Speaks(5m))
                    .AddWorkflowFunction<List<ChatMessage>, List<ChatMessage>>("synthesize", Counts(synthesized));

                builder.UseTenancy(static options =>
                {
                    options.Enabled = true;
                    options.AllowHeaderResolution = true;
                });
            });

        // Poor has a quota of 1 and rich has none. The same workflow, saved for each.
        await SaveRuleAsync(host, "poor", maxCost: 1m);
        await SaveForAsync(host, "poor");
        await SaveForAsync(host, "rich");

        var spent = await RecordForAsync(host, "poor", await RunAsync(host, tenant: "poor"));

        spent.Error.ShouldNotBeNull().Type.ShouldBe(TraconTenantQuotaExceededException.TenantQuotaExceededErrorType);
        synthesized.Count.ShouldBe(0);

        // Poor's counter is spent, so poor's next run is refused at the door, as it always was.
        using var door = new HttpRequestMessage(HttpMethod.Post, $"/tracon/api/workflows/{Workflow}/run")
        {
            Content = JsonContent.Create(new WorkflowRunHttpRequest { Message = "go" }),
        };

        door.Headers.Add(TenantHeader, "poor");

        using var refused = await host.Client.SendAsync(door);

        refused.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);

        // The same host, another tenant: poor's counter is not rich's.
        var rich = await RecordForAsync(host, "rich", await RunAsync(host, tenant: "rich"));

        rich.Status.ShouldBe(RunStatus.Completed, rich.Error?.Message);
        synthesized.Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_quota_that_is_off_never_refuses_a_node()
    {
        var synthesized = new Calls();

        await using var host = await StartAsync(
            builder => builder
                .AddWorkflowFunction<List<ChatMessage>, List<ChatMessage>>("speak", Speaks(5m))
                .AddWorkflowFunction<List<ChatMessage>, List<ChatMessage>>("synthesize", Counts(synthesized)),
            services => services.Configure<TraconQuotaOptions>(options => options.Enabled = false));

        await SaveRuleAsync(host, DefaultTenant(host), maxCost: 1m);
        await SaveAsync(host, Workflow, Function("speak"), Function("synthesize"));

        (await RecordAsync(host, await RunAsync(host))).Status.ShouldBe(RunStatus.Completed);
        synthesized.Count.ShouldBe(1);
    }

    /// <summary>The run's record, read as the tenant it belongs to.</summary>
    private static async Task<RunRecord> RecordForAsync(TraconTestHost host, string tenant, Guid runId)
    {
        using var scope = AmbientTenantScope.Begin(tenant);

        return (await host.Services.GetRequiredService<IRunStore>().GetRunAsync(runId)).ShouldNotBeNull();
    }

    private static string DefaultTenant(TraconTestHost host)
        => host.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<TraconOptions>>().Value.DefaultTenantId;

    private static async Task SaveRuleAsync(TraconTestHost host, string tenant, decimal maxCost)
        => await host.Services.GetRequiredService<IQuotaStore>().SaveAsync(new QuotaDefinition
        {
            Id = Guid.NewGuid(),
            TenantId = tenant,
            Period = QuotaPeriod.Daily,
            MaxCost = maxCost,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });

    private static async Task SaveForAsync(TraconTestHost host, string tenant)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, $"/tracon/api/workflows/{Workflow}")
        {
            Content = JsonContent.Create(new WorkflowSaveRequest
            {
                Kind = WorkflowKind.Sequential,
                Nodes = [Function("speak"), Function("synthesize")],
            }),
        };

        request.Headers.Add(TenantHeader, tenant);

        using var saved = await host.Client.SendAsync(request);

        saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
    }

    private static Func<IServiceProvider, Func<List<ChatMessage>, Microsoft.Agents.AI.Workflows.IWorkflowContext, CancellationToken, ValueTask<List<ChatMessage>>>> Speaks(decimal amount)
        => _ => async (messages, context, _) =>
        {
            (await context.ReportUsageAsync(Speech(amount))).ShouldBeTrue();

            return messages;
        };

    private static Func<IServiceProvider, Func<List<ChatMessage>, Microsoft.Agents.AI.Workflows.IWorkflowContext, CancellationToken, ValueTask<List<ChatMessage>>>> Counts(Calls calls)
        => _ => (messages, _, _) =>
        {
            calls.Hit();

            return new ValueTask<List<ChatMessage>>(messages);
        };

    private sealed class Calls
    {
        private int _count;

        public int Count => Volatile.Read(ref _count);

        public void Hit() => Interlocked.Increment(ref _count);
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
