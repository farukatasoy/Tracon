using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tracon.Core.UnitTests.Tools;

/// <summary>
/// Unit-level coverage of <see cref="ValidatingAIFunction"/>: the argument
/// validation gate installed by <see cref="ToolWrapperChain"/> (docs/127, 127.2).
/// </summary>
public sealed class ToolArgumentsValidatorTests
{
    private static ToolDescriptor Descriptor(string name = "tool") => new() { Name = name };

    private static ValidatingAIFunction Wrap(AIFunction inner, IToolArgumentsValidator validator, string name = "tool")
        => new(inner, Descriptor(name), validator, NullLogger<ValidatingAIFunction>.Instance);

    [Fact]
    public async Task Valid_arguments_run_the_real_function()
    {
        var ran = false;
        var inner = AIFunctionFactory.Create(() => { ran = true; return "ok"; }, "tool");
        var wrapped = Wrap(inner, new StaticValidator(ToolArgumentsValidationResult.Valid));

        var result = await wrapped.InvokeAsync(new AIFunctionArguments(StringComparer.Ordinal));

        ran.ShouldBeTrue();
        result?.ToString().ShouldBe("ok");
    }

    [Fact]
    public async Task Rejected_arguments_never_reach_the_real_function_and_throw_with_the_safe_reason()
    {
        var ran = false;
        var inner = AIFunctionFactory.Create(() => { ran = true; return "should never run"; }, "tool");
        var wrapped = Wrap(inner, new StaticValidator(ToolArgumentsValidationResult.Invalid("Field 'status' must be one of the allowed values.")));

        var exception = await Should.ThrowAsync<TraconException>(
            async () => await wrapped.InvokeAsync(new AIFunctionArguments(StringComparer.Ordinal)));

        ran.ShouldBeFalse();
        exception.Message.ShouldBe("Field 'status' must be one of the allowed values.");
    }

    [Fact]
    public async Task A_throwing_validator_rejects_the_call_fail_closed_instead_of_propagating()
    {
        var ran = false;
        var inner = AIFunctionFactory.Create(() => { ran = true; return "should never run"; }, "tool");
        var wrapped = Wrap(inner, new ThrowingValidator());

        // "Rejects" means a TraconException carrying a SAFE reason - not
        // the validator's own exception propagating and not the real body
        // running. This is the same fail-closed shape AuthorizingAIFunction
        // uses for a throwing IToolAuthorizationHandler.
        var exception = await Should.ThrowAsync<TraconException>(
            async () => await wrapped.InvokeAsync(new AIFunctionArguments(StringComparer.Ordinal)));

        ran.ShouldBeFalse();
        exception.ShouldNotBeOfType<InvalidOperationException>();
    }

    [Fact]
    public async Task An_empty_argument_set_does_not_crash_the_validator()
    {
        var inner = AIFunctionFactory.Create(() => "ok", "tool");
        var wrapped = Wrap(inner, new StaticValidator(ToolArgumentsValidationResult.Valid));

        var result = await wrapped.InvokeAsync(new AIFunctionArguments(StringComparer.Ordinal));

        result?.ToString().ShouldBe("ok");
    }

    [Fact]
    public async Task A_large_argument_value_reaches_the_validator_without_special_casing()
    {
        var seen = string.Empty;
        var inner = AIFunctionFactory.Create((string payload) => { seen = payload; return "ok"; }, "tool");
        var validator = new RecordingValidator();
        var wrapped = Wrap(inner, validator);

        var arguments = new AIFunctionArguments(StringComparer.Ordinal) { ["payload"] = new string('a', 1_000_000) };

        var result = await wrapped.InvokeAsync(arguments);

        validator.LastArguments.ShouldNotBeNull();
        result.ShouldNotBeNull();
    }

    private sealed class StaticValidator(ToolArgumentsValidationResult result) : IToolArgumentsValidator
    {
        public ValueTask<ToolArgumentsValidationResult> ValidateAsync(
            ToolDescriptor tool, AIFunctionArguments arguments, CancellationToken cancellationToken = default)
            => new(result);
    }

    private sealed class ThrowingValidator : IToolArgumentsValidator
    {
        public ValueTask<ToolArgumentsValidationResult> ValidateAsync(
            ToolDescriptor tool, AIFunctionArguments arguments, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("boom");
    }

    private sealed class RecordingValidator : IToolArgumentsValidator
    {
        public AIFunctionArguments? LastArguments { get; private set; }

        public ValueTask<ToolArgumentsValidationResult> ValidateAsync(
            ToolDescriptor tool, AIFunctionArguments arguments, CancellationToken cancellationToken = default)
        {
            LastArguments = arguments;
            return new ValueTask<ToolArgumentsValidationResult>(ToolArgumentsValidationResult.Valid);
        }
    }
}
