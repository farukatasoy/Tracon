using System.Collections.Concurrent;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using Tracon.TwoNode.IntegrationTests.Infrastructure;

namespace Tracon.TwoNode.IntegrationTests;

/// <summary>
/// Behaviour 6: <c>TraconWorkflowOptions.MaxConcurrentRunsPerTenant</c> holds in the job queue across both nodes.
/// </summary>
/// <remarks>
/// <para>
/// The setting limits a tenant in two places. In each process the limit is exact; in the job queue a worker does not lease
/// a queued workflow job of a tenant that already has that many leased, whichever node leased them, and leases the oldest
/// job of another tenant instead. The queue count is read and not locked, so two workers that lease at the same moment can
/// take a tenant a little past the limit. The in-process half is covered by the functional tests on one host; what only
/// two nodes can show is the queue half.
/// </para>
/// <para>
/// The scenario is built so that the race the documentation names cannot happen: the first job of a tenant is running
/// BEFORE its siblings exist, so the count the workers read is already at the ceiling when they poll.
/// </para>
/// </remarks>
public abstract partial class TwoNodeAcceptanceContract
{
    private const string Flow = "media";

    [Fact]
    public async Task A_tenant_at_its_ceiling_holds_one_slot_across_both_nodes_and_another_tenant_still_runs()
    {
        var gate = new ClusterGate();

        await using var pair = await TwoNodeCluster.StartAsync(
            NewDatabase(),
            configure: static builder => builder.UseTenancy(static options =>
            {
                options.Enabled = true;
                options.AllowHeaderResolution = true;
            }),
            configureA: builder => RegisterGatedWorkflow(builder, gate, "A"),
            configureB: builder => RegisterGatedWorkflow(builder, gate, "B"),
            configureServices: static services =>
            {
                services.Configure<TraconWorkflowOptions>(static options =>
                {
                    options.MaxConcurrentRuns = 8;
                    options.MaxConcurrentRunsPerTenant = 1;
                });
                services.Configure<TraconSchedulingOptions>(static options =>
                {
                    options.PollInterval = TimeSpan.FromMilliseconds(50);
                    options.MaxConcurrentJobs = 8;
                });
            });

        try
        {
            // The first job of "acme" runs on whichever node leases it.
            var first = await QueueWorkflowAsync(pair.A, "acme", "acme-1");
            await WaitUntil.TrueAsync(() => gate.RunningFor("acme") == 1, "the first job of the tenant to hold its slot");

            // Three more of its jobs, then one of another tenant, each posted to a node chosen in turn. Each is older
            // than the next, and every one of them is younger than the job that is running.
            var siblings = new List<Guid>
            {
                await QueueWorkflowAsync(pair.B, "acme", "acme-2"),
                await QueueWorkflowAsync(pair.A, "acme", "acme-3"),
                await QueueWorkflowAsync(pair.B, "acme", "acme-4"),
            };
            var globex = await QueueWorkflowAsync(pair.A, "globex", "globex-1");

            await WaitUntil.TrueAsync(() => gate.RunningFor("globex") == 1, "the other tenant's job to run");

            // Both workers have polled many times by now. The other tenant's job is younger than all three siblings:
            // for it to be leased, the workers passed over the siblings, because "acme" was at its ceiling in the queue.
            // Without the ceiling a sibling is leased first and waits in a process for a slot; it reads Leased or Running.
            var jobs = pair.B.Services.GetRequiredService<IJobStore>();

            gate.RunningFor("acme").ShouldBe(1);
            gate.MostAtOnceFor("acme").ShouldBe(1);

            foreach (var sibling in siblings)
            {
                (await jobs.GetAsync("acme", sibling))!.Status.ShouldBe(JobStatus.Pending);
            }

            gate.Open();

            (await QueuedWorkflowHarness.WaitForJobAsync(pair.A, first, "acme")).Status.ShouldBe(JobStatus.Completed);

            foreach (var sibling in siblings)
            {
                (await QueuedWorkflowHarness.WaitForJobAsync(pair.A, sibling, "acme")).Status.ShouldBe(JobStatus.Completed);
            }

            (await QueuedWorkflowHarness.WaitForJobAsync(pair.A, globex, "globex")).Status.ShouldBe(JobStatus.Completed);

            // On each node the limit is exact: never more than one run of the tenant at once in one process. Across both
            // nodes it is not: once the gate opens the first job leaves, and the two workers lease the siblings from a count
            // that is read and not locked, so both can take one at the same moment. That race is documented (a tenant can pass
            // the limit by up to the number of workers), so the assertion is the exact per-node limit and the sum it allows.
            gate.MostAtOnceFor("acme", "A").ShouldBeLessThanOrEqualTo(1);
            gate.MostAtOnceFor("acme", "B").ShouldBeLessThanOrEqualTo(1);
            gate.MostAtOnceFor("acme").ShouldBeLessThanOrEqualTo(2);
        }
        finally
        {
            gate.Open();
        }
    }

    private static void RegisterGatedWorkflow(ITraconBuilder builder, ClusterGate gate, string node)
    {
        builder.UseWorkflows();

        builder.AddWorkflowFunction<List<ChatMessage>, List<ChatMessage>>(
            "render",
            _ => async (messages, _, _) =>
            {
                var tenant = TraconRunContext.Current!.TenantId!;

                gate.Enter(tenant, node);

                try
                {
                    await gate.Release.Task;

                    return messages;
                }
                finally
                {
                    gate.Leave(tenant, node);
                }
            });

        builder.AddWorkflow(Flow, static services =>
        {
            var render = services.GetWorkflowFunction("render");

            return new WorkflowBuilder(render).WithOutputFrom(render).WithName(Flow).Build();
        });
    }

    private static async Task<Guid> QueueWorkflowAsync(TraconTestHost host, string tenant, string message)
    {
        using var accepted = await QueuedWorkflowHarness.PostAsync(
            host,
            $"/api/workflows/{Flow}/run",
            new WorkflowRunHttpRequest { Message = message },
            idempotencyKey: message,
            tenant: tenant);

        return await QueuedWorkflowHarness.AcceptedRunIdAsync(accepted);
    }

    /// <summary>
    /// The function node every run passes through, on both nodes. It holds a run until the test opens the gate and
    /// counts what runs at once: in total per tenant, and per node.
    /// </summary>
    private sealed class ClusterGate
    {
        private readonly ConcurrentDictionary<string, int> _running = new(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, int> _most = new(StringComparer.Ordinal);

        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int RunningFor(string tenant) => _running.GetValueOrDefault(tenant);

        public int MostAtOnceFor(string tenant) => _most.GetValueOrDefault(tenant);

        public int MostAtOnceFor(string tenant, string node) => _most.GetValueOrDefault(NodeKey(tenant, node));

        public void Open() => Release.TrySetResult();

        public void Enter(string tenant, string node)
        {
            Count(tenant, 1);
            Count(NodeKey(tenant, node), 1);
        }

        public void Leave(string tenant, string node)
        {
            _running.AddOrUpdate(tenant, 0, static (_, current) => current - 1);
            _running.AddOrUpdate(NodeKey(tenant, node), 0, static (_, current) => current - 1);
        }

        private static string NodeKey(string tenant, string node) => $"{tenant}@{node}";

        private void Count(string key, int by)
        {
            var now = _running.AddOrUpdate(key, by, (_, current) => current + by);

            _most.AddOrUpdate(key, now, (_, peak) => Math.Max(peak, now));
        }
    }
}
