using System.Text.Json;

namespace AgentPrism;

/// <summary>
/// Determines the provider and the model an agent runs with. It carries
/// <em>no</em> credentials; the API key is resolved from configuration.
/// </summary>
public sealed record ModelBinding
{
    /// <summary>Gets the provider name, for example <c>openai</c>.</summary>
    public required string Provider { get; init; }

    /// <summary>Gets the model name, for example <c>gpt-5.4-mini</c>.</summary>
    public required string Model { get; init; }

    /// <summary>Gets the sampling temperature. The provider default is used when it is <see langword="null"/>.</summary>
    public float? Temperature { get; init; }

    /// <summary>Gets the upper output token limit. The provider default is used when it is <see langword="null"/>.</summary>
    public int? MaxOutputTokens { get; init; }

    /// <summary>Gets the nucleus sampling threshold. The provider default is used when it is <see langword="null"/>.</summary>
    public float? TopP { get; init; }

    /// <summary>
    /// Gets the reasoning effort level. Models that support it use the value; the other
    /// providers ignore it.
    /// </summary>
    /// <remarks>
    /// The valid values are the <c>Microsoft.Extensions.AI.ReasoningEffort</c> names:
    /// <c>None</c>, <c>Low</c>, <c>Medium</c>, <c>High</c>, <c>ExtraHigh</c>. The
    /// comparison is case insensitive. An unrecognized value is rejected while the agent
    /// is built, with <c>AgentPrismCompilationException</c>; it is not ignored silently.
    /// </remarks>
    public string? ReasoningEffort { get; init; }

    /// <summary>
    /// Gets the provider-specific extra settings. A key has the form <c>{provider}.{setting}</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The prompt caching of Anthropic or the safety thresholds of Gemini do not fit the
    /// fixed fields of this contract. Adding them to the body of <see cref="ModelBinding"/>
    /// would leak one vendor's concept into <c>AgentPrism.Abstractions</c>. This dictionary
    /// keeps the contract clean: each provider reads only its own prefix.
    /// </para>
    /// <para>
    /// <strong>An unknown key is not ignored silently.</strong> When a provider sees a key
    /// it does not recognize, the build fails and lists the keys it supports. The rationale
    /// is the same as for <see cref="ReasoningEffort"/> (decision K-034): a setting that is
    /// ignored silently makes the user miss the behaviour they expect without seeing why.
    /// </para>
    /// <para>
    /// Keys are compared with <see cref="StringComparer.OrdinalIgnoreCase"/>. Use the
    /// <see cref="ModelProviderSettings"/> helpers to read them.
    /// </para>
    /// <example>
    /// <code language="json">
    /// "ProviderSettings": {
    ///   "anthropic.promptCaching": true,
    ///   "anthropic.thinking.budgetTokens": 8000,
    ///   "google.safety.harassment": "BLOCK_ONLY_HIGH"
    /// }
    /// </code>
    /// </example>
    /// </remarks>
    public IReadOnlyDictionary<string, JsonElement> ProviderSettings { get; init; }
        = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets the requested output format. When it is <see langword="null"/> today's
    /// behaviour does not change: no format constraint is sent to the provider.
    /// </summary>
    /// <remarks>
    /// <see cref="AgentResponseFormatKind.Text"/> differs from <see langword="null"/>:
    /// <see langword="null"/> means "say nothing", <c>Text</c> means "ask for plain text
    /// explicitly". An invalid combination (for example
    /// <see cref="AgentResponseFormatKind.JsonSchema"/> without a schema) is rejected
    /// while the agent is built, with <c>AgentPrismCompilationException</c>.
    /// </remarks>
    public AgentResponseFormat? ResponseFormat { get; init; }
}
