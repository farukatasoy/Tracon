using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace Tracon;

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
    /// is built, with <c>TraconCompilationException</c>; it is not ignored silently.
    /// </remarks>
    public string? ReasoningEffort { get; init; }

    /// <summary>
    /// Gets the provider-specific extra settings. A key has the form <c>{provider}.{setting}</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The prompt caching of Anthropic or the safety thresholds of Gemini do not fit the
    /// fixed fields of this contract. Adding them to the body of <see cref="ModelBinding"/>
    /// would leak one vendor's concept into <c>Tracon.Abstractions</c>. This dictionary
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
    public IReadOnlyDictionary<string, JsonElement> ProviderSettings
    {
        get => field ?? new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        init;
    }

    /// <summary>
    /// Gets the requested output format. When it is <see langword="null"/> today's
    /// behaviour does not change: no format constraint is sent to the provider.
    /// </summary>
    /// <remarks>
    /// <see cref="AgentResponseFormatKind.Text"/> differs from <see langword="null"/>:
    /// <see langword="null"/> means "say nothing", <c>Text</c> means "ask for plain text
    /// explicitly". An invalid combination (for example
    /// <see cref="AgentResponseFormatKind.JsonSchema"/> without a schema) is rejected
    /// while the agent is built, with <c>TraconCompilationException</c>.
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
    /// <see cref="ModelBinding"/>. The fallback call receives the primary call's
    /// request options — <see cref="Temperature"/>, <see cref="TopP"/>,
    /// <see cref="MaxOutputTokens"/>, <see cref="ResponseFormat"/>, the other sampling
    /// fields (<see cref="Seed"/>, <see cref="TopK"/>, <see cref="StopSequences"/>, the penalties,
    /// <see cref="ToolMode"/>, <see cref="AllowMultipleToolCalls"/>, <see cref="ReasoningOutput"/>)
    /// and the tools — with only the model name replaced; the link also takes
    /// <see cref="ToolLoop"/> as one object. <see cref="ProviderSettings"/> do not
    /// carry over: they configure the primary provider's client. A fallback model
    /// that needs its own settings is a separate configuration concern, not something
    /// this chain expresses.
    /// </para>
    /// <para>
    /// Switching to a fallback is never silent: the run record gets an
    /// explicit <c>ModelFallbackUsed</c> event, and cost and model attribution
    /// (<c>RunStatistics.ByModel</c>) reflect the model that actually answered,
    /// not the primary binding.
    /// </para>
    /// <para>
    /// That applies to the agent's own binding. A binding the run calls on the
    /// side, such as the context compaction summarizer or the judge of an
    /// <c>aiJudge</c> loop criterion, keeps its fallback to that call: the run
    /// keeps the agent's model and gets no event, the call's tokens are priced
    /// at the model that answered, and the switch is logged.
    /// </para>
    /// </remarks>
    public IReadOnlyList<ModelFallback> Fallbacks { get => field ?? []; init; }

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
    /// <c>TraconCompilationException</c>, naming the missing registration.
    /// </para>
    /// </remarks>
    public ResponseCacheSettings? ResponseCache { get; init; }

    /// <summary>Gets the sampling seed. The provider default is used when it is <see langword="null"/>.</summary>
    /// <remarks>
    /// Sent only by the API surfaces that support it (see <see cref="ISamplingSupport"/>); a
    /// definition that sets it for a provider that does not is rejected while the agent is
    /// built. The same holds for <see cref="TopK"/>, <see cref="StopSequences"/>,
    /// <see cref="FrequencyPenalty"/>, <see cref="PresencePenalty"/>, <see cref="ToolMode"/>,
    /// <see cref="AllowMultipleToolCalls"/> and <see cref="ReasoningOutput"/>, on the primary
    /// provider and on every <see cref="Fallbacks"/> link.
    /// </remarks>
    public long? Seed { get; init; }

    /// <summary>Gets the top-k sampling limit. The provider default is used when it is <see langword="null"/>.</summary>
    /// <remarks>It must be greater than zero.</remarks>
    public int? TopK { get; init; }

    /// <summary>
    /// Gets the sequences that stop generation. Nothing is sent when it is
    /// <see langword="null"/> or empty.
    /// </summary>
    /// <remarks>
    /// An entry must not be empty. The providers limit how many sequences a request may carry;
    /// the provider's own error is returned when the limit is exceeded.
    /// </remarks>
    public IReadOnlyList<string>? StopSequences { get; init; }

    /// <summary>Gets the frequency penalty. The provider default is used when it is <see langword="null"/>.</summary>
    public float? FrequencyPenalty { get; init; }

    /// <summary>Gets the presence penalty. The provider default is used when it is <see langword="null"/>.</summary>
    public float? PresencePenalty { get; init; }

    /// <summary>
    /// Gets how the model may use the agent's tools. The provider default (the model decides)
    /// is used when it is <see langword="null"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The values are <c>auto</c>, <c>none</c>, <c>required</c> (the model must call a tool)
    /// and <c>required:&lt;tool&gt;</c> (the model must call that tool). The comparison is case
    /// insensitive. The tool in <c>required:&lt;tool&gt;</c> must be one of the definition's
    /// <see cref="AgentDefinition.ToolNames"/>. An unrecognized value is rejected while the
    /// agent is built, with <c>TraconCompilationException</c>.
    /// </para>
    /// <para>
    /// <c>required</c> and <c>required:&lt;tool&gt;</c> force the first model call of a turn
    /// only: the tool-call loop lifts the requirement for the calls that follow, or the model
    /// could never answer.
    /// </para>
    /// </remarks>
    public string? ToolMode { get; init; }

    /// <summary>
    /// Gets whether the model may ask for several tool calls in one turn. The provider default
    /// is used when it is <see langword="null"/>.
    /// </summary>
    /// <remarks>
    /// It is not <see cref="ToolLoopSettings.AllowConcurrentToolCalls"/>: this one controls
    /// what the model may ask for, that one controls whether Tracon runs the calls at the
    /// same time.
    /// </remarks>
    public bool? AllowMultipleToolCalls { get; init; }

    /// <summary>
    /// Gets how much of the model's reasoning the response carries. The provider default is
    /// used when it is <see langword="null"/>.
    /// </summary>
    /// <remarks>
    /// The valid values are the <c>Microsoft.Extensions.AI.ReasoningOutput</c> names:
    /// <c>None</c>, <c>Summary</c>, <c>Full</c>. The comparison is case insensitive. An
    /// unrecognized value is rejected while the agent is built, with
    /// <c>TraconCompilationException</c>, in the same way as <see cref="ReasoningEffort"/>.
    /// </remarks>
    public string? ReasoningOutput { get; init; }

    /// <summary>Gets the settings of the tool-call loop the model pipeline builds.</summary>
    /// <remarks>
    /// <para>
    /// The defaults keep today's behavior: the loop runs tool calls one after another and uses
    /// its own limits. A fallback link takes this object as it is.
    /// </para>
    /// <para>
    /// It is never <see langword="null"/> when read, and it accepts <see langword="null"/> when set:
    /// <see langword="null"/> becomes the default settings, so a client that sends
    /// <c>"toolLoop": null</c> is not refused. A source-generated JSON reader assigns
    /// <see langword="null"/> to a member that is absent from the JSON when the type has
    /// <see langword="required"/> members (measured), which is what a definition stored before this
    /// member existed looks like.
    /// </para>
    /// </remarks>
    [AllowNull]
    public ToolLoopSettings ToolLoop
    {
        get;
        init => field = value ?? new ToolLoopSettings();
    } = new();
}
