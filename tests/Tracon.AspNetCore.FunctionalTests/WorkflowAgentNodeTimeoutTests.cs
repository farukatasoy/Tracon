using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using Tracon.Tests.Common;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// An agent node's own timeout (failure modes 24 and 27): it replaces the
/// installation's <c>ChildDeadline</c> for that step, through the real host - DI, the
/// compiler, the wrapper cache and a model that does not answer.
/// </summary>
/// <remarks>
/// <para>
/// The podcast pilot measured a writer step of 1.2 to 5.8 minutes against the two-minute
/// <c>ChildDeadline</c>: before this the only remedy was to raise the installation's
/// value for every workflow. The wrapper cache is keyed by tenant, workflow, agent AND
/// timeout; a key without the tenant leaked one tenant's wrapper to the other,
/// and a key without the timeout would leak one definition's timeout to another.
/// </para>
/// <para>
/// That the hard cutoff follows at the timeout plus 30 seconds, and that a changed
/// timeout keeps the executor identity, is <c>WorkflowAgentNodeTimeoutResumeTests</c> in
/// the workflow unit tests.
/// </para>
/// </remarks>
public sealed class WorkflowAgentNodeTimeoutTests
{
    private const string Slow = "slow";
    private const string TenantA = "tenant-a";
    private const string TenantB = "tenant-b";

    [Fact]
    public async Task An_agent_nodes_timeout_replaces_the_installations_child_deadline_for_that_step()
    {
        var hanging = new HangingModelProvider("hanging", respectsCancellation: true);
        var clock = new ManualClock();

        // The installation's deadline is far away: only the node's can fire.
        await using var host = await StartAsync(hanging, clock, childDeadline: TimeSpan.FromMinutes(10));

        await SaveAsync(host, "steps", timeout: "00:00:00.3");

        var (workflow, waited) = await RunAsync(host, clock, "steps", within: TimeSpan.FromSeconds(1));

        waited.ShouldBe([TimeSpan.FromMilliseconds(300)], "the step ended at its own timeout, not at the installation's");
        workflow.Status.ShouldBe(RunStatus.Failed);
        workflow.Error.ShouldNotBeNull();
        workflow.Error.Type.ShouldBe(TraconChildRunTimeoutException.ChildRunTimeoutErrorType);
        workflow.Error.Class.ShouldBe(RunErrorClass.Timeout);
        workflow.Error.Message.ShouldContain("00:00:00.3");
        workflow.Error.Message.ShouldContain($"the 'timeout' of node '{Slow}'");
    }

    [Fact]
    public async Task A_step_without_a_timeout_of_its_own_keeps_the_installations_child_deadline()
    {
        var hanging = new HangingModelProvider("hanging", respectsCancellation: true);
        var clock = new ManualClock();

        await using var host = await StartAsync(hanging, clock, childDeadline: TimeSpan.FromMilliseconds(300));

        await SaveAsync(host, "steps", timeout: null);

        var (workflow, waited) = await RunAsync(host, clock, "steps", within: TimeSpan.FromSeconds(1));

        waited.ShouldBe([TimeSpan.FromMilliseconds(300)], "the step ended at the installation's deadline");
        workflow.Status.ShouldBe(RunStatus.Failed);
        workflow.Error.ShouldNotBeNull().Type.ShouldBe(TraconChildRunTimeoutException.ChildRunTimeoutErrorType);
        workflow.Error.Message.ShouldContain("'Tracon:AgentGraph:ChildDeadline'");
    }

    [Fact]
    public async Task Two_tenants_workflows_of_one_name_keep_their_own_step_timeouts()
    {
        // Tenant A bounds the step at 300 ms; tenant B sets nothing and gets the
        // installation's 2 seconds. Each tenant runs twice, interleaved, so whichever
        // wrapper the cache built first would reach the other tenant if the key
        // ignored the tenant or the timeout.
        var hanging = new HangingModelProvider("hanging", respectsCancellation: true);
        var clock = new ManualClock();

        await using var host = await StartAsync(hanging, clock, childDeadline: TimeSpan.FromSeconds(2), tenancy: true);

        await SaveAsync(host, "steps", timeout: "00:00:00.3", tenant: TenantA);
        await SaveAsync(host, "steps", timeout: null, tenant: TenantB);

        foreach (var tenant in new[] { TenantA, TenantB, TenantA, TenantB })
        {
            var (workflow, waited) = await RunAsync(host, clock, "steps", within: TimeSpan.FromSeconds(3), tenant);

            workflow.TenantId.ShouldBe(tenant);
            workflow.Status.ShouldBe(RunStatus.Failed);

            if (string.Equals(tenant, TenantA, StringComparison.Ordinal))
            {
                waited.ShouldBe([TimeSpan.FromMilliseconds(300)], "tenant A's step ended at its own timeout");
                workflow.Error!.Message.ShouldContain("the 'timeout' of node");
                workflow.Error.Message.ShouldContain("00:00:00.3");
            }
            else
            {
                waited.ShouldBe([TimeSpan.FromSeconds(2)], "tenant B's step ended at the installation's deadline");
                workflow.Error!.Message.ShouldContain("'Tracon:AgentGraph:ChildDeadline'");
                workflow.Error.Message.ShouldContain("00:00:02");
            }
        }
    }

    private static Task<TraconTestHost> StartAsync(HangingModelProvider hanging, ManualClock clock, TimeSpan childDeadline, bool tenancy = false)
        => TraconTestHost.StartAsync(
            builder =>
            {
                builder
                    .AddModelProvider(hanging)
                    .AddAgent(TestData.Definition(Slow) with
                    {
                        Model = new ModelBinding { Provider = hanging.Name, Model = "hanging-1" },
                    })
                    .UseWorkflows()
                    .AddWorkflowFunction<List<ChatMessage>, List<ChatMessage>>(
                        "after",
                        static _ => static (messages, _, _) => new ValueTask<List<ChatMessage>>(messages));

                if (tenancy)
                {
                    builder.UseTenancy(static options =>
                    {
                        options.Enabled = true;
                        options.AllowHeaderResolution = true;
                    });
                }
            },
            configureServices: services =>
            {
                services.AddSingleton<TimeProvider>(clock);
                services.Configure<TraconOptions>(options =>
                {
                    options.AgentGraph.ChildDeadline = childDeadline;
                    options.AgentGraph.WaitTimeout = childDeadline + TimeSpan.FromSeconds(30);
                });
            });

    private static async Task SaveAsync(TraconTestHost host, string name, string? timeout, string? tenant = null)
    {
        object slow = timeout is null
            ? new { name = Slow, kind = "Agent" }
            : new { name = Slow, kind = "Agent", timeout };

        using var request = new HttpRequestMessage(HttpMethod.Put, $"/tracon/api/workflows/{name}")
        {
            Content = JsonContent.Create(new { kind = "Sequential", nodes = new[] { slow, new { name = "after", kind = "Function" } } }),
        };

        if (tenant is not null)
        {
            request.Headers.Add("X-Tracon-Tenant", tenant);
        }

        using var saved = await host.Client.SendAsync(request);

        saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// Runs the workflow and moves the host's clock to the timers that fall within <paramref name="within"/>, the step's
    /// deadline among them, and no further.
    /// </summary>
    /// <returns>The run, and the amounts the clock was moved by for this run.</returns>
    private static async Task<(RunRecord Run, IReadOnlyList<TimeSpan> Waited)> RunAsync(
        TraconTestHost host,
        ManualClock clock,
        string name,
        TimeSpan within,
        string? tenant = null)
    {
        var movedBefore = clock.Steps.Count;
        var running = StartRunAsync(host, name, tenant);

        await clock.AdvanceUntilAsync(running, within);

        return (await running, [.. clock.Steps.Skip(movedBefore)]);
    }

    private static async Task<RunRecord> StartRunAsync(TraconTestHost host, string name, string? tenant)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/tracon/api/workflows/{name}/run")
        {
            Content = JsonContent.Create(new WorkflowRunHttpRequest { Message = "write the episode" }),
        };

        if (tenant is not null)
        {
            request.Headers.Add("X-Tracon-Tenant", tenant);
        }

        using var response = await host.Client.SendAsync(request);
        var frames = await SseReader.ReadAllAsync(await response.Content.ReadAsStreamAsync());
        var runId = System.Text.Json.JsonDocument
            .Parse(frames.First(static frame => string.Equals(frame.Event, "run", StringComparison.Ordinal)).Data)
            .RootElement.GetProperty("runId").GetGuid();

        // The store reads under the ambient tenant; outside a request that is the
        // default one, so another tenant's row needs its own scope.
        using var scope = tenant is null ? null : AmbientTenantScope.Begin(tenant);

        return (await host.Services.GetRequiredService<IRunStore>().GetRunAsync(runId)).ShouldNotBeNull();
    }
}
