using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Tracon.Workflows.UnitTests.Fakes;

namespace Tracon.Workflows.UnitTests;

/// <summary>
/// A run that names a definition version compiles and stamps that version.
/// </summary>
/// <remarks>
/// <para>
/// A queued run that the recovery restarts with no checkpoint to resume from used to compile the
/// workflow as it reads NOW, so an edit made between the interrupted attempt and the restart
/// changed what ran under the same session. The restart now names the version the interrupted
/// attempt stamped on its row. This is the runner's half of that promise: the version named on a
/// <see cref="WorkflowRunRequest"/> is the one compiled, and one the workflow does not have is
/// refused, never replaced by the current definition (the same rule a resume follows).
/// </para>
/// <para>
/// The function node speaks the voice its settings name, so what a run was really given is visible
/// in the voices the node saw - the same observable <see cref="WorkflowResumePinTests"/> uses.
/// </para>
/// </remarks>
public sealed class WorkflowRunPinTests
{
    [Fact]
    public async Task A_run_that_names_a_version_runs_that_versions_settings_and_stamps_it()
    {
        var (host, voices) = Host();

        await host.SaveAsync(Narrated("alloy"));
        await host.SaveAsync(Narrated("nova"));

        await RunAsync(host.CreateRunner(), definitionVersion: 1);

        voices.ToArray().ShouldBe(["alloy"]);
        (await RootAsync(host)).AgentVersion.ShouldBe(1);
    }

    [Fact]
    public async Task A_run_that_names_no_version_runs_the_current_definition()
    {
        var (host, voices) = Host();

        await host.SaveAsync(Narrated("alloy"));
        await host.SaveAsync(Narrated("nova"));

        await RunAsync(host.CreateRunner(), definitionVersion: null);

        voices.ToArray().ShouldBe(["nova"]);
        (await RootAsync(host)).AgentVersion.ShouldBe(2);
    }

    [Fact]
    public async Task A_run_that_names_a_version_the_workflow_does_not_have_fails_closed()
    {
        var (host, voices) = Host();

        await host.SaveAsync(Narrated("alloy"));

        var events = await RunAsync(host.CreateRunner(), definitionVersion: 9);

        // The run id the endpoint announces must point at a run, so the row opens and closes failed.
        events.First().Type.ShouldBe(RunEventType.RunStarted);
        events.Single(static runEvent => runEvent.Type == RunEventType.RunFailed).Text!
            .ShouldContain("has no version 9", Case.Sensitive);
        voices.ToArray().ShouldBeEmpty("the current definition did not run in its place");

        var root = await RootAsync(host);

        root.Status.ShouldBe(RunStatus.Failed);
        root.AgentVersion.ShouldBeNull();
    }

    [Fact]
    public async Task A_workflow_defined_in_code_ignores_a_named_version()
    {
        // Code workflows have no versions: a restart of one carries no stamp, so no version is named.
        var host = new WorkflowTestHost();
        var runner = host.CreateRunner(configure: null, services: null, ApprovalWorkflow.Registration());

        await foreach (var _ in runner.RunStreamingAsync(
            new WorkflowRunRequest { WorkflowName = "approval-flow", Message = "go", DefinitionVersion = 3 }))
        {
            // Drained.
        }

        var root = await RootAsync(host);

        root.AgentVersion.ShouldBeNull();
        root.Status.ShouldBe(RunStatus.AwaitingInput);
    }

    private static (WorkflowTestHost Host, ConcurrentQueue<string> Voices) Host()
    {
        var host = new WorkflowTestHost("writer");
        var voices = new ConcurrentQueue<string>();

        host.AddFunction<List<ChatMessage>, List<ChatMessage>, WorkflowResumePinTests.Speaker>(
            "tts",
            (messages, settings, _, _) =>
            {
                voices.Enqueue(settings.Voice);

                return new ValueTask<List<ChatMessage>>(messages);
            });

        return (host, voices);
    }

    private static WorkflowDefinition Narrated(string voice)
        => new()
        {
            Name = "narrated",
            Kind = WorkflowKind.Sequential,
            Nodes =
            [
                new WorkflowNodeReference { Name = "writer", Kind = WorkflowNodeKind.Agent },
                new WorkflowNodeReference
                {
                    Name = "tts",
                    Kind = WorkflowNodeKind.Function,
                    Settings = JsonDocument.Parse($$"""{"voice":"{{voice}}"}""").RootElement.Clone(),
                },
            ],
        };

    private static async Task<List<RunEvent>> RunAsync(WorkflowRunner runner, int? definitionVersion)
    {
        var events = new List<RunEvent>();

        await foreach (var runEvent in runner.RunStreamingAsync(
            new WorkflowRunRequest { WorkflowName = "narrated", Message = "go", DefinitionVersion = definitionVersion }))
        {
            events.Add(runEvent);
        }

        return events;
    }

    private static async Task<RunRecord> RootAsync(WorkflowTestHost host)
        => (await host.RunStore.QueryRunsAsync(new RunQuery { OnlyRootRuns = false, Take = 100 }))
            .Single(static run => run.Kind == RunKind.Workflow && run.ParentRunId is null);
}
