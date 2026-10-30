using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using FakeModelProvider = Tracon.Testing.FakeModelProvider;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A child agent that is still running in the background when its root ends is
/// counted in the tenant's quota the moment it ends, under the root's name.
/// </summary>
/// <remarks>
/// <para>
/// The root read its tree from the run store once, while its own row
/// closed. A background child that had not ended by then had no usage on its row,
/// so its tokens and cost reached no counter, and a root that never waited
/// (<c>start_task</c>, answer, done) left the child's whole spend outside the
/// quota. Waiting for the child, or cancelling it, was not the repair: the
/// task lives with its session and may end in a later turn.
/// </para>
/// <para>
/// The rig is the one of <see cref="AgentTreeQuotaTests"/> with one difference:
/// the router never waits for the task, and the researcher's model answers only
/// after the test opens a gate. "The root closed first" is therefore a fact of
/// the test, not a race. Both the plain and the streaming path run, because the
/// scope lives in an <c>AsyncLocal</c> and the two paths set it differently.
/// </para>
/// </remarks>
public sealed class BackgroundChildQuotaTests
{
    private const string RouterProvider = "routing";
    private const string RouterModel = "router-model";
    private const string GatedProvider = "gated";
    private const string ResearcherModel = "researcher-model";

    private const string StartTask = "background_agents_start_task";

    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(10);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_child_that_ends_after_its_root_is_counted_in_the_roots_counter(bool streaming)
    {
        var gated = new GatedModelProvider(GatedProvider, ResearcherModel);

        await using var host = await StartAsync(gated);

        await RunRouterAsync(host, streaming);

        // The child's model has been called and has not answered: the root is closed
        // and the child is not.
        await gated.Started.WaitAsync(Bound);

        var beforeChild = await DailyCounterAsync(host, "yonlendirici");
        beforeChild.Tokens.ShouldBe(10);
        beforeChild.Runs.ShouldBe(1);

        gated.Open();

        var counter = await WaitUntil.ValueAsync(
            () => DailyCounterAsync(host, "yonlendirici"),
            static record => record.Tokens == 20,
            "the child's tokens to reach the root's counter",
            Bound);

        // The root's own turn and the child's: 4 in x 1000 + 6 out x 2000 per million = 0.016,
        // 4 x 3000 + 6 x 5000 = 0.042. One run, not two.
        counter.Runs.ShouldBe(1);
        counter.Cost.ShouldBe(0.058m);

        // The child counts under the root's name; it never opens a counter of its own.
        (await CountersAsync(host, "arastirmaci")).ShouldBeEmpty();
    }

    [Fact]
    public async Task The_threshold_a_late_child_crosses_is_written_to_the_childs_own_stream()
    {
        var gated = new GatedModelProvider(GatedProvider, ResearcherModel);

        await using var host = await StartAsync(gated, publishToRunStream: true);

        await SaveTokenQuotaAsync(host, maxTokens: 15);
        await RunRouterAsync(host, streaming: false);
        await gated.Started.WaitAsync(Bound);

        // 10 of 15 tokens: no threshold yet, so the root's stream carries no notice.
        gated.Open();

        await WaitUntil.ValueAsync(
            () => DailyCounterAsync(host, "yonlendirici"),
            static record => record.Tokens == 20,
            "the child's tokens to reach the root's counter",
            Bound);

        var runs = host.Services.GetRequiredService<IRunStore>();

        // The counter moves before the notice is appended; the notice comes before the child's
        // terminal event, so a closed child row is the proof it is there to read.
        var child = await WaitUntil.ValueAsync(
            async () => (await runs.QueryRunsAsync(new RunQuery { OnlyRootRuns = false })).Single(static run => run.Depth == 1),
            static run => run.Status == RunStatus.Completed,
            "the child's row to close",
            Bound);

        var root = (await runs.QueryRunsAsync(new RunQuery { OnlyRootRuns = false })).Single(static run => run.Depth == 0);

        // 20 of 15 tokens crosses 100%: the run that spent it carries the notice.
        (await QuotaNoticesAsync(runs, child.Id)).ShouldHaveSingleItem();
        (await QuotaNoticesAsync(runs, root.Id)).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_late_childs_entry_goes_to_the_roots_tenant()
    {
        var gated = new GatedModelProvider(GatedProvider, ResearcherModel);

        await using var host = await StartAsync(gated, tenancy: true);

        // Over HTTP, because the tenant of a request lives in the request: the child ends
        // after the response, where no request is left to name a tenant.
        using (var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/tracon/api/agents/yonlendirici/run", UriKind.Relative))
        {
            Content = JsonContent.Create(new AgentRunRequest { Message = "start" }),
        })
        {
            request.Headers.Add("X-Tracon-Tenant", "tenant-a");

            using var response = await host.Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

            response.StatusCode.ShouldBe(HttpStatusCode.OK);

            _ = await SseReader.ReadAllAsync(await response.Content.ReadAsStreamAsync());
        }

        await gated.Started.WaitAsync(Bound);
        gated.Open();

        var store = host.Services.GetRequiredService<IQuotaStore>();

        await WaitUntil.TrueAsync(
            async () => (await TenantTokensAsync(store, "tenant-a")) == 20,
            "the child's tokens to reach the root's tenant",
            Bound);

        // Nothing leaked into the tenant a task with no request would fall back to.
        var defaultTenant = host.Services.GetRequiredService<IOptions<TraconOptions>>().Value.DefaultTenantId;
        (await TenantTokensAsync(store, defaultTenant)).ShouldBe(0);
    }

    [Fact]
    public async Task The_roots_run_completed_webhook_stays_the_tree_as_it_stood_when_the_root_closed()
    {
        // The earlier decision decided the webhook is NOT the quota's twin: it is a picture of the moment the
        // root closes. A child that ends later is in the counter and in GET /api/runs/{id},
        // and publishes nothing of its own - no second event, no changed payload.
        var gated = new GatedModelProvider(GatedProvider, ResearcherModel);
        var publisher = new CompletedWebhookPublisher();

        await using var host = await StartAsync(gated, webhooks: publisher);

        await RunRouterAsync(host, streaming: false);
        await gated.Started.WaitAsync(Bound);

        gated.Open();

        await WaitUntil.ValueAsync(
            () => DailyCounterAsync(host, "yonlendirici"),
            static record => record.Tokens == 20,
            "the child's tokens to reach the root's counter",
            Bound);

        var run = publisher.Completed.ShouldHaveSingleItem();

        run.AgentName.ShouldBe("yonlendirici");
        run.InputTokens.ShouldBe(4);
        run.OutputTokens.ShouldBe(6);
        run.Cost.ShouldBe(0.016m);

        // The exact total is on the run: the root's row, read after the child ended.
        var runs = host.Services.GetRequiredService<IRunStore>();
        var child = await WaitUntil.ValueAsync(
            async () => (await runs.QueryRunsAsync(new RunQuery { OnlyRootRuns = false })).Single(static row => row.Depth == 1),
            static row => row.Status == RunStatus.Completed,
            "the child's row to close",
            Bound);

        var root = (await runs.GetRunAsync(child.RootRunId ?? child.ParentRunId!.Value)).ShouldNotBeNull();

        root.TreeUsage.ShouldNotBeNull().TotalTokens.ShouldBe(20);
    }

    [Fact]
    public async Task A_workflow_steps_background_task_is_counted_under_the_workflow()
    {
        var gated = new GatedModelProvider(GatedProvider, ResearcherModel);

        await using var host = await StartAsync(gated, workflows: true);

        await WorkflowParameterBindingTests.SaveWorkflowAsync(
            host, agentNames: ["yonlendirici"], workflowParameters: [], name: "orchestrate");

        var run = await WorkflowParameterBindingTests.RunAsync(
            host, "orchestrate", new WorkflowRunHttpRequest { Message = "start" });

        run.Status.ShouldBe(RunStatus.Completed, run.Error?.Message);

        await gated.Started.WaitAsync(Bound);

        // The workflow has closed; the step it ran left a task running. The step's own turn is
        // counted, under the workflow's name, and the workflow is one run.
        var beforeChild = await DailyCounterAsync(host, "orchestrate");
        beforeChild.Tokens.ShouldBe(10);
        beforeChild.Runs.ShouldBe(1);

        gated.Open();

        var counter = await WaitUntil.ValueAsync(
            () => DailyCounterAsync(host, "orchestrate"),
            static record => record.Tokens == 20,
            "the step's background task to reach the workflow's counter",
            Bound);

        counter.Runs.ShouldBe(1);
        counter.Cost.ShouldBe(0.058m);

        (await CountersAsync(host, "yonlendirici")).ShouldBeEmpty();
        (await CountersAsync(host, "arastirmaci")).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_quota_store_that_cannot_write_a_childs_entry_leaves_the_child_completed()
    {
        var gated = new GatedModelProvider(GatedProvider, ResearcherModel);

        // Only the entries that add no run - the children's - fail.
        var quota = new EntryRecordingQuotaStore { FailWhen = static entry => entry.Runs == 0 };

        await using var host = await StartAsync(gated, quota: quota);

        await RunRouterAsync(host, streaming: false);
        await gated.Started.WaitAsync(Bound);

        gated.Open();

        var runs = host.Services.GetRequiredService<IRunStore>();

        // Observability does not break functionality: the child still closes, as it did.
        await WaitUntil.TrueAsync(
            async () => (await runs.QueryRunsAsync(new RunQuery { OnlyRootRuns = false }))
                .Any(static run => run.Depth == 1 && run.Status == RunStatus.Completed),
            "the child to complete although its quota entry could not be written",
            Bound);

        host.Logs.AllText.ShouldContain("Could not write quota consumption", Case.Sensitive);

        // The counter holds the root's entry and nothing of the child's.
        (await DailyCounterAsync(host, "yonlendirici")).Tokens.ShouldBe(10);
    }

    [Fact]
    public async Task A_child_the_deadline_cancels_before_it_spent_anything_writes_no_entry()
    {
        // 144.1 layer 1: the child reads its token and stops. It reported no usage, so it adds
        // nothing: no entry, instead of a store round trip that leaves the counter where it was.
        var hanging = new HangingModelProvider("hanging", respectsCancellation: true);
        var quota = new EntryRecordingQuotaStore();

        await using var host = await StartAsync(
            hanging,
            quota: quota,
            subAgents: new SubAgentSettings { ChildDeadline = TimeSpan.FromMilliseconds(50), WaitTimeout = TimeSpan.FromSeconds(5) });

        await RunRouterAsync(host, streaming: false);

        var runs = host.Services.GetRequiredService<IRunStore>();

        var child = await WaitUntil.ValueAsync(
            async () => (await runs.QueryRunsAsync(new RunQuery { OnlyRootRuns = false })).SingleOrDefault(static run => run.Depth == 1),
            static run => run is { Status: not RunStatus.Running },
            "the child to end at its deadline",
            Bound);

        child!.Status.ShouldBe(RunStatus.Canceled);

        // The root's entry is the only one.
        var only = quota.Entries.ShouldHaveSingleItem();

        only.Runs.ShouldBe(1);
        only.Tokens.ShouldBe(10);
    }

    private static async Task<long> TenantTokensAsync(IQuotaStore store, string tenantId)
    {
        var usage = await store.GetUsageAsync(new QuotaUsageQuery
        {
            TenantId = tenantId,
            AgentName = string.Empty,
            Period = QuotaPeriod.Daily,
        });

        return usage.Sum(static record => record.Tokens);
    }

    private static async Task<IReadOnlyList<RunEvent>> QuotaNoticesAsync(IRunStore runs, Guid runId)
    {
        var notices = new List<RunEvent>();

        await foreach (var runEvent in runs.ReadEventsAsync(runId))
        {
            if (runEvent.Type == RunEventType.Custom &&
                string.Equals(runEvent.CustomType, RunEventCustomTypes.QuotaThreshold, StringComparison.Ordinal))
            {
                notices.Add(runEvent);
            }
        }

        return notices;
    }

    private static async Task SaveTokenQuotaAsync(TraconTestHost host, long maxTokens)
    {
        using var response = await host.Client.PutAsJsonAsync(
            new Uri("/tracon/api/quotas", UriKind.Relative),
            new QuotaSaveRequest
            {
                AgentName = "yonlendirici",
                Period = QuotaPeriod.Daily,
                MaxTokens = maxTokens,
                Enabled = true,
            });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    /// <summary>Runs the router, which starts the task and answers without waiting for it.</summary>
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

    private static Task<TraconTestHost> StartAsync(
        IModelProvider researcher,
        bool publishToRunStream = false,
        bool tenancy = false,
        bool workflows = false,
        EntryRecordingQuotaStore? quota = null,
        SubAgentSettings? subAgents = null,
        IWebhookPublisher? webhooks = null)
        => TraconTestHost.StartAsync(
            configureTracon: builder =>
            {
                var router = new FakeModelProvider(RouterProvider)
                    .WithModel(new ModelDescriptor
                    {
                        Name = RouterModel,
                        InputCostPerMillionTokens = 1_000m,
                        OutputCostPerMillionTokens = 2_000m,
                    })
                    .ForModel(RouterModel, cfg => cfg
                        .CallsTool(StartTask, new { agentName = "arastirmaci", input = "sub task", description = "sub task" })
                        .EchoesLastToolResult("Delegated: ", inputTokens: 4, outputTokens: 6));

                builder.AddModelProvider(router);
                builder.AddModelProvider(researcher);

                if (tenancy)
                {
                    builder.UseTenancy(static options =>
                    {
                        options.Enabled = true;
                        options.AllowHeaderResolution = true;
                    });
                }

                if (workflows)
                {
                    builder.UseWorkflows();
                }

                builder.AddAgent(new AgentDefinition
                {
                    Name = "arastirmaci",
                    Description = "Performs research.",
                    Instructions = "Research.",
                    Model = new ModelBinding { Provider = researcher.Name, Model = researcher.Models[0].Name },
                    Origin = AgentDefinitionOrigin.Code,
                });

                builder.AddAgent(new AgentDefinition
                {
                    Name = "yonlendirici",
                    Description = "Delegates the work.",
                    Instructions = "Delegate.",
                    Model = new ModelBinding { Provider = RouterProvider, Model = RouterModel },
                    CallableAgents = ["arastirmaci"],
                    SubAgents = subAgents,
                    Origin = AgentDefinitionOrigin.Code,
                });
            },
            configureServices: services =>
            {
                if (quota is not null)
                {
                    services.AddSingleton<IQuotaStore>(quota);
                }

                if (webhooks is not null)
                {
                    services.AddSingleton(webhooks);
                }

                services.Configure<TraconQuotaOptions>(options =>
                {
                    options.PublishThresholdToRunStream = publishToRunStream;

                    if (publishToRunStream)
                    {
                        options.ThresholdPercents.Clear();
                        options.ThresholdPercents.Add(100);
                    }
                });
            });

    private sealed class CompletedWebhookPublisher : IWebhookPublisher
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
