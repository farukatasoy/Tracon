namespace Tracon;

/// <summary>The default <see cref="IStructuredResponseValidator"/>: every response is valid.</summary>
internal sealed class NoOpStructuredResponseValidator : IStructuredResponseValidator
{
    /// <summary>The shared instance.</summary>
    internal static readonly NoOpStructuredResponseValidator Instance = new();

    private NoOpStructuredResponseValidator()
    {
    }

    public ValueTask<StructuredResponseValidationResult> ValidateAsync(
        StructuredResponseValidationContext context, CancellationToken cancellationToken = default)
        => new(StructuredResponseValidationResult.Valid);
}
