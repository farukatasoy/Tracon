using Tracon.Workflows.UnitTests.Fakes;

namespace Tracon.Workflows.UnitTests;

/// <summary>
/// <see cref="IRunEventSink"/> (phase 70) also fans out from a workflow run —
/// <see cref="WorkflowRunner"/> builds its own <see cref="RunEventWriter"/> and
/// must wire the same sinks the agent path does.
/// </summary>
public sealed class WorkflowEventSinkTests
{
    [Fact]
    public async Task A_registered_sink_sees_the_workflow_s_own_events()
    {
        var host = new WorkflowTestHost("writer", "editor");
        var sink = new SpyRunEventSink();

        await host.SaveAsync(new WorkflowDefinition
        {
            Name = "chain",
            Kind = WorkflowKind.Sequential,
            AgentNames = ["writer", "editor"],
        });

        var runner = host.CreateRunner(configure: null, services: null, sinks: [sink]);

        await foreach (var _ in runner.RunStreamingAsync(new WorkflowRunRequest
        {
            WorkflowName = "chain",
            Message = "hello",
        }))
        {
            // drain the stream
        }

        var runs = await host.RunStore.QueryRunsAsync(new RunQuery { OnlyRootRuns = false, Take = 100 });
        var root = runs.Single(static run => run.Kind == RunKind.Workflow);

        var storeSequences = new List<long>();
        await foreach (var runEvent in host.RunStore.ReadEventsAsync(root.Id))
        {
            storeSequences.Add(runEvent.Sequence);
        }

        // The sink saw exactly the workflow's OWN stream — the same sequence
        // numbers the store holds for the root run, in the same order.
        sink.Events.Select(static e => e.Sequence).ShouldBe(storeSequences);
        sink.Events.ShouldContain(e => e.Type == RunEventType.WorkflowStarted);
        sink.Events.ShouldContain(e => e.Type == RunEventType.RunCompleted);
    }

    private sealed class SpyRunEventSink : IRunEventSink
    {
        public List<RunEvent> Events { get; } = [];

        public ValueTask OnEventAsync(RunEvent runEvent, CancellationToken cancellationToken = default)
        {
            Events.Add(runEvent);

            return ValueTask.CompletedTask;
        }
    }
}
