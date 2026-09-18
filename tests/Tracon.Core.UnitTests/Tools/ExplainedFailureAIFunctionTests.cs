using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Tools;

/// <summary>
/// HATA-S1-024: a tool's own explanation reaches the model instead of
/// <c>"Error: Function failed."</c>.
/// </summary>
/// <remarks>
/// <para>
/// Microsoft Agent Framework converts a thrown exception into that generic
/// sentence unless <c>IncludeDetailedErrors</c> is on, and Tracon does not
/// turn it on — doing so would publish every exception's detail, including a
/// provider SDK's own message. A <c>TraconException</c> is different in kind:
/// its message is a sentence Tracon wrote for this exact audience.
/// </para>
/// <para>
/// Measured scope before the fix: 17 runtime throws across five types
/// (<c>TranscribeTool</c>, <c>SpeakTool</c>, <c>VoiceToolBase</c>,
/// <c>GenerateImageTool</c>, <c>ValidatingAIFunction</c>), all of which the
/// model saw as the same six words.
/// </para>
/// </remarks>
public sealed class ExplainedFailureAIFunctionTests
{
    [Fact]
    public async Task A_TraconException_becomes_the_result_the_model_reads()
    {
        var wrapped = new ExplainedFailureAIFunction(
            Throwing(new TraconException("'cat.png' is not an audio file (type: image/png).")));

        var result = await wrapped.InvokeAsync(
            new AIFunctionArguments(StringComparer.Ordinal), TestContext.Current.CancellationToken);

        result?.ToString().ShouldBe("'cat.png' is not an audio file (type: image/png).");
    }

    /// <summary>
    /// The layer is narrow on purpose: turning ANY exception into a result is
    /// what <c>IncludeDetailedErrors</c> does, and that publishes a provider
    /// SDK's message and whatever a stack trace carries.
    /// </summary>
    [Fact]
    public async Task Any_other_exception_still_travels()
    {
        var wrapped = new ExplainedFailureAIFunction(
            Throwing(new InvalidOperationException("upstream connection reset")));

        var thrown = await Should.ThrowAsync<InvalidOperationException>(
            async () => await wrapped.InvokeAsync(
                new AIFunctionArguments(StringComparer.Ordinal), TestContext.Current.CancellationToken));

        thrown.Message.ShouldBe("upstream connection reset");
    }

    [Fact]
    public async Task A_cancellation_still_travels()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var wrapped = new ExplainedFailureAIFunction(
            Throwing(new OperationCanceledException()));

        await Should.ThrowAsync<OperationCanceledException>(
            async () => await wrapped.InvokeAsync(new AIFunctionArguments(StringComparer.Ordinal), cts.Token));
    }

    [Fact]
    public async Task A_successful_call_is_untouched()
    {
        var wrapped = new ExplainedFailureAIFunction(
            AIFunctionFactory.Create((Func<string>)(() => "the answer"), "ok"));

        var result = await wrapped.InvokeAsync(
            new AIFunctionArguments(StringComparer.Ordinal), TestContext.Current.CancellationToken);

        result?.ToString().ShouldBe("the answer");
    }

    /// <summary>
    /// The reason a rejected call matters most: a model that cannot read WHY
    /// its arguments were refused repeats the same call.
    /// </summary>
    [Fact]
    public async Task An_argument_rejection_reaches_the_model_through_the_chain()
    {
        var validating = new ValidatingAIFunction(
            AIFunctionFactory.Create((Func<string>)(() => "never reached"), "guarded"),
            new ToolDescriptor { Name = "guarded" },
            new RejectingValidator("'amount' must be greater than zero."),
            NullLogger<ValidatingAIFunction>.Instance);

        var wrapped = new ExplainedFailureAIFunction(validating);

        var result = await wrapped.InvokeAsync(
            new AIFunctionArguments(StringComparer.Ordinal), TestContext.Current.CancellationToken);

        result?.ToString().ShouldBe("'amount' must be greater than zero.");
    }

    private static AIFunction Throwing(Exception exception)
        => AIFunctionFactory.Create((Func<string>)(() => throw exception), "throwing");

    private sealed class RejectingValidator(string reason) : IToolArgumentsValidator
    {
        public ValueTask<ToolArgumentsValidationResult> ValidateAsync(
            ToolDescriptor descriptor,
            AIFunctionArguments arguments,
            CancellationToken cancellationToken = default)
            => new(ToolArgumentsValidationResult.Invalid(reason));
    }
}
