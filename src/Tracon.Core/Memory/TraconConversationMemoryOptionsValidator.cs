using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>
/// Validates <see cref="TraconConversationMemoryOptions"/> when the application starts.
/// </summary>
/// <remarks>
/// This validation is handwritten. <c>ValidateDataAnnotations()</c> uses reflection
/// and produces <c>IL2026</c>.
/// </remarks>
internal sealed class TraconConversationMemoryOptionsValidator : IValidateOptions<TraconConversationMemoryOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, TraconConversationMemoryOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        List<string>? failures = null;

        if (options.VectorDimensions < 1)
        {
            (failures ??= []).Add(
                $"{nameof(TraconConversationMemoryOptions)}.{nameof(TraconConversationMemoryOptions.VectorDimensions)} " +
                $"must be at least 1 (the size of the vectors the embedding model makes). Actual value: {options.VectorDimensions}.");
        }

        if (string.IsNullOrWhiteSpace(options.CollectionName))
        {
            (failures ??= []).Add(
                $"{nameof(TraconConversationMemoryOptions)}.{nameof(TraconConversationMemoryOptions.CollectionName)} must not be empty.");
        }

        return failures is null
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
