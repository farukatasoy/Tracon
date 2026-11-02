using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>
/// Validates <see cref="TraconKnowledgeOptions"/> when the application starts.
/// </summary>
/// <remarks>
/// The text splitter and the vector store refuse these values the first time a document is uploaded
/// or searched, long after the host started; the checks here move that failure to the start.
/// </remarks>
internal sealed class TraconKnowledgeOptionsValidator : IValidateOptions<TraconKnowledgeOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, TraconKnowledgeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        List<string>? failures = null;

        AddWhenBelowOne(options.Dimensions, nameof(TraconKnowledgeOptions.Dimensions), ref failures);
        AddWhenBelowOne(options.ChunkSize, nameof(TraconKnowledgeOptions.ChunkSize), ref failures);
        AddWhenBelowOne(options.MaxResults, nameof(TraconKnowledgeOptions.MaxResults), ref failures);

        if (options.ChunkOverlap < 0 || options.ChunkOverlap >= Math.Max(1, options.ChunkSize))
        {
            // With an overlap of the chunk size or more the splitter cannot move forward.
            (failures ??= []).Add(
                $"{nameof(TraconKnowledgeOptions)}.{nameof(TraconKnowledgeOptions.ChunkOverlap)} " +
                $"must be at least 0 and less than {nameof(TraconKnowledgeOptions.ChunkSize)} ({options.ChunkSize}). " +
                $"Actual value: {options.ChunkOverlap}.");
        }

        return failures is null
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static void AddWhenBelowOne(int value, string memberName, ref List<string>? failures)
    {
        if (value < 1)
        {
            (failures ??= []).Add($"{nameof(TraconKnowledgeOptions)}.{memberName} must be at least 1. Actual value: {value}.");
        }
    }
}
