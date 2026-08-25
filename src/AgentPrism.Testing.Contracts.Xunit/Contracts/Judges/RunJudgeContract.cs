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
    public async Task Judgment_score_is_null_or_in_range()
    {
        var judgment = await Judge.JudgeAsync(CreateContext()).ConfigureAwait(false);
        (judgment.Score is null || (judgment.Score >= 0 && judgment.Score <= 100)).ShouldBeTrue();
        (judgment.Reason?.Length ?? 0).ShouldBeLessThanOrEqualTo(RunJudgment.MaxReasonLength);
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

        results.All(static result => result.Score is null || (result.Score >= 0 && result.Score <= 100)).ShouldBeTrue();
    }

    [Fact]
    public async Task Repeated_calls_with_the_same_context_complete()
    {
        var context = CreateContext();
        var first = await Judge.JudgeAsync(context).ConfigureAwait(false);
        var second = await Judge.JudgeAsync(context).ConfigureAwait(false);

        (first.Score is null || (first.Score >= 0 && first.Score <= 100)).ShouldBeTrue();
        (second.Score is null || (second.Score >= 0 && second.Score <= 100)).ShouldBeTrue();
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
