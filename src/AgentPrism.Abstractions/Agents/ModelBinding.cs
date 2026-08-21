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
    /// is the same as for <see cref="ReasoningEffort"/>: a setting that is
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

    /// <summary>
    /// Gets the ordered fallback chain tried when the primary provider is
    /// unavailable. Empty by default.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Empty by default: with no fallback configured, today's behavior is
    /// preserved exactly — a provider failure (for example an open circuit)
    /// still throws, and no fallback code path runs.
    /// </para>
    /// <para>
    /// A fallback link carries only a provider and a model, not a full
    /// <see cref="ModelBinding"/>: <see cref="Temperature"/>,
    /// <see cref="ProviderSettings"/>, and the other fields do not carry over
    /// to the fallback call. A fallback model that needs its own settings is a
    /// separate configuration concern, not something this chain expresses.
    /// </para>
    /// <para>
    /// Switching to a fallback is never silent: the run record gets an
    /// explicit <c>ModelFallbackUsed</c> event, and cost and model attribution
    /// (<c>RunStatistics.ByModel</c>) reflect the model that actually answered,
    /// not the primary binding.
    /// </para>
    /// </remarks>
    public IReadOnlyList<ModelFallback> Fallbacks { get; init; } = [];

    /// <summary>
    /// Gets the response caching settings. Disabled when <see langword="null"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Disabled by default: with no cache configured, today's behavior is
    /// preserved exactly — every call reaches the model, and no cache ring is
    /// added to the pipeline.
    /// </para>
    /// <para>
    /// Enabling it while no <c>IDistributedCache</c> is registered is not
    /// silently ignored: compilation stops with
    /// <c>AgentPrismCompilationException</c>, naming the missing registration.
    /// </para>
    /// </remarks>
    public ResponseCacheSettings? ResponseCache { get; init; }

    /// <summary>
    /// Gets whether independent tool calls within one turn may run at the
    /// same time.
    /// </summary>
    /// <remarks>
    /// <see langword="false"/> by default: with no change, tool calls within a
    /// turn run one after another exactly as they do today. A tool body that
    /// is not written to be thread-safe (a non-thread-safe field it shares
    /// across calls, for example) is only safe to run concurrently with
    /// itself once this is turned on for its agent.
    /// </remarks>
    public bool AllowConcurrentToolCalls { get; init; }
}
