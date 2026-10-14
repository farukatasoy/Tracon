using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Tracon.Workflows.UnitTests.Fakes;

namespace Tracon.Workflows.UnitTests;

/// <summary>
/// A run that waits to be resumed keeps the definition it was started with (phase 200, failure
/// modes 9, 11 and 12).
/// </summary>
/// <remarks>
/// <para>
/// The function node here speaks the voice its settings name, so what a resumed run was really
/// given is visible in the voices the node saw - not in what the test hoped. Settings are the
/// observable on purpose: unlike a structural edit, they never make a checkpoint incompatible, so
/// the only thing that can differ between the two compilations is WHICH version was compiled.
/// </para>
/// <para>
/// The structural half of the same promise is <see cref="WorkflowStructuralChangeResumeTests"/>;
/// the parameter-schema half is <see cref="WorkflowParameterRespondTests"/>.
/// </para>
/// </remarks>
public sealed class WorkflowResumePinTests
{
    [Fact]
    public async Task A_resume_after_a_settings_edit_runs_with_the_old_settings_and_with_the_new_ones_when_the_new_version_is_named()
    {
        var (host, voices) = Host();

        await host.SaveAsync(Narrated("alloy"));

        var runner = host.CreateRunner();
        var source = await RunToCompletionAsync(host, runner);
        var checkpoint = await FirstCheckpointAsync(host, source);

        var edited = await host.SaveAsync(Narrated("nova"));

        await ResumeAsync(runner, source, checkpoint, definitionVersion: null);
        await ResumeAsync(runner, source, checkpoint, definitionVersion: edited.Version);

        voices.ToArray().ShouldBe(["alloy", "alloy", "nova"]);
    }

    [Fact]
    public async Task The_stamp_follows_a_chain_of_resumes()
    {
        // A recovery chain (an interrupted run continued, then continued again) must keep the version
        // it began with: the second resume reads the stamp the first one wrote.
        var (host, voices) = Host();

        await host.SaveAsync(Narrated("alloy"));

        var runner = host.CreateRunner();
        var source = await RunToCompletionAsync(host, runner);

        await host.SaveAsync(Narrated("nova"));

        await ResumeAsync(runner, source, await FirstCheckpointAsync(host, source), definitionVersion: null);

        var firstResume = (await host.RunStore.QueryRunsAsync(new RunQuery { OnlyRootRuns = false, Take = 100 }))
            .Single(run => run.Kind == RunKind.Workflow && run.Id != source.Id);

        firstResume.AgentVersion.ShouldBe(1);

        await host.SaveAsync(Narrated("echo"));

        await ResumeAsync(runner, firstResume, await FirstCheckpointAsync(host, firstResume), definitionVersion: null);

        var secondResume = (await host.RunStore.QueryRunsAsync(new RunQuery { OnlyRootRuns = false, Take = 100 }))
            .Single(run => run.Kind == RunKind.Workflow && run.Id != source.Id && run.Id != firstResume.Id);

        secondResume.AgentVersion.ShouldBe(1, "two edits later, the chain still runs the version it began with");
        secondResume.Status.ShouldBe(RunStatus.Completed, secondResume.Error?.Message);
        voices.ToArray().ShouldNotContain("nova", StringComparer.Ordinal);
        voices.ToArray().ShouldNotContain("echo", StringComparer.Ordinal);
    }

    [Fact]
    public async Task A_run_that_recorded_no_version_resumes_with_the_current_definition()
    {
        // A row from before versions were recorded names none. It resumes the way every run did:
        // with whatever the workflow says now.
        var (host, voices) = Host();

        await host.SaveAsync(Narrated("alloy"));

        var runner = host.CreateRunner();
        var source = await RunToCompletionAsync(host, runner);
        var checkpoint = await FirstCheckpointAsync(host, source);

        await ForgetTheStampAsync(host, source);
        await host.SaveAsync(Narrated("nova"));
        await ResumeAsync(runner, source, checkpoint, definitionVersion: null);

        voices.ToArray().ShouldBe(["alloy", "nova"]);
    }

    [Fact]
    public async Task A_stamp_whose_version_is_gone_fails_closed_and_never_falls_back_to_the_current_definition()
    {
        var (host, voices) = Host();
        var probe = new ProbeWorkflowDefinitionStore(host.DefinitionStore);

        await host.SaveAsync(Narrated("alloy"));

        var runner = host.CreateRunnerOver(probe);
        var source = await RunToCompletionAsync(host, runner);
        var checkpoint = await FirstCheckpointAsync(host, source);

        await host.SaveAsync(Narrated("nova"));
        probe.ForgottenVersions.Add(1);

        var refusal = await Should.ThrowAsync<TraconException>(
            async () => await ResumeAsync(runner, source, checkpoint, definitionVersion: null));

        refusal.Message.ShouldContain("has no version 1", Case.Sensitive);
        voices.ToArray().ShouldBe(["alloy"], "the node did not run again under the current settings");
    }

    [Fact]
    public async Task A_resume_that_names_a_version_the_workflow_does_not_have_fails_closed_too()
    {
        var (host, voices) = Host();

        await host.SaveAsync(Narrated("alloy"));

        var runner = host.CreateRunner();
        var source = await RunToCompletionAsync(host, runner);
        var checkpoint = await FirstCheckpointAsync(host, source);

        var refusal = await Should.ThrowAsync<TraconException>(
            async () => await ResumeAsync(runner, source, checkpoint, definitionVersion: 9));

        refusal.Message.ShouldContain("has no version 9", Case.Sensitive);
        voices.ToArray().ShouldBe(["alloy"]);
    }

    [Fact]
    public async Task A_resume_of_a_deleted_workflow_is_still_refused_even_though_its_history_is_kept()
    {
        // History outlives the definition so that a number is never reused; it is not a way to run
        // a workflow the operator removed.
        var (host, voices) = Host();

        await host.SaveAsync(Narrated("alloy"));

        var runner = host.CreateRunner();
        var source = await RunToCompletionAsync(host, runner);
        var checkpoint = await FirstCheckpointAsync(host, source);

        await host.DefinitionStore.DeleteAsync(host.TenantContext.TenantId, "narrated");

        var events = new List<RunEvent>();

        await foreach (var runEvent in runner.ResumeStreamingAsync(new WorkflowResumeRequest { RunId = source.Id, CheckpointId = checkpoint }))
        {
            events.Add(runEvent);
        }

        events.ShouldContain(static runEvent => runEvent.Type == RunEventType.RunFailed);
        events.Single(static runEvent => runEvent.Type == RunEventType.RunFailed).Text!
            .ShouldContain("There is no workflow named 'narrated'", Case.Sensitive);
        voices.ToArray().ShouldBe(["alloy"]);
    }

    private static (WorkflowTestHost Host, ConcurrentQueue<string> Voices) Host()
    {
        var host = new WorkflowTestHost("writer");
        var voices = new ConcurrentQueue<string>();

        host.AddFunction<List<ChatMessage>, List<ChatMessage>, Speaker>(
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

    private static async Task<RunRecord> RunToCompletionAsync(WorkflowTestHost host, WorkflowRunner runner)
    {
        await foreach (var _ in runner.RunStreamingAsync(new WorkflowRunRequest { WorkflowName = "narrated", Message = "go" }))
        {
            // Drained.
        }

        var source = (await host.RunStore.QueryRunsAsync(new RunQuery { OnlyRootRuns = false, Take = 100 }))
            .Single(static run => run.Kind == RunKind.Workflow);

        source.Status.ShouldBe(RunStatus.Completed, source.Error?.Message);

        return source;
    }

    /// <summary>The checkpoint written before the function ran: resuming from it runs the function again.</summary>
    private static async Task<string> FirstCheckpointAsync(WorkflowTestHost host, RunRecord run)
    {
        var checkpoints = await host.CheckpointStore.ListByRunAsync(host.TenantContext.TenantId, run.Id);

        checkpoints.ShouldNotBeEmpty();

        return checkpoints[0].CheckpointId;
    }

    private static async Task ResumeAsync(WorkflowRunner runner, RunRecord source, string checkpointId, int? definitionVersion)
    {
        await foreach (var _ in runner.ResumeStreamingAsync(new WorkflowResumeRequest
        {
            RunId = source.Id,
            CheckpointId = checkpointId,
            DefinitionVersion = definitionVersion,
        }))
        {
            // Drained.
        }
    }

    /// <summary>Writes the row again without its version, the way a row from before versions were recorded reads.</summary>
    private static async Task ForgetTheStampAsync(WorkflowTestHost host, RunRecord run)
        => await host.RunStore.StartRunAsync(new RunStartInfo
        {
            RunId = run.Id,
            AgentName = run.AgentName,
            Kind = run.Kind,
            WorkflowName = run.WorkflowName,
            Status = run.Status,
            StartedAt = run.StartedAt,
            TenantId = run.TenantId,
            SessionId = run.SessionId,
            AgentVersion = null,
        });

    /// <summary>The settings of the <c>tts</c> function: the voice the node speaks.</summary>
    internal sealed record Speaker
    {
        public required string Voice { get; init; }
    }
}
