using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgentPrism.Core.UnitTests.Evaluation;

public sealed class EvalJobHandlerTests
{
    private const string TenantId = "tenant";
    private const string AgentName = "customer-support-agent";

    [Fact]
    public void Kind_is_Eval()
    {
        var handler = CreateHandler(new InMemoryEvalStore(), new MapAgent());
        handler.Kind.ShouldBe(JobKind.Eval);
    }

    [Fact]
    public async Task Missing_suite_throws_and_the_run_is_never_attempted()
    {
        var evalStore = new InMemoryEvalStore();
        var handler = CreateHandler(evalStore, new MapAgent());
        var context = BuildContext(evalStore, suite: null, items: [], jobId: Guid.NewGuid());

        await Should.ThrowAsync<AgentPrismException>(() => handler.ExecuteAsync(context).AsTask());
    }

    [Fact]
    public async Task Missing_run_record_throws()
    {
        var evalStore = new InMemoryEvalStore();
        var suite = await evalStore.SaveSuiteAsync(Suite(Checks("""[{"kind":"nonEmpty"}]""")));
        var handler = CreateHandler(evalStore, new MapAgent());

        // Executed directly without creating a run record.
        var context = new JobContext
        {
            Job = Job(suite, Guid.NewGuid(), []),
            Items = [],
            ReportItemAsync = (_, _) => default,
            IsCancelledAsync = _ => new ValueTask<bool>(false),
        };

        await Should.ThrowAsync<AgentPrismException>(() => handler.ExecuteAsync(context).AsTask());
    }

    [Fact]
    public async Task Run_completes_when_all_cases_pass()
    {
        var evalStore = new InMemoryEvalStore();
        var agent = new MapAgent();
        var handler = CreateHandler(evalStore, agent);

        var suite = await evalStore.SaveSuiteAsync(Suite(Checks("""[{"kind":"nonEmpty","minLength":1}]""")));
        var cases = await evalStore.ReplaceCasesAsync(suite.Id, [CaseInput("first question"), CaseInput("second question")]);

        var reported = new List<JobItemResult>();
        var jobId = Guid.NewGuid();
        var run = await evalStore.CreateRunAsync(Run(suite.Id, jobId));

        var context = BuildContextForCases(suite, cases, jobId, reported);

        await handler.ExecuteAsync(context);

        reported.Count.ShouldBe(2);
        reported.ShouldAllBe(static result => result.Status == JobItemStatus.Completed);

        var completed = await evalStore.GetRunAsync(TenantId, run.Id);
        completed!.Status.ShouldBe(EvalRunStatus.Completed);
        completed.Total.ShouldBe(2);
        completed.Passed.ShouldBe(2);
        completed.Failed.ShouldBe(0);
        completed.AgentVersion.ShouldBe(7);
        completed.ModelId.ShouldBe("gpt-test");

        var results = await evalStore.ListCaseResultsAsync(TenantId, run.Id);
        results.Count.ShouldBe(2);
        results.ShouldAllBe(static result => result.Passed);
        results.ShouldAllBe(static result => result.RunId.HasValue);
    }

    [Fact]
    public async Task Case_that_fails_the_check_is_reported_as_failed()
    {
        var evalStore = new InMemoryEvalStore();
        var agent = new MapAgent();
        agent.Responses["bad question"] = "irrelevant";
        var handler = CreateHandler(evalStore, agent);

        var suite = await evalStore.SaveSuiteAsync(
            Suite(Checks("""[{"kind":"containsExpected","caseSensitive":false}]""")));
        var cases = await evalStore.ReplaceCasesAsync(
            suite.Id,
            [new EvalCase { SuiteId = suite.Id, Seq = 0, Query = "bad question", ExpectedOutput = "expected-word" }]);

        var jobId = Guid.NewGuid();
        var run = await evalStore.CreateRunAsync(Run(suite.Id, jobId));
        var reported = new List<JobItemResult>();

        var context = BuildContextForCases(suite, cases, jobId, reported);

        await handler.ExecuteAsync(context);

        reported.Count.ShouldBe(1);
        reported[0].Status.ShouldBe(JobItemStatus.Failed);

        var completed = await evalStore.GetRunAsync(TenantId, run.Id);
        completed!.Status.ShouldBe(EvalRunStatus.Completed);
        completed.Passed.ShouldBe(0);
        completed.Failed.ShouldBe(1);

        var results = await evalStore.ListCaseResultsAsync(TenantId, run.Id);
        results[0].Passed.ShouldBeFalse();
        results[0].FailureReason.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Cancelled_run_closes_as_cancelled()
    {
        var evalStore = new InMemoryEvalStore();
        var agent = new MapAgent();
        var handler = CreateHandler(evalStore, agent);

        var suite = await evalStore.SaveSuiteAsync(Suite(Checks("""[{"kind":"nonEmpty"}]""")));
        var cases = await evalStore.ReplaceCasesAsync(suite.Id, [CaseInput("first"), CaseInput("second")]);
        var jobId = Guid.NewGuid();
        var run = await evalStore.CreateRunAsync(Run(suite.Id, jobId));

        var items = cases.Select(static (evalCase, seq) => Item(seq, evalCase.Id)).ToArray();
        var calls = 0;

        var context = new JobContext
        {
            Job = Job(suite, jobId, items),
            Items = items,
            ReportItemAsync = (_, _) => default,
            IsCancelledAsync = _ =>
            {
                calls++;
                return new ValueTask<bool>(calls > 0);
            },
        };

        await handler.ExecuteAsync(context);

        agent.Calls.ShouldBeEmpty();

        var completed = await evalStore.GetRunAsync(TenantId, run.Id);
        completed!.Status.ShouldBe(EvalRunStatus.Cancelled);
    }

    [Fact]
    public async Task NumRepetitions_case_is_counted_failed_when_one_repetition_fails()
    {
        var evalStore = new InMemoryEvalStore();
        var agent = new MapAgent();
        var responses = new Queue<string>(["a long enough answer", "sh"]);
        agent.ResponseFactory = _ => responses.Dequeue();
        var handler = CreateHandler(evalStore, agent);

        var suite = await evalStore.SaveSuiteAsync(Suite(Checks("""[{"kind":"nonEmpty","minLength":5}]""")));
        var cases = await evalStore.ReplaceCasesAsync(suite.Id, [CaseInput("question")]);
        var jobId = Guid.NewGuid();
        var run = await evalStore.CreateRunAsync(Run(suite.Id, jobId));
        var reported = new List<JobItemResult>();

        var context = BuildContextForCases(
            suite,
            cases,
            jobId,
            reported,
            payloadOverride: PayloadWithRepetitions(suite.Name, 2));

        await handler.ExecuteAsync(context);

        reported[0].Status.ShouldBe(JobItemStatus.Failed);

        var results = await evalStore.ListCaseResultsAsync(TenantId, run.Id);
        results.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Version_in_the_payload_is_pinned_and_run_against_that_version()
    {
        var evalStore = new InMemoryEvalStore();
        var agent = new MapAgent();
        var catalog = new SingleAgentCatalog(agent, version: 7, modelId: "gpt-test");
        var handler = new EvalJobHandler(evalStore, catalog, new EvalCheckRegistry([]), NullLogger<EvalJobHandler>.Instance);

        var suite = await evalStore.SaveSuiteAsync(Suite(Checks("""[{"kind":"nonEmpty","minLength":1}]""")));
        var cases = await evalStore.ReplaceCasesAsync(suite.Id, [CaseInput("question")]);
        var jobId = Guid.NewGuid();
        var run = await evalStore.CreateRunAsync(Run(suite.Id, jobId));
        var reported = new List<JobItemResult>();

        var context = BuildContextForCases(
            suite,
            cases,
            jobId,
            reported,
            payloadOverride: PayloadWithVersion(suite.Name, 3));

        await handler.ExecuteAsync(context);

        // Even though the catalog's current version is 7, the 3 in the payload
        // is pinned: eval runs against a fixed version, not a moving target.
        catalog.LastRequestedVersion.ShouldBe(3);

        var completed = await evalStore.GetRunAsync(TenantId, run.Id);
        completed!.AgentVersion.ShouldBe(3);
    }

    private static JsonElement PayloadWithVersion(string suiteName, int agentVersion)
        => JsonSerializer.SerializeToElement(new { suiteName, agentVersion });

    private static EvalJobHandler CreateHandler(IEvalStore evalStore, AIAgent agent, int? agentVersion = 7, string? modelId = "gpt-test")
        => new(
            evalStore,
            new SingleAgentCatalog(agent, agentVersion, modelId),
            new EvalCheckRegistry([]),
            NullLogger<EvalJobHandler>.Instance);

    private static EvalSuite Suite(JsonElement checks) => new()
    {
        TenantId = TenantId,
        Name = "customer-support-suite",
        AgentName = AgentName,
        Checks = checks,
    };

    private static EvalCase CaseInput(string query) => new() { SuiteId = Guid.Empty, Seq = 0, Query = query };

    private static EvalRun Run(Guid suiteId, Guid jobId) => new()
    {
        Id = Guid.Empty,
        TenantId = TenantId,
        SuiteId = suiteId,
        JobId = jobId,
        Status = EvalRunStatus.Pending,
        Total = 0,
        StartedAt = DateTimeOffset.UtcNow,
    };

    private static JsonElement Checks(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static JsonElement Payload(string suiteName) => JsonSerializer.SerializeToElement(new { suiteName });

    private static JsonElement PayloadWithRepetitions(string suiteName, int numRepetitions)
        => JsonSerializer.SerializeToElement(new { suiteName, numRepetitions });

    private static JobItemRecord Item(int seq, Guid caseId)
        => new() { Id = Guid.NewGuid(), JobId = Guid.NewGuid(), Seq = seq, Input = caseId.ToString(), Status = JobItemStatus.Pending };

    private static JobRecord Job(EvalSuite? suite, Guid jobId, IReadOnlyList<JobItemRecord> items) => new()
    {
        Id = jobId,
        TenantId = TenantId,
        Kind = JobKind.Eval,
        TargetName = suite?.AgentName ?? AgentName,
        Status = JobStatus.Running,
        Payload = Payload(suite?.Name ?? "no-such-suite"),
        TotalItems = items.Count,
        ScheduledFor = DateTimeOffset.UtcNow,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    private static JobContext BuildContext(
        IEvalStore evalStore,
        EvalSuite? suite,
        IReadOnlyList<JobItemRecord> items,
        Guid jobId)
        => new()
        {
            Job = Job(suite, jobId, items),
            Items = items,
            ReportItemAsync = (_, _) => default,
            IsCancelledAsync = _ => new ValueTask<bool>(false),
        };

    private static JobContext BuildContextForCases(
        EvalSuite suite,
        IReadOnlyList<EvalCase> cases,
        Guid jobId,
        List<JobItemResult> reported,
        JsonElement? payloadOverride = null)
    {
        var items = cases.Select(static (evalCase, seq) => Item(seq, evalCase.Id)).ToArray();

        return new JobContext
        {
            Job = Job(suite, jobId, items) with { Payload = payloadOverride ?? Payload(suite.Name) },
            Items = items,
            ReportItemAsync = (result, _) =>
            {
                reported.Add(result);
                return default;
            },
            IsCancelledAsync = _ => new ValueTask<bool>(false),
        };
    }

    private sealed class SingleAgentCatalog(AIAgent? agent, int? version, string? modelId) : IAgentCatalog
    {
        public ValueTask<IReadOnlyList<AgentDescriptor>> ListAsync(CancellationToken cancellationToken = default)
            => new((IReadOnlyList<AgentDescriptor>)
            [
                new AgentDescriptor
                {
                    Name = AgentName,
                    Origin = AgentDefinitionOrigin.Code,
                    SourceName = "test",
                    Version = version ?? 1,
                    Model = modelId is null ? null : new ModelBinding { Provider = "openai", Model = modelId },
                },
            ]);

        public ValueTask<AIAgent?> ResolveAsync(string agentName, CancellationToken cancellationToken)
            => new(string.Equals(agentName, AgentName, StringComparison.Ordinal) ? agent : null);

        /// <summary>The version from the last <see cref="ResolveAsync(string, int?, CancellationToken)"/> call.</summary>
        public int? LastRequestedVersion { get; private set; }

        public ValueTask<AIAgent?> ResolveAsync(string agentName, int? version, CancellationToken cancellationToken = default)
        {
            LastRequestedVersion = version;
            return new(string.Equals(agentName, AgentName, StringComparison.Ordinal) ? agent : null);
        }
    }

    /// <summary>The smallest fake agent that produces a configurable response based on input.</summary>
    private sealed class MapAgent : AIAgent
    {
        public Dictionary<string, string> Responses { get; } = new(StringComparer.Ordinal);

        public Func<string, string>? ResponseFactory { get; set; }

        public List<string> Calls { get; } = [];

        public override string Name => AgentName;

        public override string? Description => "for testing";

        protected override Task<AgentResponse> RunCoreAsync(
            IEnumerable<ChatMessage> messages,
            AgentSession? session = null,
            AgentRunOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var query = messages.LastOrDefault()?.Text ?? string.Empty;
            Calls.Add(query);

            var text = ResponseFactory?.Invoke(query)
                ?? (Responses.TryGetValue(query, out var configured) ? configured : $"long enough answer: {query}");

            return Task.FromResult(new AgentResponse(new ChatMessage(ChatRole.Assistant, text)));
        }

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
            => new(new ScriptedSession());

        protected override ValueTask<AgentSession> DeserializeSessionCoreAsync(
            JsonElement serializedState,
            JsonSerializerOptions? jsonSerializerOptions = null,
            CancellationToken cancellationToken = default)
            => new(new ScriptedSession());

        protected override ValueTask<JsonElement> SerializeSessionCoreAsync(
            AgentSession session,
            JsonSerializerOptions? jsonSerializerOptions = null,
            CancellationToken cancellationToken = default)
            => new(EmptyState);

        private static JsonElement EmptyState { get; } = JsonDocument.Parse("{}").RootElement.Clone();

        private sealed class ScriptedSession : AgentSession;
    }
}
