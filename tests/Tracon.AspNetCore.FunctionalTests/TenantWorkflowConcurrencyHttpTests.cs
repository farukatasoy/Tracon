using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// Phase 240: <c>TraconWorkflowOptions.MaxConcurrentRunsPerTenant</c>, through the real host: the HTTP status of
/// a run that waits for a slot, its cancellation, and the job worker's lease order.
/// </summary>
/// <remarks>
/// <para>
/// Two layers carry the one setting, and each has a case that only it can pass. <em>In the process</em>, a
/// run beyond the tenant's limit waits <c>Queued</c> and a cancel reaches it: the cancel endpoint used to send
/// every <c>Queued</c> run to the job queue, where a run that is waiting inside the process has no job to
/// cancel and the call would have answered "already ended". <em>In the queue</em>, the worker leases the
/// oldest job of a tenant that is under the ceiling: the older jobs of the tenant at it stay <c>Pending</c>
/// instead of being leased only to wait in the process for a slot.
/// </para>
/// </remarks>
public sealed class TenantWorkflowConcurrencyHttpTests
{
    private const string Flow = "media";

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(15);

    [Fact]
    public async Task A_run_waiting_for_its_tenants_slot_reads_Queued_over_HTTP_and_a_cancel_reaches_it()
    {
        await WithHostAsync(maxConcurrentRuns: 2, perTenant: 1, async (host, node) =>
        {
            var holder = await StreamAsync(host, "acme", "holder");
            await WaitUntil.TrueAsync(() => node.Running == 1, "the tenant's first run to hold its slot");

            var waiter = await StreamAsync(host, "acme", "waiter");

            (await WaitForStatusAsync(host, waiter.RunId, "acme", expected: "Queued")).ShouldBe("Queued");

            // Another tenant is not held up by it, and reads Running.
            var other = await StreamAsync(host, "globex", "other");
            await WaitUntil.TrueAsync(() => node.Running == 2, "the other tenant's run to start");

            (await WaitForStatusAsync(host, other.RunId, "globex", expected: "Running")).ShouldBe("Running");
            (await StatusAsync(host, waiter.RunId, "acme")).ShouldBe("Queued");

            using var cancelled = await SendAsync(host, HttpMethod.Post, $"/tracon/api/runs/{waiter.RunId}/cancel", "acme");

            cancelled.StatusCode.ShouldBe(HttpStatusCode.Accepted, await cancelled.Content.ReadAsStringAsync());
            (await WaitForStatusAsync(host, waiter.RunId, "acme", expected: "Canceled")).ShouldBe("Canceled");

            // It never ran, and the slot it queued for is still the holder's.
            node.Calls.ShouldBe(2);

            node.Open();
            await Task.WhenAll(holder.Rest, other.Rest, waiter.Rest);

            (await StatusAsync(host, holder.RunId, "acme")).ShouldBe("Completed");
            (await StatusAsync(host, other.RunId, "globex")).ShouldBe("Completed");
        });
    }

    [Fact]
    public async Task The_worker_leaves_the_older_jobs_of_a_tenant_at_its_ceiling_in_the_queue_and_runs_another_tenants_job()
    {
        await WithHostAsync(maxConcurrentRuns: 8, perTenant: 1, async (host, node) =>
        {
            // Three jobs of one tenant, then one of another, each older than the next.
            var acme = new List<Guid>();

            foreach (var index in Enumerable.Range(1, 3))
            {
                acme.Add(await QueueAsync(host, "acme", $"acme-{index}"));
            }

            var globex = await QueueAsync(host, "globex", "globex-1");

            await WaitUntil.TrueAsync(() => node.RunningFor("globex") == 1, "the other tenant's job to run");

            // The other tenant's job is younger than all three: for it to have been leased, the worker passed
            // over the two acme jobs that follow the first, because acme was at its ceiling. Without the ceiling
            // they are leased first and wait inside the process for a slot, and they read Leased or Running.
            var jobs = host.Services.GetRequiredService<IJobStore>();

            node.RunningFor("acme").ShouldBe(1);
            (await jobs.GetAsync("acme", acme[1]))!.Status.ShouldBe(JobStatus.Pending);
            (await jobs.GetAsync("acme", acme[2]))!.Status.ShouldBe(JobStatus.Pending);

            node.Open();

            foreach (var jobId in acme)
            {
                (await QueuedWorkflowHarness.WaitForJobAsync(host, jobId, "acme")).Status.ShouldBe(JobStatus.Completed);
            }

            (await QueuedWorkflowHarness.WaitForJobAsync(host, globex, "globex")).Status.ShouldBe(JobStatus.Completed);
            node.MostAtOnceFor("acme").ShouldBe(1);
        });
    }

    [Fact]
    public async Task With_no_tenant_limit_the_worker_leases_every_job_in_age_order()
    {
        await WithHostAsync(maxConcurrentRuns: 8, perTenant: null, async (host, node) =>
        {
            var acme = new List<Guid>();

            foreach (var index in Enumerable.Range(1, 3))
            {
                acme.Add(await QueueAsync(host, "acme", $"acme-{index}"));
            }

            // The behaviour before the setting existed: nothing holds the tenant back.
            await WaitUntil.TrueAsync(() => node.RunningFor("acme") == 3, "all three jobs of the tenant to run at once");

            node.Open();

            foreach (var jobId in acme)
            {
                (await QueuedWorkflowHarness.WaitForJobAsync(host, jobId, "acme")).Status.ShouldBe(JobStatus.Completed);
            }
        });
    }

    /// <summary>
    /// Runs a test against a host and always opens the gate before the host stops: a run held inside the node would
    /// keep a failed test from ever finishing, and the failure would never be read.
    /// </summary>
    private static async Task WithHostAsync(int maxConcurrentRuns, int? perTenant, Func<TraconTestHost, GateNode, Task> test)
    {
        var node = new GateNode();

        await using var host = await StartAsync(node, maxConcurrentRuns, perTenant);

        try
        {
            await test(host, node);
        }
        finally
        {
            node.Open();
        }
    }

    private static Task<TraconTestHost> StartAsync(GateNode node, int maxConcurrentRuns, int? perTenant)
        => NodeUsageHost.StartAsync(
            builder =>
            {
                builder.UseTenancy(static options =>
                {
                    options.Enabled = true;
                    options.AllowHeaderResolution = true;
                });

                builder.AddWorkflowFunction<List<ChatMessage>, List<ChatMessage>>(
                    "render",
                    _ => async (messages, _, _) =>
                    {
                        var tenant = TraconRunContext.Current!.TenantId!;

                        node.Enter(tenant);

                        try
                        {
                            await node.Gate.Task;

                            return messages;
                        }
                        finally
                        {
                            node.Leave(tenant);
                        }
                    });

                builder.AddWorkflow(Flow, Graph);
            },
            services =>
            {
                services.Configure<TraconWorkflowOptions>(options =>
                {
                    options.MaxConcurrentRuns = maxConcurrentRuns;
                    options.MaxConcurrentRunsPerTenant = perTenant;
                });
                services.Configure<TraconSchedulingOptions>(options =>
                {
                    options.PollInterval = TimeSpan.FromMilliseconds(50);
                    options.MaxConcurrentJobs = 8;
                });
            });

    private static Workflow Graph(IServiceProvider services)
    {
        var render = services.GetWorkflowFunction("render");

        return new WorkflowBuilder(render).WithOutputFrom(render).WithName(Flow).Build();
    }

    /// <summary>Starts a streamed run and returns once the stream names it; the rest is read in the background.</summary>
    private static async Task<StreamedRun> StreamAsync(TraconTestHost host, string tenant, string message)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/tracon/api/workflows/{Flow}/run")
        {
            Content = JsonContent.Create(new WorkflowRunHttpRequest { Message = message }),
        };
        request.Headers.Add("X-Tracon-Tenant", tenant);

        var response = await host.Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead)
            .WaitAsync(Patience, TestContext.Current.CancellationToken);

        // The body is read only when the status is wrong: a streamed run's body ends with the run, and reading it here
        // as the failure message would wait for the run to finish.
        if (response.StatusCode != HttpStatusCode.OK)
        {
            throw new InvalidOperationException($"The run was refused with {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        }

        var frames = SseReader.ReadAsync(await response.Content.ReadAsStreamAsync()).GetAsyncEnumerator();

        while (await frames.MoveNextAsync().AsTask().WaitAsync(Patience, TestContext.Current.CancellationToken))
        {
            if (string.Equals(frames.Current.Event, "run", StringComparison.Ordinal))
            {
                var runId = JsonDocument.Parse(frames.Current.Data).RootElement.GetProperty("runId").GetGuid();

                return new StreamedRun(runId, DrainAsync(frames, request, response));
            }
        }

        throw new InvalidOperationException("The stream ended before it named the run.");
    }

    private static async Task DrainAsync(IAsyncEnumerator<SseFrame> frames, HttpRequestMessage request, HttpResponseMessage response)
    {
        try
        {
            while (await frames.MoveNextAsync())
            {
            }
        }
        finally
        {
            await frames.DisposeAsync();
            response.Dispose();
            request.Dispose();
        }
    }

    private static async Task<Guid> QueueAsync(TraconTestHost host, string tenant, string message)
    {
        using var accepted = await QueuedWorkflowHarness.PostAsync(
            host,
            $"/api/workflows/{Flow}/run",
            new WorkflowRunHttpRequest { Message = message },
            idempotencyKey: message,
            tenant: tenant);

        return await QueuedWorkflowHarness.AcceptedRunIdAsync(accepted);
    }

    private static async Task<HttpResponseMessage> SendAsync(TraconTestHost host, HttpMethod method, string path, string tenant)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Add("X-Tracon-Tenant", tenant);

        return await host.Client.SendAsync(request);
    }

    /// <summary>The status <c>GET /api/runs/{id}</c> reports, or <see langword="null"/> while the run has no row.</summary>
    private static async Task<string?> StatusAsync(TraconTestHost host, Guid runId, string tenant)
    {
        using var response = await SendAsync(host, HttpMethod.Get, $"/tracon/api/runs/{runId}", tenant);

        if (response.StatusCode != HttpStatusCode.OK)
        {
            return null;
        }

        return (await TraconTestHost.ReadJsonAsync(response)).GetProperty("status").GetString();
    }

    private static async Task<string?> WaitForStatusAsync(TraconTestHost host, Guid runId, string tenant, string expected)
        => await WaitUntil.ValueAsync(
            () => StatusAsync(host, runId, tenant),
            status => string.Equals(status, expected, StringComparison.Ordinal),
            $"run {runId} to read '{expected}'");

    private sealed record StreamedRun(Guid RunId, Task Rest);

    /// <summary>The function node every run passes through; it holds the run until the test opens the gate.</summary>
    private sealed class GateNode
    {
        private readonly ConcurrentDictionary<string, int> _running = new(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, int> _most = new(StringComparer.Ordinal);
        private int _calls;

        public TaskCompletionSource Gate { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int Running => _running.Values.Sum();

        public int Calls => Volatile.Read(ref _calls);

        public int RunningFor(string tenant) => _running.GetValueOrDefault(tenant);

        public int MostAtOnceFor(string tenant) => _most.GetValueOrDefault(tenant);

        public void Open() => Gate.TrySetResult();

        public void Enter(string tenant)
        {
            Interlocked.Increment(ref _calls);

            var now = _running.AddOrUpdate(tenant, 1, static (_, current) => current + 1);

            _most.AddOrUpdate(tenant, now, (_, peak) => Math.Max(peak, now));
        }

        public void Leave(string tenant) => _running.AddOrUpdate(tenant, 0, static (_, current) => current - 1);
    }
}
