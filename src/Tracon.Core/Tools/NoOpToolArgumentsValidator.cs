using Microsoft.Extensions.AI;

namespace Tracon;

/// <summary>The default <see cref="IToolArgumentsValidator"/>: every call is valid.</summary>
/// <remarks>
/// <see cref="ToolWrapperChain.Compose"/> recognizes this specific instance and
/// skips installing <see cref="ValidatingAIFunction"/> entirely when it is the
/// resolved validator — an installation that never registers its own validator
/// pays no extra layer on the call path (the same pattern
/// <c>ContentGuardPipeline.HasGuards</c> uses).
/// </remarks>
internal sealed class NoOpToolArgumentsValidator : IToolArgumentsValidator
{
    /// <summary>The shared instance.</summary>
    internal static readonly NoOpToolArgumentsValidator Instance = new();

    private NoOpToolArgumentsValidator()
    {
    }

    public ValueTask<ToolArgumentsValidationResult> ValidateAsync(
        ToolDescriptor tool, AIFunctionArguments arguments, CancellationToken cancellationToken = default)
        => new(ToolArgumentsValidationResult.Valid);
}
