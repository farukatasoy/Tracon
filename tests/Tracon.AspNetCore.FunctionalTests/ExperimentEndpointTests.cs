using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using FakeModelProvider = Tracon.Testing.FakeModelProvider;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>In-memory behavior tests for the A/B experiment endpoints (Phase 19.3-19.4).</summary>
public sealed class ExperimentEndpointTests
{
    private static readonly Uri Experiments = new("/tracon/api/experiments", UriKind.Relative);
    private static readonly Uri Experiment = new("/tracon/api/experiments/version-comparison", UriKind.Relative);
    private static readonly Uri Start = new("/tracon/api/experiments/version-comparison/start", UriKind.Relative);
    private static readonly Uri Stop = new("/tracon/api/experiments/version-comparison/stop", UriKind.Relative);
    private static readonly Uri Results = new("/tracon/api/experiments/version-comparison/results", UriKind.Relative);
    private static readonly Uri Canary = new("/tracon/api/experiments/version-comparison/canary", UriKind.Relative);

    [Fact]
    public async Task Experiment_is_created_and_listed()
    {
        await using var host = await TraconTestHost.StartAsync();
        await CreateVersionedAgentAsync(host);

        using var created = await host.Client.PutAsJsonAsync(Experiment, Request());
        created.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await TraconTestHost.ReadJsonAsync(created);
        body.GetProperty("status").GetString().ShouldBe("Draft");
        body.GetProperty("variants").GetArrayLength().ShouldBe(2);

        using var listed = await host.Client.GetAsync(Experiments);
        (await TraconTestHost.ReadJsonAsync(listed)).GetArrayLength().ShouldBe(1);
    }

    [Fact]
    public async Task Rejected_when_weight_total_is_not_100()
    {
        await using var host = await TraconTestHost.StartAsync();
        await CreateVersionedAgentAsync(host);

        using var response = await host.Client.PutAsJsonAsync(
            Experiment,
            Request() with
            {
                Variants =
                [
                    new ExperimentVariant { Name = "control", Version = 1, Weight = 40 },
                    new ExperimentVariant { Name = "v2", Version = 2, Weight = 40 },
                ],
            });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Experiment_cannot_be_set_up_for_a_code_based_agent()
    {
        await using var host = await TraconTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition(name: "code-agent")));

        using var response = await host.Client.PutAsJsonAsync(
            Experiment,
            Request() with { AgentName = "code-agent" });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).ShouldContain("version history");
    }

    [Fact]
    public async Task Variant_with_a_nonexistent_version_is_rejected()
    {
        await using var host = await TraconTestHost.StartAsync();
        await CreateVersionedAgentAsync(host);

        using var response = await host.Client.PutAsJsonAsync(
            Experiment,
            Request() with
            {
                Variants =
                [
                    new ExperimentVariant { Name = "control", Version = 1, Weight = 50 },
                    new ExperimentVariant { Name = "v2", Version = 99, Weight = 50 },
                ],
            });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Lifecycle_starts_and_stops()
    {
        await using var host = await TraconTestHost.StartAsync();
        await CreateVersionedAgentAsync(host);
        await host.Client.PutAsJsonAsync(Experiment, Request());

        using var started = await host.Client.PostAsJsonAsync(Start, new { });
        started.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await TraconTestHost.ReadJsonAsync(started)).GetProperty("status").GetString().ShouldBe("Running");

        using var stopped = await host.Client.PostAsJsonAsync(Stop, new { });
        stopped.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await TraconTestHost.ReadJsonAsync(stopped)).GetProperty("status").GetString().ShouldBe("Stopped");
    }

    [Fact]
    public async Task A_second_running_experiment_for_the_same_agent_is_rejected()
    {
        await using var host = await TraconTestHost.StartAsync();
        await CreateVersionedAgentAsync(host);

        await host.Client.PutAsJsonAsync(Experiment, Request());
        await host.Client.PostAsJsonAsync(Start, new { });

        var secondUri = new Uri("/tracon/api/experiments/second-experiment", UriKind.Relative);
        using (var created = await host.Client.PutAsJsonAsync(secondUri, Request()))
        {
            created.EnsureSuccessStatusCode();
        }

        using var secondStart = await host.Client.PostAsJsonAsync(
            new Uri("/tracon/api/experiments/second-experiment/start", UriKind.Relative), new { });

        secondStart.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Cannot_be_edited_while_running()
    {
        await using var host = await TraconTestHost.StartAsync();
        await CreateVersionedAgentAsync(host);
        await host.Client.PutAsJsonAsync(Experiment, Request());
        await host.Client.PostAsJsonAsync(Start, new { });

        using var response = await host.Client.PutAsJsonAsync(Experiment, Request());

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Results_endpoint_returns_an_empty_list_for_an_empty_experiment()
    {
        await using var host = await TraconTestHost.StartAsync();
        await CreateVersionedAgentAsync(host);
        await host.Client.PutAsJsonAsync(Experiment, Request());

        using var response = await host.Client.GetAsync(Results);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await TraconTestHost.ReadJsonAsync(response);
        body.GetProperty("results").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task Canary_policy_is_defined_and_read()
    {
        await using var host = await TraconTestHost.StartAsync();
        await CreateVersionedAgentAsync(host);
        await host.Client.PutAsJsonAsync(Experiment, Request());

        using var set = await host.Client.PutAsJsonAsync(Canary, CanaryPolicy());
        set.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await TraconTestHost.ReadJsonAsync(set)).GetProperty("canary").GetProperty("canaryVariant").GetString().ShouldBe("v2");

        using var fetched = await host.Client.GetAsync(Canary);
        fetched.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await TraconTestHost.ReadJsonAsync(fetched);
        body.GetProperty("policy").GetProperty("canaryVariant").GetString().ShouldBe("v2");

        // No runs yet -- evaluation returns "insufficient data", NOT a rollback.
        body.GetProperty("evaluation").GetProperty("decision").GetString().ShouldBe("InsufficientData");
    }

    [Fact]
    public async Task Canary_policy_is_removed_with_a_null_body()
    {
        await using var host = await TraconTestHost.StartAsync();
        await CreateVersionedAgentAsync(host);
        await host.Client.PutAsJsonAsync(Experiment, Request());
        await host.Client.PutAsJsonAsync(Canary, CanaryPolicy());

        using var content = new StringContent("null", System.Text.Encoding.UTF8, "application/json");
        using var cleared = await host.Client.PutAsync(Canary, content);
        cleared.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await TraconTestHost.ReadJsonAsync(cleared)).GetProperty("canary").ValueKind.ShouldBe(System.Text.Json.JsonValueKind.Null);

        using var fetched = await host.Client.GetAsync(Canary);
        var body = await TraconTestHost.ReadJsonAsync(fetched);
        body.GetProperty("policy").ValueKind.ShouldBe(System.Text.Json.JsonValueKind.Null);
        body.GetProperty("evaluation").ValueKind.ShouldBe(System.Text.Json.JsonValueKind.Null);
    }

    [Fact]
    public async Task Canary_is_rejected_for_an_experiment_with_other_than_two_variants()
    {
        await using var host = await TraconTestHost.StartAsync();
        await CreateVersionedAgentAsync(host);

        using var created = await host.Client.PutAsJsonAsync(
            Experiment,
            Request() with
            {
                Variants =
                [
                    new ExperimentVariant { Name = "control", Version = 1, Weight = 34 },
                    new ExperimentVariant { Name = "v2", Version = 2, Weight = 33 },
                    new ExperimentVariant { Name = "v3", Version = 1, Weight = 33 },
                ],
            });
        created.EnsureSuccessStatusCode();

        using var response = await host.Client.PutAsJsonAsync(Canary, CanaryPolicy());

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).ShouldContain("two-variant");
    }

    [Fact]
    public async Task Canary_with_a_nonexistent_variant_is_rejected()
    {
        await using var host = await TraconTestHost.StartAsync();
        await CreateVersionedAgentAsync(host);
        await host.Client.PutAsJsonAsync(Experiment, Request());

        using var response = await host.Client.PutAsJsonAsync(Canary, CanaryPolicy() with { CanaryVariant = "missing" });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Canary_for_a_nonexistent_experiment_returns_404()
    {
        await using var host = await TraconTestHost.StartAsync();

        using var response = await host.Client.PutAsJsonAsync(Canary, CanaryPolicy());

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// F-351: a queued run was assigned to no arm and ran the CURRENT version
    /// (2), so a queued run of an agent under an experiment never counted in
    /// the arm its key falls in, and ran instructions the arm did not name.
    /// </summary>
    [Fact]
    public async Task A_queued_run_runs_and_records_the_experiment_arm_it_was_assigned()
    {
        var provider = ArmProvider();
        await using var host = await StartQueuedHostAsync(provider);
        var experimentId = await StartSingleArmExperimentAsync(host);

        using var request = new HttpRequestMessage(HttpMethod.Post, ArmAgentRun)
        {
            Content = JsonContent.Create(new AgentRunRequest { Message = "hi" }),
        };
        request.Headers.Add("Prefer", "respond-async");

        using var accepted = await host.Client.SendAsync(request);
        accepted.StatusCode.ShouldBe(HttpStatusCode.Accepted);

        var runId = (await TraconTestHost.ReadJsonAsync(accepted)).GetProperty("runId").GetGuid();

        await host.WaitForRunStatusAsync(runId, "Completed");

        var run = (await host.Services.GetRequiredService<IRunStore>().GetRunAsync(runId)).ShouldNotBeNull();

        run.AgentVersion.ShouldBe(1);
        run.ExperimentId.ShouldBe(experimentId);
        run.Variant.ShouldBe("control");
        provider.Requests[^1].Options!.Instructions.ShouldBe("first");
    }

    /// <summary>
    /// F-351: an Admin can schedule an <c>AgentRun</c> job with a hand-written
    /// payload. A payload naming an arm whose version it does not carry must
    /// not produce a run recorded under that arm.
    /// </summary>
    [Fact]
    public async Task A_queued_run_job_whose_variant_does_not_name_its_version_fails_closed()
    {
        var provider = ArmProvider();
        await using var host = await StartQueuedHostAsync(provider);
        var experimentId = await StartSingleArmExperimentAsync(host);

        var runs = host.Services.GetRequiredService<IRunStore>();
        var jobs = host.Services.GetRequiredService<IJobStore>();
        var tenantId = host.Services.GetRequiredService<ITenantContext>().TenantId;
        var runId = TraconId.NewId();
        var now = DateTimeOffset.UtcNow;

        await runs.StartRunAsync(new RunStartInfo
        {
            RunId = runId,
            AgentName = ArmAgentName,
            Status = RunStatus.Queued,
            StartedAt = now,
            TenantId = tenantId,
        });

        // "control" is version 1; the forged payload pairs it with version 2.
        await jobs.EnqueueAsync(
            new JobRecord
            {
                Id = runId,
                TenantId = tenantId,
                HandlerKey = JobHandlerKeys.AgentRun,
                TargetName = ArmAgentName,
                Status = JobStatus.Pending,
                Payload = JsonSerializer.SerializeToElement(new
                {
                    runId = runId.ToString(),
                    message = "hi",
                    agentVersion = 2,
                    experimentId = experimentId.ToString(),
                    variant = "control",
                }),
                MaxAttempts = 1,
                ScheduledFor = now,
                CreatedAt = now,
            },
            []);

        var run = await host.WaitForTerminalRunAsync(runId);

        run.Status.ShouldBe(RunStatus.Failed);
        run.ExperimentId.ShouldBeNull();
        run.Variant.ShouldBeNull();
        provider.Requests.ShouldBeEmpty();

        var job = await WaitUntil.ValueAsync(
            () => jobs.GetAsync(tenantId, runId).AsTask(),
            static job => job is { Status: JobStatus.Failed or JobStatus.Completed },
            "the forged job to settle");

        job.ShouldNotBeNull().Status.ShouldBe(JobStatus.Failed);
    }

    private static CanaryPolicy CanaryPolicy()
        => new()
        {
            CanaryVariant = "v2",
            MaxErrorRateDelta = 0.1,
            MinSampleSize = 20,
        };

    private const string ArmAgentName = "arm-agent";

    private static readonly Uri ArmAgentRun = new($"/tracon/api/agents/{ArmAgentName}/run", UriKind.Relative);

    private static FakeModelProvider ArmProvider()
        => new FakeModelProvider("arm-provider").EchoesUserMessage();

    /// <summary>
    /// Starts a host with the queue worker and a database agent with two
    /// versions: version 1 has the instructions "first", version 2 "second".
    /// </summary>
    private static async Task<TraconTestHost> StartQueuedHostAsync(FakeModelProvider provider)
    {
        var host = await TraconTestHost.StartAsync(
            builder => builder.AddModelProvider(provider),
            configureServices: static services => services.UseScheduling(
                static options => options.PollInterval = TimeSpan.FromMilliseconds(20)));

        var model = new ModelBinding { Provider = "arm-provider", Model = "arm-1" };

        using var created = await host.Client.PostAsJsonAsync(
            new Uri("/tracon/api/agents", UriKind.Relative),
            TestData.Request(name: ArmAgentName, instructions: "first") with { Model = model });
        created.EnsureSuccessStatusCode();

        using var updated = await host.Client.PutAsJsonAsync(
            new Uri($"/tracon/api/agents/{ArmAgentName}", UriKind.Relative),
            TestData.Request(name: ArmAgentName, instructions: "second") with { Model = model });
        updated.EnsureSuccessStatusCode();

        return host;
    }

    /// <summary>Starts a one-arm experiment: every key falls in "control", version 1.</summary>
    private static async Task<Guid> StartSingleArmExperimentAsync(TraconTestHost host)
    {
        using var created = await host.Client.PutAsJsonAsync(
            Experiment,
            new ExperimentSaveRequest
            {
                AgentName = ArmAgentName,
                Variants = [new ExperimentVariant { Name = "control", Version = 1, Weight = 100 }],
            });
        created.EnsureSuccessStatusCode();

        using var started = await host.Client.PostAsJsonAsync(Start, new { });
        started.EnsureSuccessStatusCode();

        return (await TraconTestHost.ReadJsonAsync(started)).GetProperty("id").GetGuid();
    }

    /// <summary>Creates a database agent named "customer-support-agent" with two versions.</summary>
    private static async Task CreateVersionedAgentAsync(TraconTestHost host)
    {
        using var created = await host.Client.PostAsJsonAsync(
            new Uri("/tracon/api/agents", UriKind.Relative),
            TestData.Request(name: "customer-support-agent", instructions: "first"));
        created.EnsureSuccessStatusCode();

        using var updated = await host.Client.PutAsJsonAsync(
            new Uri("/tracon/api/agents/customer-support-agent", UriKind.Relative),
            TestData.Request(name: "customer-support-agent", instructions: "second"));
        updated.EnsureSuccessStatusCode();
    }

    private static ExperimentSaveRequest Request()
        => new()
        {
            AgentName = "customer-support-agent",
            Variants =
            [
                new ExperimentVariant { Name = "control", Version = 1, Weight = 50 },
                new ExperimentVariant { Name = "v2", Version = 2, Weight = 50 },
            ],
        };
}
