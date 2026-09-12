using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Tracon.Core.UnitTests.Fakes;
using Tracon.Testing.Contracts;
using Tracon.Testing.Contracts.Scheduling;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Tracon.Core.UnitTests.Contracts;

/// <summary>Runs the published <see cref="JobHandlerContract"/> against <see cref="AgentBatchJobHandler"/>.</summary>
public sealed class AgentBatchJobHandlerContractTests : JobHandlerContract
{
    protected override ValueTask<IJobHandler> CreateHandlerAsync()
        => ValueTask.FromResult<IJobHandler>(
            new AgentBatchJobHandler(new AnyNameAgentCatalog(new ContractAgent()), NullLogger<AgentBatchJobHandler>.Instance));

    protected override JobItemRecord CreateItem(int sequence, JobItemStatus status)
        => new() { Id = Guid.NewGuid(), JobId = JobId, Seq = sequence, Input = $"input-{sequence}", Status = status };
}

/// <summary>Runs the published <see cref="JobHandlerContract"/> against <see cref="WorkflowJobHandler"/>.</summary>
public sealed class WorkflowJobHandlerContractTests : JobHandlerContract
{
    protected override ValueTask<IJobHandler> CreateHandlerAsync()
        => ValueTask.FromResult<IJobHandler>(
            new WorkflowJobHandler(new AlwaysSucceedsWorkflowRunner(), NullLogger<WorkflowJobHandler>.Instance));

    protected override JobItemRecord CreateItem(int sequence, JobItemStatus status)
        => new() { Id = Guid.NewGuid(), JobId = JobId, Seq = sequence, Input = $"input-{sequence}", Status = status };

    /// <summary>Only <see cref="IWorkflowRunner.RunStreamingAsync"/> is actually used.</summary>
    private sealed class AlwaysSucceedsWorkflowRunner : IWorkflowRunner
    {
        public ValueTask<IReadOnlyList<WorkflowDescriptor>> ListAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<WorkflowDescriptor?> GetAsync(string name, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<WorkflowGraph?> GetGraphAsync(string name, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<IReadOnlyList<WorkflowPendingRequest>> ListPendingRequestsAsync(
            Guid runId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public IAsyncEnumerable<RunEvent> RespondStreamingAsync(
            WorkflowRespondRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public async IAsyncEnumerable<RunEvent> RunStreamingAsync(
            WorkflowRunRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var runId = TraconId.NewId();
            var now = DateTimeOffset.UtcNow;

            yield return new RunEvent { RunId = runId, Sequence = 0, Type = RunEventType.RunStarted, Timestamp = now };
            await Task.Yield();
            yield return new RunEvent { RunId = runId, Sequence = 1, Type = RunEventType.RunCompleted, Timestamp = now };
        }

        public IAsyncEnumerable<RunEvent> ResumeStreamingAsync(
            WorkflowResumeRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}

/// <summary>Runs the published <see cref="JobHandlerContract"/> against <see cref="EvalJobHandler"/>.</summary>
public sealed class EvalJobHandlerContractTests : JobHandlerContract
{
    private const string AgentName = "contract-agent";
    private const string TenantId = "contract-tenant";

    private EvalCase[] _cases = [];
    private string _suiteName = string.Empty;

    protected override async ValueTask<IJobHandler> CreateHandlerAsync()
    {
        var evalStore = new InMemoryEvalStore();

        var suite = await evalStore.SaveSuiteAsync(new EvalSuite
        {
            TenantId = TenantId,
            Name = "contract-suite",
            AgentName = AgentName,
            Checks = JsonDocument.Parse("""[{"kind":"nonEmpty","minLength":1}]""").RootElement.Clone(),
        }).ConfigureAwait(false);

        _suiteName = suite.Name;

        _cases =
        [
            .. await evalStore.ReplaceCasesAsync(
                suite.Id,
                [
                    new EvalCase { SuiteId = suite.Id, Seq = 0, Query = "question-0" },
                    new EvalCase { SuiteId = suite.Id, Seq = 1, Query = "question-1" },
                ]).ConfigureAwait(false),
        ];

        await evalStore.CreateRunAsync(new EvalRun
        {
            Id = Guid.Empty,
            TenantId = TenantId,
            SuiteId = suite.Id,
            JobId = JobId,
            Status = EvalRunStatus.Pending,
            Total = 0,
            StartedAt = DateTimeOffset.UtcNow,
        }).ConfigureAwait(false);

        return new EvalJobHandler(
            evalStore,
            new AnyNameAgentCatalog(new ContractAgent()),
            new InMemoryAgentDefinitionStore(),
            new AgentDefinitionCompiler(TestData.Providers(new FakeModelProvider()), TestData.Registry()),
            new EvalCheckRegistry([]),
            new LocalEvalEvaluatorFactory(),
            Options.Create(new TraconOptions()),
            [],
            NullLogger<EvalJobHandler>.Instance);
    }

    protected override JobRecord CreateJob(IReadOnlyList<JobItemRecord> items) => base.CreateJob(items) with
    {
        TenantId = TenantId,
        TargetName = AgentName,
        Payload = JsonSerializer.SerializeToElement(new { suiteName = _suiteName }),
    };

    protected override JobItemRecord CreateItem(int sequence, JobItemStatus status)
        => new() { Id = Guid.NewGuid(), JobId = JobId, Seq = sequence, Input = _cases[sequence].Id.ToString(), Status = status };
}

/// <summary>Prevents a new job handler contract from silently going untested.</summary>
public sealed class JobHandlerContractCoverageTests
{
    [Fact]
    public void Every_job_handler_contract_has_a_derived_test()
        => ContractCoverage.MissingDerivedTypes(
            Assembly.GetExecutingAssembly(), ContractCoverage.SchedulingContracts).ShouldBeEmpty();
}

/// <summary>Resolves any requested agent name to the same fake agent. Shared by the batch and eval contract tests.</summary>
internal sealed class AnyNameAgentCatalog(AIAgent agent) : IAgentCatalog
{
    public ValueTask<IReadOnlyList<AgentDescriptor>> ListAsync(CancellationToken cancellationToken = default)
        => new((IReadOnlyList<AgentDescriptor>)[]);

    public ValueTask<AIAgent?> ResolveAsync(string agentName, string? culture, CancellationToken cancellationToken)
        => new(agent);

    public ValueTask<AIAgent?> ResolveAsync(string agentName, int? version, string? culture = null, CancellationToken cancellationToken = default)
        => new(agent);
}

/// <summary>The smallest possible fake agent: always answers with a fixed, non-empty response.</summary>
internal sealed class ContractAgent : AIAgent
{
    public override string Name => "contract-fake-agent";

    public override string Description => "for contract tests";

    protected override Task<AgentResponse> RunCoreAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session = null,
        AgentRunOptions? options = null,
        CancellationToken cancellationToken = default)
        => Task.FromResult(new AgentResponse(new ChatMessage(ChatRole.Assistant, "contract response")));

    protected override async IAsyncEnumerable<AgentResponseUpdate> RunCoreStreamingAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session = null,
        AgentRunOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var response = await RunCoreAsync(messages, session, options, cancellationToken).ConfigureAwait(false);

        foreach (var message in response.Messages)
        {
            yield return new AgentResponseUpdate(message.Role, message.Contents);
        }
    }

    protected override ValueTask<AgentSession> CreateSessionCoreAsync(CancellationToken cancellationToken = default)
        => new(new ContractSession());

    protected override ValueTask<AgentSession> DeserializeSessionCoreAsync(
        JsonElement serializedState,
        JsonSerializerOptions? jsonSerializerOptions = null,
        CancellationToken cancellationToken = default)
        => new(new ContractSession());

    protected override ValueTask<JsonElement> SerializeSessionCoreAsync(
        AgentSession session,
        JsonSerializerOptions? jsonSerializerOptions = null,
        CancellationToken cancellationToken = default)
        => new(EmptyState);

    private static JsonElement EmptyState { get; } = JsonDocument.Parse("{}").RootElement.Clone();

    private sealed class ContractSession : AgentSession;
}
