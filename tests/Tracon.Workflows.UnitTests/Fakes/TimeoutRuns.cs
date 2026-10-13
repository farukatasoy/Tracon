using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Tracon.Workflows.UnitTests.Fakes;

/// <summary>
/// The shared setup of the timeout tests (phase 196): a host whose function wrappers
/// and runner read the same <see cref="ManualClock"/>, and the calls that save a chain,
/// run it and read its row.
/// </summary>
internal static class TimeoutRuns
{
    /// <summary>Builds a host whose function registry reads <paramref name="clock"/>.</summary>
    /// <param name="clock">The clock the node timeouts arm their delays on.</param>
    /// <param name="logs">Where the function wrappers log; <see langword="null"/> for none.</param>
    /// <param name="agents">The echo agents of the catalog.</param>
    /// <returns>The host.</returns>
    public static WorkflowTestHost Host(TimeProvider clock, ILoggerProvider? logs = null, params string[] agents)
    {
        var host = new WorkflowTestHost(agents);

        host.FunctionServices = WorkflowTestHost.ServicesFor(configure: services =>
        {
            services.AddSingleton(clock);

            if (logs is not null)
            {
                services.AddLogging(builder => builder.AddProvider(logs));
            }
        });

        return host;
    }

    /// <summary>Saves a Sequential chain of <paramref name="nodes"/> under <paramref name="name"/>.</summary>
    public static async Task SaveAsync(WorkflowTestHost host, string name, params WorkflowNodeReference[] nodes)
        => await host.SaveAsync(new WorkflowDefinition
        {
            Name = name,
            Kind = WorkflowKind.Sequential,
            Nodes = nodes,
        });

    /// <summary>A function node.</summary>
    public static WorkflowNodeReference Function(string name, TimeSpan? timeout = null)
        => new() { Name = name, Kind = WorkflowNodeKind.Function, Timeout = timeout };

    /// <summary>An agent node.</summary>
    public static WorkflowNodeReference Agent(string name, TimeSpan? timeout = null)
        => new() { Name = name, Kind = WorkflowNodeKind.Agent, Timeout = timeout };

    /// <summary>Runs a workflow to its end and returns every event it streamed.</summary>
    public static async Task<List<RunEvent>> Collect(
        WorkflowRunner runner,
        string name,
        string message = "hello",
        CancellationToken cancellationToken = default)
    {
        var events = new List<RunEvent>();

        await foreach (var runEvent in runner.RunStreamingAsync(
            new WorkflowRunRequest { WorkflowName = name, Message = message }, cancellationToken))
        {
            events.Add(runEvent);
        }

        return events;
    }

    /// <summary>Reads the row of the run a stream belongs to.</summary>
    public static async Task<RunRecord> RunOf(WorkflowTestHost host, IReadOnlyList<RunEvent> events)
        => (await host.RunStore.GetRunAsync(events[0].RunId))!;

    /// <summary>Lists the workflow runs of the host's current tenant.</summary>
    public static async Task<IReadOnlyList<RunRecord>> WorkflowRunsAsync(WorkflowTestHost host)
        => [.. (await host.RunStore.QueryRunsAsync(new RunQuery { OnlyRootRuns = false, Take = 100 }))
            .Where(static run => run.Kind == RunKind.Workflow)];
}
