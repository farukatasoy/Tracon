using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Tracon.Workflows.UnitTests.Fakes;

namespace Tracon.Workflows.UnitTests;

/// <summary>
/// The reader of what the earlier runs of one logical workflow run spent (F-531): which rows it
/// follows, what it adds, and what it does when it cannot read.
/// </summary>
public sealed class BudgetLineageTests
{
    private const string Tenant = "test";
    private static readonly DateTimeOffset Start = new(2026, 10, 24, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task A_run_that_continues_nothing_carries_what_it_spent_itself()
    {
        var (store, clock) = Setup();
        var run = Guid.NewGuid();

        await OpenAsync(store, run, minutesAgo: 10);
        await CloseAsync(store, run, minutes: 4, tokens: 30, amount: 0.5m);

        var carry = await ReadAsync(store, clock, run);

        carry.Tokens.ShouldBe(30);
        carry.Cost.ShouldBe(0.5m);
        carry.ActiveDuration.ShouldBe(TimeSpan.FromMinutes(4));
    }

    [Fact]
    public async Task A_recovery_chain_is_followed_back_through_every_link()
    {
        var (store, clock) = Setup();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var third = Guid.NewGuid();

        await OpenAsync(store, first, minutesAgo: 60);
        await CloseAsync(store, first, minutes: 5, tokens: 10, amount: 1m);
        await OpenAsync(store, second, minutesAgo: 50, continuedFrom: first);
        await CloseAsync(store, second, minutes: 6, tokens: 20, amount: 2m);
        await OpenAsync(store, third, minutesAgo: 40, continuedFrom: second);
        await CloseAsync(store, third, minutes: 7, tokens: 40, amount: 4m);

        var carry = await ReadAsync(store, clock, third);

        carry.Tokens.ShouldBe(70);
        carry.Cost.ShouldBe(7m);
        carry.ActiveDuration.ShouldBe(TimeSpan.FromMinutes(18));
    }

    [Fact]
    public async Task The_time_a_workflow_waited_for_a_person_is_not_active_time()
    {
        var (store, clock) = Setup();
        var waiting = Guid.NewGuid();
        var answer = Guid.NewGuid();

        // Ran 10 minutes, then waited three days for a person: the row closed when it began to wait.
        await OpenAsync(store, waiting, minutesAgo: 3 * 24 * 60 + 10);
        await CloseAsync(store, waiting, minutes: 10, status: RunStatus.AwaitingInput);
        await OpenAsync(store, answer, minutesAgo: 1);
        await AnswersAsync(store, answer, waiting);
        await CloseAsync(store, answer, minutes: 1);

        var carry = await ReadAsync(store, clock, answer);

        carry.ActiveDuration.ShouldBe(TimeSpan.FromMinutes(11), "the wait between the two runs is not in it");
    }

    [Fact]
    public async Task A_run_that_answered_a_person_is_followed_to_the_run_that_waited()
    {
        var (store, clock) = Setup();
        var waiting = Guid.NewGuid();
        var answer = Guid.NewGuid();
        var secondAnswer = Guid.NewGuid();

        await OpenAsync(store, waiting, minutesAgo: 90);
        await CloseAsync(store, waiting, minutes: 10, tokens: 100, amount: 0.8m, status: RunStatus.AwaitingInput);
        await OpenAsync(store, answer, minutesAgo: 60);
        await AnswersAsync(store, answer, waiting);
        await CloseAsync(store, answer, minutes: 5, tokens: 10, amount: 0.1m, status: RunStatus.AwaitingInput);
        await OpenAsync(store, secondAnswer, minutesAgo: 30);
        await AnswersAsync(store, secondAnswer, answer);
        await CloseAsync(store, secondAnswer, minutes: 2, tokens: 5, amount: 0.05m);

        var carry = await ReadAsync(store, clock, secondAnswer);

        carry.Tokens.ShouldBe(115);
        carry.Cost.ShouldBe(0.95m);
        carry.ActiveDuration.ShouldBe(TimeSpan.FromMinutes(17));
    }

    [Fact]
    public async Task A_recovery_of_an_answer_is_followed_through_both_kinds_of_link()
    {
        var (store, clock) = Setup();
        var waiting = Guid.NewGuid();
        var answer = Guid.NewGuid();
        var recovered = Guid.NewGuid();

        await OpenAsync(store, waiting, minutesAgo: 90);
        await CloseAsync(store, waiting, minutes: 10, tokens: 100, status: RunStatus.AwaitingInput);
        await OpenAsync(store, answer, minutesAgo: 60);
        await AnswersAsync(store, answer, waiting);
        await CloseAsync(store, answer, minutes: 3, tokens: 7, status: RunStatus.Failed);
        await OpenAsync(store, recovered, minutesAgo: 50, continuedFrom: answer);
        await CloseAsync(store, recovered, minutes: 2, tokens: 3);

        var carry = await ReadAsync(store, clock, recovered);

        carry.Tokens.ShouldBe(110);
        carry.ActiveDuration.ShouldBe(TimeSpan.FromMinutes(15));
    }

    [Fact]
    public async Task The_children_of_each_run_count_with_the_run()
    {
        var (store, clock) = Setup();
        var run = Guid.NewGuid();

        await OpenAsync(store, run, minutesAgo: 10);
        await OpenChildAsync(store, Guid.NewGuid(), parent: run, tokens: 40, inputCost: 0.2m);
        await OpenChildAsync(store, Guid.NewGuid(), parent: run, tokens: 60, inputCost: 0.3m);
        await CloseAsync(store, run, minutes: 4, amount: 0.5m);

        var carry = await ReadAsync(store, clock, run);

        carry.Runs.ShouldBe(2);
        carry.Tokens.ShouldBe(100);
        carry.Cost.ShouldBe(1.0m, "the children's token cost and the root's reported amount, added once");
    }

    [Fact]
    public async Task A_run_an_interruption_closed_is_counted_from_the_items_its_function_nodes_reported()
    {
        // The recovery closes the row of a run whose process is gone, and the row holds no snapshot of what
        // its nodes reported. The items were written as they were reported.
        var (store, clock) = Setup();
        var run = Guid.NewGuid();

        await OpenAsync(store, run, minutesAgo: 10);
        await ReportAsync(store, run, 0.6m);
        await ReportAsync(store, run, 0.1m);
        await ReportAsync(store, run, 0.3m, currency: "EUR");
        await CloseAsync(store, run, minutes: 2, status: RunStatus.Failed);

        (await ReadAsync(store, clock, run)).Cost.ShouldBe(0.7m, "the items in the currency Tracon prices in, by the rule the run itself would have used");
    }

    [Fact]
    public async Task A_run_that_holds_its_snapshot_is_not_counted_a_second_time_from_its_items()
    {
        var (store, clock) = Setup();
        var run = Guid.NewGuid();

        await OpenAsync(store, run, minutesAgo: 10);
        await ReportAsync(store, run, 0.5m);
        await CloseAsync(store, run, minutes: 2, amount: 0.5m);

        (await ReadAsync(store, clock, run)).Cost.ShouldBe(0.5m);
    }

    [Theory]
    [InlineData("interrupted")]
    [InlineData("orphaned")]
    public async Task The_wait_between_a_crash_and_the_recovery_is_not_active_time(string errorType)
    {
        // The host was down for two hours; the recovery closed the row when it came back. The run executed for ten
        // minutes. The closing event carries the recovery's clock, so it is not where the run stopped.
        var (store, clock) = Setup();
        var run = Guid.NewGuid();

        await OpenAsync(store, run, minutesAgo: 200);
        await AppendAsync(store, run, 0, RunEventType.RunStarted, minutesAfterStart: 0, startedMinutesAgo: 200);
        await AppendAsync(store, run, 1, RunEventType.ExecutorInvoked, minutesAfterStart: 10, startedMinutesAgo: 200);
        await AppendAsync(store, run, 2, RunEventType.RunFailed, minutesAfterStart: 130, startedMinutesAgo: 200);
        await CloseAsync(store, run, minutes: 130, status: RunStatus.Failed, errorType: errorType, startedMinutesAgo: 200);

        (await ReadAsync(store, clock, run)).ActiveDuration.ShouldBe(TimeSpan.FromMinutes(10));
    }

    [Fact]
    public async Task A_run_closed_by_someone_else_that_wrote_nothing_but_the_closing_event_executed_for_no_time()
    {
        var (store, clock) = Setup();
        var run = Guid.NewGuid();

        await OpenAsync(store, run, minutesAgo: 200);
        await AppendAsync(store, run, 0, RunEventType.RunFailed, minutesAfterStart: 130, startedMinutesAgo: 200);
        await CloseAsync(store, run, minutes: 130, status: RunStatus.Failed, errorType: "interrupted", startedMinutesAgo: 200);

        (await ReadAsync(store, clock, run)).ActiveDuration.ShouldBe(TimeSpan.Zero);
    }

    [Fact]
    public async Task A_run_closed_by_someone_else_whose_events_are_gone_is_counted_to_the_closing()
    {
        var (store, clock) = Setup();
        var run = Guid.NewGuid();

        await OpenAsync(store, run, minutesAgo: 200);
        await CloseAsync(store, run, minutes: 130, status: RunStatus.Failed, errorType: "interrupted", startedMinutesAgo: 200);

        (await ReadAsync(store, clock, run)).ActiveDuration.ShouldBe(TimeSpan.FromMinutes(130));
    }

    [Fact]
    public async Task A_run_that_failed_on_its_own_is_counted_to_its_closing_whatever_its_events_say()
    {
        // Only a run someone else closed is read from its events: a failure the run reported itself closes at the
        // moment the run stopped.
        var (store, clock) = Setup();
        var run = Guid.NewGuid();

        await OpenAsync(store, run, minutesAgo: 20);
        await AppendAsync(store, run, 0, RunEventType.RunStarted, minutesAfterStart: 0, startedMinutesAgo: 20);
        await CloseAsync(store, run, minutes: 7, status: RunStatus.Failed, errorType: "run_budget_exceeded");

        (await ReadAsync(store, clock, run)).ActiveDuration.ShouldBe(TimeSpan.FromMinutes(7));
    }

    [Fact]
    public async Task A_run_that_has_not_closed_is_counted_up_to_now_and_the_log_says_so()
    {
        var (store, clock) = Setup();
        var run = Guid.NewGuid();
        var logs = new CapturingLoggerProvider();

        await OpenAsync(store, run, minutesAgo: 6);

        var carry = await ReadAsync(store, clock, run, logs.CreateLogger("lineage"));

        carry.ActiveDuration.ShouldBe(TimeSpan.FromMinutes(6));
        logs.Entries.ShouldContain(static entry => entry.Level == LogLevel.Warning && entry.Message.Contains("has not closed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_link_that_loops_counts_each_run_once()
    {
        var (store, clock) = Setup();
        var one = Guid.NewGuid();
        var two = Guid.NewGuid();

        await OpenAsync(store, one, minutesAgo: 20, continuedFrom: two);
        await CloseAsync(store, one, minutes: 1, tokens: 10);
        await OpenAsync(store, two, minutesAgo: 10, continuedFrom: one);
        await CloseAsync(store, two, minutes: 1, tokens: 20);

        var carry = await ReadAsync(store, clock, two);

        carry.Tokens.ShouldBe(30);
    }

    [Fact]
    public async Task A_chain_longer_than_the_bound_is_refused_and_not_read_in_part()
    {
        var (store, clock) = Setup();
        Guid? previous = null;
        var last = Guid.Empty;

        for (var index = 0; index <= BudgetLineage.MaxLength; index++)
        {
            last = Guid.NewGuid();
            await OpenAsync(store, last, minutesAgo: 1_000 - index, continuedFrom: previous);
            await CloseAsync(store, last, minutes: 1, tokens: 1);
            previous = last;
        }

        var refusal = await Should.ThrowAsync<TraconException>(async () => await ReadAsync(store, clock, last));

        refusal.Message.ShouldContain($"longer than {BudgetLineage.MaxLength}", Case.Sensitive);
    }

    [Fact]
    public async Task A_chain_of_exactly_the_bound_is_read()
    {
        var (store, clock) = Setup();
        Guid? previous = null;
        var last = Guid.Empty;

        for (var index = 0; index < BudgetLineage.MaxLength; index++)
        {
            last = Guid.NewGuid();
            await OpenAsync(store, last, minutesAgo: 1_000 - index, continuedFrom: previous);
            await CloseAsync(store, last, minutes: 1, tokens: 1);
            previous = last;
        }

        (await ReadAsync(store, clock, last)).Tokens.ShouldBe(BudgetLineage.MaxLength);
    }

    [Fact]
    public async Task A_predecessor_the_store_no_longer_holds_ends_the_chain_and_the_log_says_so()
    {
        var (store, clock) = Setup();
        var run = Guid.NewGuid();
        var logs = new CapturingLoggerProvider();

        await OpenAsync(store, run, minutesAgo: 10, continuedFrom: Guid.NewGuid());
        await CloseAsync(store, run, minutes: 2, tokens: 9);

        var carry = await ReadAsync(store, clock, run, logs.CreateLogger("lineage"));

        carry.Tokens.ShouldBe(9);
        logs.Entries.ShouldContain(static entry => entry.Level == LogLevel.Warning && entry.Message.Contains("was not found", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Another_tenants_run_is_not_read()
    {
        var (store, clock) = Setup();
        var own = Guid.NewGuid();
        var foreign = Guid.NewGuid();

        await OpenAsync(store, foreign, minutesAgo: 30, tenant: "other");
        await CloseAsync(store, foreign, minutes: 5, tokens: 500, tenant: "other", startedMinutesAgo: 30);
        await OpenAsync(store, own, minutesAgo: 20, continuedFrom: foreign);
        await CloseAsync(store, own, minutes: 1, tokens: 1);

        (await ReadAsync(store, clock, own)).Tokens.ShouldBe(1, "a tenant's chain cannot reach into another tenant's rows");
    }

    [Fact]
    public async Task A_store_that_cannot_be_read_fails_the_read_and_never_returns_a_zero()
    {
        var clock = new AdjustableClock(Start);
        var failing = new FaultyReadRunStore(
            new InMemoryRunStore(tenantContext: new WorkflowTestHost.FixedTenantContext()),
            static _ => new IOException("the run store is down"));

        var failure = await Should.ThrowAsync<TraconException>(
            async () => await BudgetLineage.ReadAsync(failing, Guid.NewGuid(), Tenant, "USD", clock, NullLogger.Instance, CancellationToken.None));

        failure.Message.ShouldContain("could not be read", Case.Sensitive);
        failure.InnerException.ShouldBeOfType<IOException>();
    }

    [Fact]
    public async Task A_cancellation_is_not_turned_into_a_failure_to_read()
    {
        var clock = new AdjustableClock(Start);
        var store = new InMemoryRunStore(tenantContext: new WorkflowTestHost.FixedTenantContext());

        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(
            async () => await BudgetLineage.ReadAsync(
                new FaultyReadRunStore(store, static token => new OperationCanceledException(token)),
                Guid.NewGuid(),
                Tenant,
                "USD",
                clock,
                NullLogger.Instance,
                cancellation.Token));
    }

    [Fact]
    public void The_payload_of_the_link_names_the_run_that_waited_and_a_cut_one_names_nobody()
    {
        var source = Guid.NewGuid();

        WorkflowResponseLink.ReadSourceRunId(WorkflowResponseLink.Write(source, "request-1")).ShouldBe(source);
        WorkflowResponseLink.ReadSourceRunId(null).ShouldBeNull();
        WorkflowResponseLink.ReadSourceRunId("").ShouldBeNull();
        WorkflowResponseLink.ReadSourceRunId("""{"sourceRunId":"not-a-guid"}""").ShouldBeNull();
        WorkflowResponseLink.ReadSourceRunId("""{"requestId":"r"}""").ShouldBeNull();
        WorkflowResponseLink.ReadSourceRunId($$"""{"sourceRunId":"{{source}}","requestId":"cut""").ShouldBeNull();
    }

    private static (InMemoryRunStore Store, AdjustableClock Clock) Setup()
        => (new InMemoryRunStore(tenantContext: new WorkflowTestHost.FixedTenantContext()), new AdjustableClock(Start));

    private static async ValueTask<BudgetCarryOver> ReadAsync(InMemoryRunStore store, AdjustableClock clock, Guid run, ILogger? logger = null)
        => await BudgetLineage.ReadAsync(store, run, Tenant, "USD", clock, logger ?? NullLogger.Instance, CancellationToken.None);

    private static async Task OpenAsync(
        InMemoryRunStore store,
        Guid run,
        int minutesAgo,
        Guid? continuedFrom = null,
        string tenant = Tenant)
        => await store.StartRunAsync(new RunStartInfo
        {
            RunId = run,
            AgentName = "podcast",
            Kind = RunKind.Workflow,
            WorkflowName = "podcast",
            Status = RunStatus.Running,
            StartedAt = Start - TimeSpan.FromMinutes(minutesAgo),
            TenantId = tenant,
            ContinuedFromRunId = continuedFrom,
        });

    private static async Task CloseAsync(
        InMemoryRunStore store,
        Guid run,
        int minutes,
        long tokens = 0,
        decimal? amount = null,
        RunStatus status = RunStatus.Completed,
        string tenant = Tenant,
        int? startedMinutesAgo = null,
        string? errorType = null)
    {
        // A row of another tenant is not visible to the store's ambient tenant: its start is given.
        var startedAt = startedMinutesAgo is { } ago
            ? Start - TimeSpan.FromMinutes(ago)
            : (await store.GetRunAsync(run)).ShouldNotBeNull().StartedAt;

        await store.CompleteRunAsync(new RunCompletion
        {
            RunId = run,
            Status = status,
            CompletedAt = startedAt + TimeSpan.FromMinutes(minutes),
            Usage = tokens > 0 ? new RunUsage { InputTokens = tokens, TotalTokens = tokens } : null,
            NonTokenCost = amount is { } value ? new RunNonTokenCost { Amount = value, Currency = "USD" } : null,
            Error = errorType is null ? null : new RunError { Type = errorType, Message = "The run stopped." },
            TenantId = tenant,
        });
    }

    private static async Task AppendAsync(
        InMemoryRunStore store,
        Guid run,
        long sequence,
        RunEventType type,
        int minutesAfterStart,
        int startedMinutesAgo)
        => await store.AppendEventAsync(new RunEvent
        {
            RunId = run,
            Sequence = sequence,
            Type = type,
            Timestamp = Start - TimeSpan.FromMinutes(startedMinutesAgo) + TimeSpan.FromMinutes(minutesAfterStart),
            TenantId = Tenant,
        });

    private static async Task ReportAsync(InMemoryRunStore store, Guid run, decimal amount, string currency = "USD")
        => await store.RecordNonTokenUsageAsync(new NonTokenUsageRecord
        {
            Id = Guid.NewGuid(),
            RunId = run,
            NodeName = "speak",
            Usage = new ToolCallUsage
            {
                Unit = ToolUsageUnits.Characters,
                Quantity = 100m,
                Cost = amount,
                Currency = currency,
                Provider = "elevenlabs",
                Model = "eleven_multilingual_v2",
            },
            CreatedAt = Start,
            TenantId = Tenant,
        });

    private static async Task OpenChildAsync(InMemoryRunStore store, Guid run, Guid parent, long tokens, decimal inputCost)
    {
        await store.StartRunAsync(new RunStartInfo
        {
            RunId = run,
            AgentName = "writer",
            Status = RunStatus.Running,
            StartedAt = Start - TimeSpan.FromMinutes(9),
            TenantId = Tenant,
            ParentRunId = parent,
            RootRunId = parent,
            Depth = 1,
        });

        await store.CompleteRunAsync(new RunCompletion
        {
            RunId = run,
            Status = RunStatus.Completed,
            CompletedAt = Start - TimeSpan.FromMinutes(8),
            Usage = new RunUsage { InputTokens = tokens, TotalTokens = tokens },
            Cost = new RunCost { InputCost = inputCost, Currency = "USD", Source = PricingSource.Catalog },
            TenantId = Tenant,
        });
    }

    private static async Task AnswersAsync(InMemoryRunStore store, Guid answer, Guid waiting)
        => await store.AppendEventAsync(new RunEvent
        {
            RunId = answer,
            Sequence = 1,
            Type = RunEventType.WorkflowResponseApplied,
            Timestamp = Start,
            Payload = WorkflowResponseLink.Write(waiting, "request-1"),
            TenantId = Tenant,
        });
}
