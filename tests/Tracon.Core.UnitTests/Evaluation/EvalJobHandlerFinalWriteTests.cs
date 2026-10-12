using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Evaluation;

/// <summary>
/// The write that closes a finished eval run is not part of the work it records.
/// </summary>
/// <remarks>
/// F-365: <c>CompleteRunAsync(Completed)</c> sat inside the try that guards the
/// cases. A store error while recording a finished eval was caught as the
/// eval's own failure: the run was written <c>Failed</c> and the exception was
/// thrown on, so the worker ran the job again or dropped it. F-348's rule: only
/// the work sits in the work's try; the write of its outcome is separate and is
/// tried again.
/// </remarks>
public sealed class EvalJobHandlerFinalWriteTests
{
    private const string TenantId = "tenant";
    private const string AgentName = "graded-agent";

    [Fact]
    public async Task A_store_error_on_the_final_write_does_not_fail_a_finished_eval()
    {
        var store = new FlakyCompletionEvalStore(new InMemoryEvalStore()) { CompletedWriteFailuresLeft = 1 };
        var chat = new FakeChatClient(_ => new ChatResponse(new ChatMessage(ChatRole.Assistant, "a long enough answer")));
        var publisher = new RecordingPublisher();
        var handler = CreateHandler(store, chat, publisher, TimeSpan.FromMinutes(5));
        var (context, run, _) = await ArrangeAsync(store);

        var thrown = await Record.ExceptionAsync(() => handler.ExecuteAsync(context).AsTask());

        store.WrittenStatuses.ShouldNotContain(EvalRunStatus.Failed);
        thrown.ShouldBeNull();
        chat.CallCount.ShouldBe(2);

        var closed = await store.GetRunAsync(TenantId, run.Id);
        closed.ShouldNotBeNull();
        closed.Status.ShouldBe(EvalRunStatus.Completed);
        closed.Passed.ShouldBe(2);
        publisher.Statuses.ShouldBe([nameof(EvalRunStatus.Completed)]);
    }

    [Fact]
    public async Task A_final_write_that_keeps_failing_is_left_to_the_next_attempt_without_running_the_cases_again()
    {
        var store = new FlakyCompletionEvalStore(new InMemoryEvalStore()) { CompletedWriteFailuresLeft = int.MaxValue };
        var chat = new FakeChatClient(_ => new ChatResponse(new ChatMessage(ChatRole.Assistant, "a long enough answer")));

        // A third of the lease bounds the tries: 200 ms here.
        var handler = CreateHandler(store, chat, publisher: null, TimeSpan.FromMilliseconds(600));
        var (context, run, reported) = await ArrangeAsync(store);

        await Should.ThrowAsync<InvalidOperationException>(() => handler.ExecuteAsync(context).AsTask());

        store.WrittenStatuses.ShouldNotContain(EvalRunStatus.Failed);
        (await store.GetRunAsync(TenantId, run.Id))!.Status.ShouldBe(EvalRunStatus.Running);

        // The worker leases the job again; its items carry what the first attempt reported.
        store.CompletedWriteFailuresLeft = 0;

        var retry = new JobContext
        {
            Job = context.Job with { Attempt = context.Job.Attempt + 1 },
            Items = [.. context.Items.Select(item => item with { Status = reported.Single(result => result.Seq == item.Seq).Status })],
            ReportItemAsync = (_, _) => default,
            IsCancelledAsync = _ => new ValueTask<bool>(false),
        };

        await handler.ExecuteAsync(retry);

        chat.CallCount.ShouldBe(2);

        var closed = await store.GetRunAsync(TenantId, run.Id);
        closed.ShouldNotBeNull();
        closed.Status.ShouldBe(EvalRunStatus.Completed);
        closed.Passed.ShouldBe(2);
    }

    private static async Task<(JobContext Context, EvalRun Run, List<JobItemResult> Reported)> ArrangeAsync(IEvalStore store)
    {
        var suite = await store.SaveSuiteAsync(new EvalSuite
        {
            TenantId = TenantId,
            Name = "graded-suite",
            AgentName = AgentName,
            Checks = JsonDocument.Parse("""[{"kind":"nonEmpty","minLength":1}]""").RootElement.Clone(),
        });

        var cases = await store.ReplaceCasesAsync(
            suite.Id,
            [
                new EvalCase { SuiteId = suite.Id, Seq = 0, Query = "first question" },
                new EvalCase { SuiteId = suite.Id, Seq = 1, Query = "second question" },
            ]);

        var jobId = Guid.NewGuid();

        var run = await store.CreateRunAsync(new EvalRun
        {
            Id = Guid.Empty,
            TenantId = TenantId,
            SuiteId = suite.Id,
            JobId = jobId,
            Status = EvalRunStatus.Pending,
            Total = cases.Count,
            StartedAt = DateTimeOffset.UtcNow,
        });

        var items = cases
            .Select((evalCase, seq) => new JobItemRecord
            {
                Id = Guid.NewGuid(),
                JobId = jobId,
                Seq = seq,
                Input = evalCase.Id.ToString(),
                Status = JobItemStatus.Pending,
            })
            .ToArray();

        var reported = new List<JobItemResult>();

        var context = new JobContext
        {
            Job = new JobRecord
            {
                Id = jobId,
                TenantId = TenantId,
                HandlerKey = JobHandlerKeys.Eval,
                TargetName = AgentName,
                Status = JobStatus.Running,
                Payload = JsonSerializer.SerializeToElement(new { suiteName = suite.Name }),
                TotalItems = items.Length,
                Attempt = 1,
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

        return (context, run, reported);
    }

    private static EvalJobHandler CreateHandler(
        IEvalStore store,
        IChatClient chat,
        IWebhookPublisher? publisher,
        TimeSpan leaseDuration)
        => new(
            store,
            new SingleAgentCatalog(new ChatClientAgent(chat, new ChatClientAgentOptions { Name = AgentName })),
            new InMemoryAgentDefinitionStore(),
            new AgentDefinitionCompiler(TestData.Providers(new FakeModelProvider()), TestData.Registry()),
            new EvalCheckRegistry([]),
            new LocalEvalEvaluatorFactory(),
            Options.Create(new TraconOptions()),
            [],
            NullLogger<EvalJobHandler>.Instance,
            webhookPublisher: publisher,
            schedulingOptions: new StaticOptionsMonitor<TraconSchedulingOptions>(
                new TraconSchedulingOptions { LeaseDuration = leaseDuration, MaxAttempts = 3 }));

    /// <summary>
    /// An eval store that refuses the <see cref="EvalRunStatus.Completed"/> write while
    /// failures are left. Every other write goes through, so a handler that answers
    /// the refusal by writing <see cref="EvalRunStatus.Failed"/> is seen doing so.
    /// </summary>
    private sealed class FlakyCompletionEvalStore(IEvalStore inner) : IEvalStore
    {
        public int CompletedWriteFailuresLeft { get; set; }

        /// <summary>The status of every completion that reached the inner store.</summary>
        public List<EvalRunStatus> WrittenStatuses { get; } = [];

        public ValueTask CompleteRunAsync(EvalRunCompletion completion, CancellationToken cancellationToken = default)
        {
            if (completion.Status == EvalRunStatus.Completed && CompletedWriteFailuresLeft > 0)
            {
                CompletedWriteFailuresLeft--;

                throw new InvalidOperationException("The eval store is unavailable.");
            }

            WrittenStatuses.Add(completion.Status);

            return inner.CompleteRunAsync(completion, cancellationToken);
        }

        public ValueTask<IReadOnlyList<EvalSuite>> ListSuitesAsync(string tenantId, CancellationToken cancellationToken = default)
            => inner.ListSuitesAsync(tenantId, cancellationToken);

        public ValueTask<EvalSuite?> GetSuiteAsync(string tenantId, string name, CancellationToken cancellationToken = default)
            => inner.GetSuiteAsync(tenantId, name, cancellationToken);

        public ValueTask<EvalSuite> SaveSuiteAsync(EvalSuite suite, CancellationToken cancellationToken = default)
            => inner.SaveSuiteAsync(suite, cancellationToken);

        public ValueTask<bool> DeleteSuiteAsync(string tenantId, string name, CancellationToken cancellationToken = default)
            => inner.DeleteSuiteAsync(tenantId, name, cancellationToken);

        public ValueTask<IReadOnlyList<EvalCase>> ListCasesAsync(Guid suiteId, CancellationToken cancellationToken = default)
            => inner.ListCasesAsync(suiteId, cancellationToken);

        public ValueTask<IReadOnlyList<EvalCase>> ReplaceCasesAsync(
            Guid suiteId,
            IReadOnlyList<EvalCase> cases,
            CancellationToken cancellationToken = default)
            => inner.ReplaceCasesAsync(suiteId, cases, cancellationToken);

        public ValueTask<EvalCaseAddResult> AddCaseAsync(
            Guid suiteId,
            EvalCaseDraft draft,
            CancellationToken cancellationToken = default)
            => inner.AddCaseAsync(suiteId, draft, cancellationToken);

        public ValueTask<EvalRun> CreateRunAsync(EvalRun run, CancellationToken cancellationToken = default)
            => inner.CreateRunAsync(run, cancellationToken);

        public ValueTask MarkRunRunningAsync(
            Guid evalRunId,
            int? agentVersion,
            string? modelId,
            CancellationToken cancellationToken = default)
            => inner.MarkRunRunningAsync(evalRunId, agentVersion, modelId, cancellationToken);

        public ValueTask<EvalRun?> GetRunAsync(string tenantId, Guid evalRunId, CancellationToken cancellationToken = default)
            => inner.GetRunAsync(tenantId, evalRunId, cancellationToken);

        public ValueTask<EvalRun?> GetRunByJobIdAsync(string tenantId, Guid jobId, CancellationToken cancellationToken = default)
            => inner.GetRunByJobIdAsync(tenantId, jobId, cancellationToken);

        public ValueTask<IReadOnlyList<EvalRun>> QueryRunsAsync(EvalRunQuery query, CancellationToken cancellationToken = default)
            => inner.QueryRunsAsync(query, cancellationToken);

        public ValueTask RecordCaseResultAsync(EvalCaseResult result, CancellationToken cancellationToken = default)
            => inner.RecordCaseResultAsync(result, cancellationToken);

        public ValueTask<IReadOnlyList<EvalCaseResult>> ListCaseResultsAsync(
            string tenantId,
            Guid evalRunId,
            CancellationToken cancellationToken = default)
            => inner.ListCaseResultsAsync(tenantId, evalRunId, cancellationToken);

        public ValueTask<EvalRunDiff?> DiffRunsAsync(EvalRunDiffQuery query, CancellationToken cancellationToken = default)
            => inner.DiffRunsAsync(query, cancellationToken);
    }

    private sealed class SingleAgentCatalog(AIAgent agent) : IAgentCatalog
    {
        public ValueTask<IReadOnlyList<AgentDescriptor>> ListAsync(CancellationToken cancellationToken = default)
            => new((IReadOnlyList<AgentDescriptor>)
            [
                new AgentDescriptor
                {
                    Name = AgentName,
                    Origin = AgentDefinitionOrigin.Code,
                    SourceName = "test",
                    Version = 1,
                },
            ]);

        public ValueTask<AIAgent?> ResolveAsync(string agentName, string? culture, CancellationToken cancellationToken)
            => new(string.Equals(agentName, AgentName, StringComparison.Ordinal) ? agent : null);

        public ValueTask<AIAgent?> ResolveAsync(string agentName, int? version, string? culture = null, CancellationToken cancellationToken = default)
            => ResolveAsync(agentName, culture, cancellationToken);
    }

    private sealed class RecordingPublisher : IWebhookPublisher
    {
        public List<string?> Statuses { get; } = [];

        public ValueTask<int> PublishAsync(
            string tenantId,
            string eventType,
            WebhookEventPayload payload,
            CancellationToken cancellationToken = default)
        {
            if (string.Equals(eventType, WebhookEvents.EvalCompleted, StringComparison.Ordinal))
            {
                Statuses.Add(payload.Eval?.Status);
            }

            return new ValueTask<int>(1);
        }
    }
}
