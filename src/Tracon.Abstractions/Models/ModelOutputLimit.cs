namespace Tracon;

/// <summary>
/// The output token limit a provider's chat client sends when
/// <see cref="ModelBinding.MaxOutputTokens"/> is empty.
/// </summary>
/// <remarks>
/// <para>
/// A provider that sends its own default limit lets its raw chat client return
/// this record from <c>IChatClient.GetService(typeof(ModelOutputLimit))</c> —
/// the same way a client describes itself with <c>ChatClientMetadata</c>. The
/// client is built for one binding, so the record can depend on the binding's
/// provider settings. A provider that sends no limit (the model's own maximum
/// applies) returns nothing.
/// </para>
/// <para>
/// The agent validation report reads it. When the binding leaves
/// <see cref="ModelBinding.MaxOutputTokens"/> empty, the default is below the
/// model's <see cref="ModelDescriptor.MaxOutputTokens"/> in the catalog, and the
/// agent asks for a structured response or <see cref="HighReasoningEffort"/> is
/// <see langword="true"/>, the report carries a warning: a response cut off at
/// the limit fails a structured run (<see cref="RunErrorClass.OutputTruncated"/>).
/// </para>
/// </remarks>
public sealed record ModelOutputLimit
{
    /// <summary>Gets the output token limit the client sends when the binding sets none.</summary>
    public required int DefaultMaxOutputTokens { get; init; }

    /// <summary>
    /// Gets a value indicating whether the binding asks the model to reason at high
    /// effort, so reasoning tokens are likely to take a large share of the same limit.
    /// </summary>
    public bool HighReasoningEffort { get; init; }
}
