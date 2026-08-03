using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgentPrism.Core.UnitTests.Scheduling;

public sealed class WorkflowJobHandlerTests
{
    [Fact]
    public void Kind_Workflow_dur()
    {
        new WorkflowJobHandler(null, NullLogger<WorkflowJobHandler>.Instance).Kind.ShouldBe(JobKind.Workflow);
    }

    [Fact]
    public async Task Motor_kayitli_degilse_istisna_firlatir()
    {
        var handler = new WorkflowJobHandler(runner: null, NullLogger<WorkflowJobHandler>.Instance);

        var exception = await Should.ThrowAsync<AgentPrismException>(
            () => handler.ExecuteAsync(BuildContext([Item(0, "girdi")])).AsTask());

        exception.Message.ShouldContain("UseWorkflows", Case.Sensitive);
    }

    [Fact]
    public async Task Basarili_akis_tamamlandi_olarak_raporlanir()
    {
        var runner = new ScriptedWorkflowRunner(shouldFail: false);
        var handler = new WorkflowJobHandler(runner, NullLogger<WorkflowJobHandler>.Instance);
        var reported = new List<JobItemResult>();

        await handler.ExecuteAsync(BuildContext([Item(0, "girdi")], reported));

        reported.ShouldHaveSingleItem();
        reported[0].Status.ShouldBe(JobItemStatus.Completed);
        reported[0].RunId.ShouldBe(runner.LastRunId);
    }

    [Fact]
    public async Task RunFailed_olayi_basarisiz_olarak_raporlanir()
    {
        var runner = new ScriptedWorkflowRunner(shouldFail: true);
        var handler = new WorkflowJobHandler(runner, NullLogger<WorkflowJobHandler>.Instance);
        var reported = new List<JobItemResult>();

        await handler.ExecuteAsync(BuildContext([Item(0, "girdi")], reported));

        reported.ShouldHaveSingleItem();
        reported[0].Status.ShouldBe(JobItemStatus.Failed);
        reported[0].Error.ShouldNotBeNullOrWhiteSpace();
    }

    private static JobItemRecord Item(int seq, string input, JobItemStatus status = JobItemStatus.Pending)
        => new() { Id = Guid.NewGuid(), JobId = Guid.NewGuid(), Seq = seq, Input = input, Status = status };

    private static JobContext BuildContext(IReadOnlyList<JobItemRecord> items, List<JobItemResult>? reported = null)
    {
        reported ??= [];

        return new JobContext
        {
            Job = new JobRecord
            {
                Id = Guid.NewGuid(),
                TenantId = "kiraci",
                Kind = JobKind.Workflow,
                TargetName = "gunluk-rapor",
                Status = JobStatus.Running,
                ScheduledFor = DateTimeOffset.UtcNow,
                CreatedAt = DateTimeOffset.UtcNow,
            },
            Items = items,
            ReportItemAsync = (result, _) =>
            {
                reported.Add(result);
                return default;
            },
            IsCancelledAsync = _ => new ValueTask<bool>(false),
        };
    }

    /// <summary>
    /// Yalnizca <see cref="RunStreamingAsync"/> gercekten kullanilir; digerleri
    /// bu isleyicinin hic cagirmadigi uyelerdir.
    /// </summary>
    private sealed class ScriptedWorkflowRunner(bool shouldFail) : IWorkflowRunner
    {
        public Guid LastRunId { get; private set; }

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

        public async IAsyncEnumerable<RunEvent> RunStreamingAsync(
            WorkflowRunRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            LastRunId = AgentPrismId.NewId();
            var now = DateTimeOffset.UtcNow;

            yield return new RunEvent { RunId = LastRunId, Sequence = 0, Type = RunEventType.RunStarted, Timestamp = now };
            await Task.Yield();

            if (shouldFail)
            {
                yield return new RunEvent
                {
                    RunId = LastRunId,
                    Sequence = 1,
                    Type = RunEventType.RunFailed,
                    Timestamp = now,
                    Text = "workflow patladi",
                };
            }
            else
            {
                yield return new RunEvent { RunId = LastRunId, Sequence = 1, Type = RunEventType.RunCompleted, Timestamp = now };
            }
        }

        public IAsyncEnumerable<RunEvent> ResumeStreamingAsync(
            WorkflowResumeRequest request,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
