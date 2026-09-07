using Microsoft.Extensions.AI;

namespace AgentPrism.Testing.Contracts.Judges;

/// <summary>Behavior tests for an <see cref="IRunJudge"/> implementation.</summary>
public abstract class RunJudgeContract : IAsyncLifetime
{
    /// <summary>The judge under test.</summary>
    protected IRunJudge Judge { get; private set; } = null!;

    /// <summary>Creates the judge under test.</summary>
    protected abstract ValueTask<IRunJudge> CreateJudgeAsync();

    /// <summary>
    /// Gets the number of calls used by the deterministic concurrency probe.
    /// </summary>
    protected virtual int ConcurrentCallCount => 8;

    /// <summary>Creates a normal completed-run context.</summary>
    protected virtual RunJudgeContext CreateContext() => new()
    {
        RunId = Guid.NewGuid(),
        TenantId = "contract-tenant",
        AgentName = "contract-agent",
        Input = [new ChatMessage(ChatRole.User, "question")],
        Output = "answer",
    };

    /// <inheritdoc />
    public async ValueTask InitializeAsync() => Judge = await CreateJudgeAsync().ConfigureAwait(false);

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        return default;
    }

    [Fact]
    public async Task Name_is_a_stable_valid_identifier_across_calls()
    {
        var before = Judge.Name;
        await Judge.JudgeAsync(CreateContext()).ConfigureAwait(false);
        var after = Judge.Name;

        before.ShouldNotBeNullOrWhiteSpace();
        before.Length.ShouldBeLessThanOrEqualTo(64);
        before.All(static character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.').ShouldBeTrue();
        after.ShouldBe(before);
    }

    [Fact]
    public async Task Every_score_in_a_judgment_can_be_stored()
    {
        var judgment = await Judge.JudgeAsync(CreateContext()).ConfigureAwait(false);
        ShouldBeStorable(judgment);
    }

    /// <summary>Asserts that every score in a judgment satisfies the stored invariants.</summary>
    /// <param name="judgment">The judgment under test.</param>
    /// <remarks>
    /// An empty judgment passes: it says the judge reached no decision, and
    /// nothing is written. What must never happen is a judgment that AgentPrism
    /// then refuses to store — a repeated name, an out-of-range value, or a
    /// value shape that does not match its kind.
    /// </remarks>
    protected static void ShouldBeStorable(RunJudgment judgment)
    {
        ArgumentNullException.ThrowIfNull(judgment);

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var score in judgment.Scores)
        {
            RunScoreRules.IsValidName(score.Name).ShouldBeTrue(RunScoreRules.NameDescription);
            names.Add(score.Name).ShouldBeTrue($"The name '{score.Name}' is repeated in one judgment.");
            (score.Comment?.Length ?? 0).ShouldBeLessThanOrEqualTo(RunJudgment.MaxReasonLength);

            if (score.Kind == RunScoreKind.Categorical)
            {
                score.Value.ShouldBeNull();
                score.TextValue.ShouldNotBeNullOrEmpty();
                score.TextValue.Length.ShouldBeLessThanOrEqualTo(RunScoreRules.MaxTextValueLength);
                continue;
            }

            score.TextValue.ShouldBeNull();

            if (score.Value is not { } value)
            {
                // No measurement. Legal, and not the same as a zero.
                continue;
            }

            switch (score.Kind)
            {
                case RunScoreKind.Binary:
                    (value is 0 or 1).ShouldBeTrue($"A binary score must be 0 or 1, not {value}.");
                    break;
                case RunScoreKind.Stars:
                    value.ShouldBeInRange(1, 5);
                    break;
                default:
                    value.ShouldBeInRange(0, 100);
                    break;
            }
        }
    }

    [Fact]
    public async Task Whitespace_output_and_empty_tools_do_not_throw()
    {
        var context = CreateContext() with { Output = "   ", ToolNames = [] };
        await Should.NotThrowAsync(async () => await Judge.JudgeAsync(context).ConfigureAwait(false));
    }

    [Fact]
    public async Task Concurrent_calls_start_together_and_complete()
    {
        using var start = new Barrier(ConcurrentCallCount);
        var results = await Task.WhenAll(Enumerable.Range(0, ConcurrentCallCount).Select(_ => Task.Run(async () =>
        {
            start.SignalAndWait();
            return await Judge.JudgeAsync(CreateContext()).ConfigureAwait(false);
        }))).ConfigureAwait(false);

        foreach (var result in results)
        {
            ShouldBeStorable(result);
        }
    }

    [Fact]
    public async Task Repeated_calls_with_the_same_context_complete()
    {
        var context = CreateContext();
        var first = await Judge.JudgeAsync(context).ConfigureAwait(false);
        var second = await Judge.JudgeAsync(context).ConfigureAwait(false);

        ShouldBeStorable(first);
        ShouldBeStorable(second);
    }

    [Fact]
    public async Task A_pre_cancelled_token_is_honored()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Should.ThrowAsync<OperationCanceledException>(
            async () => await Judge.JudgeAsync(CreateContext(), cancellation.Token).ConfigureAwait(false));
    }
}
