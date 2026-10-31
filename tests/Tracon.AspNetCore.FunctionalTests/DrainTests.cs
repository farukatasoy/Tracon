using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>Tests for the graceful-shutdown drain gate on the run-starting endpoints.</summary>
public sealed class DrainTests
{
    private const string PreferHeaderName = "Prefer";

    private static readonly Uri Run = new("/tracon/api/agents/kod-agent/run", UriKind.Relative);
    private static readonly Uri WorkflowRun = new("/tracon/api/workflows/chain/run", UriKind.Relative);

    [Fact]
    public async Task Not_draining_by_default_run_is_accepted()
    {
        await using var host = await TraconTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        using var response = await host.Client.PostAsJsonAsync(Run, new AgentRunRequest { Message = "hello" });

        response.StatusCode.ShouldNotBe(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task Draining_rejects_a_new_streaming_run_with_503()
    {
        await using var host = await TraconTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()),
            configureServices: static services => services.AddSingleton<ITraconDrainState>(new FixedDrainState(isDraining: true)));

        using var response = await host.Client.PostAsJsonAsync(Run, new AgentRunRequest { Message = "hello" });

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);

        var problem = await TraconTestHost.ReadJsonAsync(response);
        problem.GetProperty("title").GetString().ShouldBe("Service is shutting down");
    }

    [Fact]
    public async Task Draining_rejects_a_new_queued_run_with_503()
    {
        await using var host = await TraconTestHost.StartAsync(
            static builder =>
            {
                builder.AddAgent(TestData.Definition());
                builder.UseScheduling(static options => options.RunWorker = false);
            },
            configureServices: static services =>
            {
                services.AddSingleton<ITraconDrainState>(new FixedDrainState(isDraining: true));
            });

        using var request = new HttpRequestMessage(HttpMethod.Post, Run)
        {
            Content = JsonContent.Create(new AgentRunRequest { Message = "hello" }),
        };
        request.Headers.Add(PreferHeaderName, "respond-async");

        using var response = await host.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task Draining_stops_the_worker_from_leasing_a_pending_job_then_resumes_once_it_ends()
    {
        var drainState = new MutableDrainState();

        await using var host = await TraconTestHost.StartAsync(
            static builder =>
            {
                builder.AddAgent(TestData.Definition());
                builder.UseScheduling(static options => options.PollInterval = TimeSpan.FromMilliseconds(20));
            },
            configureServices: services =>
            {
                services.AddSingleton<ITraconDrainState>(drainState);
            });

        var jobStore = host.Services.GetRequiredService<IJobStore>();
        var runStore = host.Services.GetRequiredService<IRunStore>();
        var tenantContext = host.Services.GetRequiredService<ITenantContext>();

        var runId = TraconId.NewId();
        var now = DateTimeOffset.UtcNow;

        await runStore.StartRunAsync(new RunStartInfo
        {
            RunId = runId,
            AgentName = "kod-agent",
            Status = RunStatus.Queued,
            StartedAt = now,
            TenantId = tenantContext.TenantId,
        });

        drainState.IsDraining = true;

        var job = await jobStore.EnqueueAsync(
            new JobRecord
            {
                Id = runId,
                TenantId = tenantContext.TenantId,
                HandlerKey = JobHandlerKeys.AgentRun,
                TargetName = "kod-agent",
                Status = JobStatus.Pending,
                Payload = JsonSerializer.SerializeToElement(new { runId = runId.ToString(), message = "hello" }),
                ScheduledFor = now,
                CreatedAt = now,
            },
            []);

        // Give the worker several ticks to (wrongly) pick this up. This used to be
        // a fixed 200 ms, which under load could pass without
        // a single tick - a green test that proved nothing. Every tick reads
        // the drain state first, so counting reads counts refused ticks.
        var readsBefore = drainState.Reads;
        await WaitUntil.TrueAsync(
            () => drainState.Reads >= readsBefore + 3,
            "the worker to tick three times while draining");

        (await jobStore.GetAsync(tenantContext.TenantId, job.Id)).ShouldNotBeNull().Status.ShouldBe(JobStatus.Pending);

        drainState.IsDraining = false;

        var settled = await WaitUntil.ValueAsync(
            async () => (await jobStore.GetAsync(tenantContext.TenantId, job.Id)).ShouldNotBeNull().Status,
            static status => status is not (JobStatus.Pending or JobStatus.Leased or JobStatus.Running),
            "the job to settle once the drain ended");

        settled.ShouldBe(JobStatus.Completed);
    }

    // --- an earlier fix: the workflow endpoints start runs too ---
    //
    // Each endpoint gets its own test: they do not share a filter, so a test of
    // one says nothing about another (the RunAuthorizationEndpointTests rule).

    [Fact]
    public async Task Draining_rejects_a_new_streamed_workflow_run_with_503_and_opens_no_run_row()
    {
        var drainState = new MutableDrainState();
        await using var host = await StartWorkflowHostAsync(drainState);
        await SaveChainAsync(host);

        drainState.IsDraining = true;

        using var response = await host.Client.PostAsJsonAsync(
            WorkflowRun, new WorkflowRunHttpRequest { Message = "hello" });

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable, await response.Content.ReadAsStringAsync());
        (await TraconTestHost.ReadJsonAsync(response)).GetProperty("title").GetString().ShouldBe("Service is shutting down");
        (await CountRunsAsync(host)).ShouldBe(0);
    }

    [Fact]
    public async Task Draining_rejects_a_new_queued_workflow_run_with_503_and_queues_no_job()
    {
        var drainState = new MutableDrainState();
        await using var host = await StartWorkflowHostAsync(drainState, runWorker: false);
        await SaveChainAsync(host);

        drainState.IsDraining = true;

        using var request = new HttpRequestMessage(HttpMethod.Post, WorkflowRun)
        {
            Content = JsonContent.Create(new WorkflowRunHttpRequest { Message = "hello" }),
        };
        request.Headers.Add(PreferHeaderName, "respond-async");

        using var response = await host.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable, await response.Content.ReadAsStringAsync());
        (await CountRunsAsync(host)).ShouldBe(0);

        var tenantContext = host.Services.GetRequiredService<ITenantContext>();
        (await host.Services.GetRequiredService<IJobStore>().QueryAsync(new JobQuery { TenantId = tenantContext.TenantId })).ShouldBeEmpty();
    }

    [Fact]
    public async Task Draining_rejects_a_workflow_resume_with_503_and_opens_no_run_row()
    {
        var drainState = new MutableDrainState();
        await using var host = await StartWorkflowHostAsync(drainState);
        await SaveChainAsync(host);
        var source = await RunChainAsync(host);

        drainState.IsDraining = true;

        using var response = await host.Client.PostAsJsonAsync(
            new Uri($"/tracon/api/workflows/runs/{source}/resume", UriKind.Relative),
            new WorkflowResumeHttpRequest());

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable, await response.Content.ReadAsStringAsync());
        (await CountRunsAsync(host)).ShouldBe(1, "only the source run exists");
    }

    [Fact]
    public async Task Draining_rejects_a_workflow_respond_with_503_and_opens_no_run_row()
    {
        var drainState = new MutableDrainState();
        await using var host = await StartWorkflowHostAsync(drainState);
        await SaveChainAsync(host);
        var source = await RunChainAsync(host);

        drainState.IsDraining = true;

        using var response = await host.Client.PostAsJsonAsync(
            new Uri($"/tracon/api/workflows/runs/{source}/respond", UriKind.Relative),
            new WorkflowRespondHttpRequest { RequestId = "any" });

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable, await response.Content.ReadAsStringAsync());
        (await CountRunsAsync(host)).ShouldBe(1, "only the source run exists");
    }

    [Fact]
    public async Task Draining_rejects_a_run_replay_with_503_and_opens_no_run_row()
    {
        var drainState = new MutableDrainState();
        await using var host = await TraconTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()),
            configureServices: services => services.AddSingleton<ITraconDrainState>(drainState));

        using (var first = await host.Client.PostAsJsonAsync(Run, new AgentRunRequest { Message = "hello" }))
        {
            first.StatusCode.ShouldBe(HttpStatusCode.OK, await first.Content.ReadAsStringAsync());
            _ = await first.Content.ReadAsStringAsync();
        }

        var runs = host.Services.GetRequiredService<IRunStore>();
        var source = (await runs.QueryRunsAsync(new RunQuery { Take = 10 })).ShouldHaveSingleItem().Id;

        drainState.IsDraining = true;

        using var response = await host.Client.PostAsJsonAsync(
            new Uri($"/tracon/api/runs/{source}/replay", UriKind.Relative), new { toolMode = "NoTools" });

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable, await response.Content.ReadAsStringAsync());
        (await runs.QueryRunsAsync(new RunQuery { Take = 10 })).ShouldHaveSingleItem("only the source run exists");
    }

    [Fact]
    public async Task Draining_rejects_a_chat_completion_with_an_OpenAI_shaped_503_and_opens_no_run_row()
    {
        await using var host = await TraconTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()),
            configureServices: static services => services.AddSingleton<ITraconDrainState>(new FixedDrainState(isDraining: true)));

        using var response = await host.Client.PostAsJsonAsync(
            new Uri("/tracon/v1/chat/completions", UriKind.Relative),
            new { model = "kod-agent", messages = new[] { new { role = "user", content = "hello" } } });

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable, await response.Content.ReadAsStringAsync());
        (await TraconTestHost.ReadJsonAsync(response)).GetProperty("error").GetProperty("type").GetString().ShouldBe("service_unavailable");
        (await host.Services.GetRequiredService<IRunStore>().QueryRunsAsync(new RunQuery { Take = 10 })).ShouldBeEmpty();
    }

    [Fact]
    public async Task Draining_rejects_a_response_with_an_OpenAI_shaped_503_and_opens_no_run_row()
    {
        await using var host = await TraconTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()),
            configureServices: static services => services.AddSingleton<ITraconDrainState>(new FixedDrainState(isDraining: true)));

        using var response = await host.Client.PostAsJsonAsync(
            new Uri("/tracon/v1/responses", UriKind.Relative),
            new { model = "kod-agent", input = "hello" });

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable, await response.Content.ReadAsStringAsync());
        (await TraconTestHost.ReadJsonAsync(response)).GetProperty("error").GetProperty("type").GetString().ShouldBe("service_unavailable");
        (await host.Services.GetRequiredService<IRunStore>().QueryRunsAsync(new RunQuery { Take = 10 })).ShouldBeEmpty();
    }

    [Fact]
    public async Task Draining_refuses_the_voice_conversation_handshake_with_503()
    {
        await using var host = await TraconTestHost.StartAsync(
            static builder => builder
                .AddAgent(TestData.Definition("code-agent"))
                .UseVoiceConversation(static options => options.OutputMediaType = "audio/mpeg"),
            configureServices: static services =>
            {
                var voice = new CountingSpeechProvider("test-voice");
                services.AddSingleton<ISpeechTranscriber>(voice);
                services.AddSingleton<ISpeechSynthesizer>(voice);
                services.AddSingleton<ITraconDrainState>(new FixedDrainState(isDraining: true));
            });

        var socketClient = host.CreateWebSocketClient();
        socketClient.SubProtocols.Add(VoiceConversationProtocol.SubProtocol);

        // A refused handshake surfaces in TestServer as an exception whose message
        // carries the status (VoiceConversationTenantPolicyTests).
        var refused = await Should.ThrowAsync<InvalidOperationException>(async () => await socketClient.ConnectAsync(
            new Uri("http://localhost/tracon/api/voice/sessions/session-1/stream"),
            TestContext.Current.CancellationToken));

        refused.Message.ShouldContain("503");
    }

    private static Task<TraconTestHost> StartWorkflowHostAsync(MutableDrainState drainState, bool runWorker = true)
        => TraconTestHost.StartAsync(
            builder =>
            {
                builder
                    .AddAgent(TestData.Definition("writer"))
                    .AddAgent(TestData.Definition("editor"))
                    .UseWorkflows();
                builder.UseScheduling(options => options.RunWorker = runWorker);
            },
            configureServices: services =>
            {
                services.AddSingleton<ITraconDrainState>(drainState);
            });

    private static async Task SaveChainAsync(TraconTestHost host)
    {
        using var saved = await host.Client.PutAsJsonAsync(
            "/tracon/api/workflows/chain",
            new WorkflowSaveRequest { Kind = WorkflowKind.Sequential, AgentNames = ["writer", "editor"] });

        saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
    }

    private static async Task<Guid> RunChainAsync(TraconTestHost host)
    {
        using (var response = await host.Client.PostAsJsonAsync(WorkflowRun, new WorkflowRunHttpRequest { Message = "hello" }))
        {
            response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
            _ = await response.Content.ReadAsStringAsync();
        }

        return (await host.Services.GetRequiredService<IRunStore>()
                .QueryRunsAsync(new RunQuery { Take = 20 }))
            .Single(static run => run.Kind == RunKind.Workflow)
            .Id;
    }

    private static async Task<int> CountRunsAsync(TraconTestHost host)
        => (await host.Services.GetRequiredService<IRunStore>()
                .QueryRunsAsync(new RunQuery { Take = 50 }))
            .Count(static run => run.Kind == RunKind.Workflow);

    private sealed class MutableDrainState : ITraconDrainState
    {
        private volatile bool _isDraining;
        private int _reads;

        /// <summary>How many times anything asked whether the process is draining.</summary>
        public int Reads => Volatile.Read(ref _reads);

        public bool IsDraining
        {
            get
            {
                Interlocked.Increment(ref _reads);

                return _isDraining;
            }

            set => _isDraining = value;
        }
    }
}
