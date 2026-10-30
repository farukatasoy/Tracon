using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Scheduling;

/// <summary>
/// The attribution envelope of a workflow or agent batch payload, and the scope
/// the workflow handler replays from it.
/// </summary>
/// <remarks>
/// A queued workflow ran with no user and no labels. The envelope is how
/// the identity of whoever queued the job travels to the worker; the boundary
/// cases (dispatcher, trigger, schedule) are in
/// <c>QueuedWorkflowAttributionTests</c>, which runs the real host.
/// </remarks>
public sealed class QueuedAttributionTests
{
    [Fact]
    public void An_envelope_of_a_batch_key_gives_its_items_and_its_attribution()
    {
        var content = JobPayload.Read(
            JobHandlerKeys.Workflow,
            Json("""{"items":["one",{"message":"two"}],"userId":"ada","labels":{"team":"payments"}}"""));

        content.Problem.ShouldBeNull();
        content.Items.ShouldBe(["one", """{"message":"two"}"""]);
        content.UserId.ShouldBe("ada");
        content.Labels.ShouldNotBeNull()["team"].ShouldBe("payments");
        content.HasAttribution.ShouldBeTrue();
    }

    [Fact]
    public void Another_handler_key_reads_the_same_object_as_one_item()
    {
        var content = JobPayload.Read("acme.reports", Json("""{"items":["one"],"userId":"ada"}"""));

        content.Items.ShouldBe(["""{"items":["one"],"userId":"ada"}"""]);
        content.HasAttribution.ShouldBeFalse();
    }

    [Theory]
    [InlineData("""{"message":"hello","parameters":{"tone":"formal"}}""")]
    [InlineData("""{"items":["one"],"note":"keep"}""")]
    [InlineData("""{}""")]
    public void An_object_with_any_other_member_keeps_its_old_meaning(string payload)
    {
        var content = JobPayload.Read(JobHandlerKeys.AgentBatch, Json(payload));

        content.Problem.ShouldBeNull();
        content.Items.ShouldBe([Json(payload).GetRawText()]);
        content.HasAttribution.ShouldBeFalse();
    }

    [Theory]
    [InlineData("""{"items":"one"}""", "'items' must be an array")]
    [InlineData("""{"items":[],"userId":42}""", "'userId' must be a string")]
    [InlineData("""{"items":[],"labels":["team"]}""", "'labels' must be an object")]
    [InlineData("""{"items":[],"labels":{"team":1}}""", "label 'team' is not a string")]
    [InlineData("""{"items":[],"labels":{"a":"1","b":"1","c":"1","d":"1","e":"1","f":"1","g":"1","h":"1","i":"1"}}""", "at most 8")]
    public void A_malformed_envelope_carries_a_reason_and_neither_items_nor_attribution(string payload, string reason)
    {
        var content = JobPayload.Read(JobHandlerKeys.Workflow, Json(payload));

        content.Problem.ShouldNotBeNull().ShouldContain(reason, Case.Sensitive);
        content.Items.ShouldBeEmpty();
        content.HasAttribution.ShouldBeFalse();
    }

    [Fact]
    public void A_written_envelope_reads_back_the_same_items_and_attribution()
    {
        var labels = new Dictionary<string, string>(StringComparer.Ordinal) { ["team"] = "payments" };
        var written = JobPayload.Envelope(["one", """{"message":"two"}"""], "ada", labels);

        var content = JobPayload.Read(JobHandlerKeys.Workflow, written);

        content.Items.ShouldBe(["one", """{"message":"two"}"""]);
        content.UserId.ShouldBe("ada");
        content.Labels.ShouldNotBeNull()["team"].ShouldBe("payments");
    }

    [Fact]
    public async Task A_workflow_runs_under_the_payloads_attribution_on_every_step_of_its_stream()
    {
        var runner = new AmbientCapturingRunner();
        var handler = new WorkflowJobHandler(runner, TestWorkflowRecovery.Off(), NullLogger<WorkflowJobHandler>.Instance);
        var reported = new List<JobItemResult>();

        await handler.ExecuteAsync(Context(
            Json("""{"items":["input"],"userId":"ada","labels":{"team":"payments"}}"""),
            reported));

        runner.Seen.ShouldBe([("ada", "payments"), ("ada", "payments")]);
        reported.ShouldHaveSingleItem().Status.ShouldBe(JobItemStatus.Completed);
        AmbientRunAttributionScope.IsActive.ShouldBeFalse();
    }

    [Fact]
    public async Task An_unusable_attribution_is_dropped_and_the_item_still_runs()
    {
        var runner = new AmbientCapturingRunner();
        var handler = new WorkflowJobHandler(runner, TestWorkflowRecovery.Off(), NullLogger<WorkflowJobHandler>.Instance);
        var reported = new List<JobItemResult>();

        // Written straight to a store: the boundaries refuse this, the worker must not fail on it.
        await handler.ExecuteAsync(Context(Json("""{"items":["input"],"userId":7}"""), reported));

        runner.ActiveAtStart.ShouldBe([false]);
        reported.ShouldHaveSingleItem().Status.ShouldBe(JobItemStatus.Completed);
    }

    [Fact]
    public async Task A_payload_without_an_attribution_opens_no_scope()
    {
        var runner = new AmbientCapturingRunner();
        var handler = new WorkflowJobHandler(runner, TestWorkflowRecovery.Off(), NullLogger<WorkflowJobHandler>.Instance);

        await handler.ExecuteAsync(Context(Json("""["input"]"""), []));

        runner.ActiveAtStart.ShouldBe([false]);
    }

    private static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement.Clone();

    private static JobContext Context(JsonElement payload, List<JobItemResult> reported)
        => new()
        {
            Job = new JobRecord
            {
                Id = Guid.NewGuid(),
                TenantId = "tenant",
                HandlerKey = JobHandlerKeys.Workflow,
                TargetName = "daily-report",
                Status = JobStatus.Running,
                Payload = payload,
                ScheduledFor = DateTimeOffset.UtcNow,
                CreatedAt = DateTimeOffset.UtcNow,
            },
            Items =
            [
                new JobItemRecord { Id = Guid.NewGuid(), JobId = Guid.NewGuid(), Seq = 0, Input = "input", Status = JobItemStatus.Pending },
            ],
            ReportItemAsync = (result, _) =>
            {
                reported.Add(result);
                return default;
            },
            IsCancelledAsync = _ => new ValueTask<bool>(false),
        };

    /// <summary>Records the ambient attribution at each step of the stream it produces.</summary>
    private sealed class AmbientCapturingRunner : IWorkflowRunner
    {
        public List<(string? UserId, string? Team)> Seen { get; } = [];

        public List<bool> ActiveAtStart { get; } = [];

        public ValueTask<IReadOnlyList<WorkflowDescriptor>> ListAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<WorkflowDescriptor?> GetAsync(string name, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<WorkflowGraph?> GetGraphAsync(string name, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<IReadOnlyList<WorkflowPendingRequest>> ListPendingRequestsAsync(
            Guid runId,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public IAsyncEnumerable<RunEvent> RespondStreamingAsync(
            WorkflowRespondRequest request,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public IAsyncEnumerable<RunEvent> ResumeStreamingAsync(
            WorkflowResumeRequest request,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public async IAsyncEnumerable<RunEvent> RunStreamingAsync(
            WorkflowRunRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var runId = TraconId.NewId();
            var now = DateTimeOffset.UtcNow;

            ActiveAtStart.Add(AmbientRunAttributionScope.IsActive);
            Capture();

            yield return new RunEvent { RunId = runId, Sequence = 0, Type = RunEventType.RunStarted, Timestamp = now };

            await Task.Yield();

            // The second step runs in a later MoveNextAsync: the scope must still be there.
            Capture();

            yield return new RunEvent { RunId = runId, Sequence = 1, Type = RunEventType.RunCompleted, Timestamp = now };
        }

        private void Capture()
        {
            if (AmbientRunAttributionScope.IsActive)
            {
                Seen.Add((AmbientRunAttributionScope.CurrentUserId, AmbientRunAttributionScope.CurrentLabels?["team"]));
            }
        }
    }
}
