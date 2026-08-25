using Microsoft.Extensions.AI;

namespace AgentPrism.Testing.Contracts.Judges;

/// <summary>Behavior tests for an <see cref="IRunJudge"/> implementation.</summary>
public abstract class RunJudgeContract : IAsyncLifetime
{
    /// <summary>The judge under test.</summary>
    protected IRunJudge Judge { get; private set; } = null!;

    /// <summary>Creates the judge under test.</summary>
    protected abstract ValueTask<IRunJudge> CreateJudgeAsync();

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
    public void Name_is_a_stable_valid_identifier()
    {
        Judge.Name.ShouldNotBeNullOrWhiteSpace();
        Judge.Name.Length.ShouldBeLessThanOrEqualTo(64);
        Judge.Name.All(static character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.').ShouldBeTrue();
        Judge.Name.ShouldBe(Judge.Name);
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
    public async Task Concurrent_calls_complete()
    {
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Judge.JudgeAsync(CreateContext()).AsTask())).ConfigureAwait(false);
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
    public async Task A_pre_cancelled_token_does_not_produce_an_unrelated_exception()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Should.NotThrowAsync(async () =>
        {
            try
            {
                _ = await Judge.JudgeAsync(CreateContext(), cancellation.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // A deterministic judge may ignore cancellation. A remote judge may honor it.
            }
        });
    }
}
