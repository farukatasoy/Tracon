using Microsoft.Extensions.AI;

namespace AgentPrism;

/// <summary>Defines AgentPrism run-time options.</summary>
/// <remarks><see cref="AgentPrismOptionsValidator"/> performs validation manually.</remarks>
public sealed class AgentPrismOptions
{
    /// <summary>Gets the default configuration section name.</summary>
    public const string SectionName = "AgentPrism";

    /// <summary>
    /// Gets or sets the tenant identifier used when a request cannot resolve one.
    /// A single-tenant installation always uses this value.
    /// </summary>
    public string DefaultTenantId { get; set; } = "default";

    /// <summary>Gets or sets run-recording options.</summary>
    public AgentPrismRunRecordingOptions RunRecording { get; set; } = new();

    /// <summary>Gets or sets telemetry options.</summary>
    public AgentPrismObservabilityOptions Observability { get; set; } = new();

    /// <summary>Gets or sets model-provider circuit-breaker options.</summary>
    public AgentPrismCircuitBreakerOptions CircuitBreaker { get; set; } = new();

    /// <summary>Gets or sets model-provider health-check options.</summary>
    public AgentPrismHealthOptions Health { get; set; } = new();

    /// <summary>Gets or sets audit-trail options.</summary>
    public AgentPrismAuditOptions Audit { get; set; } = new();

    /// <summary>Gets or sets skill-loading limits.</summary>
    public AgentPrismSkillOptions Skills { get; set; } = new();

    /// <summary>Gets or sets limits for agent-to-agent calls.</summary>
    public AgentPrismAgentGraphOptions AgentGraph { get; set; } = new();

    /// <summary>Gets or sets attachment upload limits for images, audio, and documents.</summary>
    public AgentPrismAttachmentOptions Attachments { get; set; } = new();

    /// <summary>Gets or sets the run-cost price source.</summary>
    public AgentPrismPricingOptions Pricing { get; set; } = new();

    /// <summary>
    /// Gets or sets the default model for context-compaction summarization.
    /// </summary>
    /// <remarks>
    /// This value applies when an agent definition does not provide its own
    /// <c>CompactionSettings.SummarizationModel</c>. When this is also empty,
    /// the agent's own model performs summarization. This avoids using an
    /// expensive model for a low-cost task such as summarization.
    /// </remarks>
    public ModelBinding? UtilityModel { get; set; }

    /// <summary>Gets or sets definition-validation endpoint options.</summary>
    public AgentPrismValidationOptions Validation { get; set; } = new();

    /// <summary>Gets or sets the pre-flight context-window check options.</summary>
    public AgentPrismPreflightOptions Preflight { get; set; } = new();

    /// <summary>Gets or sets the per-provider outgoing concurrency limit.</summary>
    public AgentPrismModelConcurrencyOptions ModelConcurrency { get; set; } = new();

    /// <summary>Gets or sets tool execution options.</summary>
    public AgentPrismToolOptions Tools { get; set; } = new();

    /// <summary>
    /// Gets or sets the largest byte count (UTF-8) for one <c>AgentParameter</c> value.
    /// </summary>
    /// <remarks>
    /// A single limit for every parameter, not one per parameter: a per-parameter
    /// limit would have to live in <see cref="AgentParameter"/> itself and grow the
    /// schema for a concern that is really about the request, not the definition.
    /// Applies to a value from <c>AgentRunRequest.Parameters</c> and to an eval
    /// case's own <c>EvalCase.Parameters</c> alike - both feed the same binder.
    /// </remarks>
    public int MaxParameterValueLength { get; set; } = 4 * 1024;
}

/// <summary>Defines tool execution options.</summary>
public sealed class AgentPrismToolOptions
{
    /// <summary>
    /// Gets or sets the longest duration one tool call may run when its own
    /// registration does not set <see cref="ToolDescriptor.Timeout"/>.
    /// </summary>
    /// <remarks>
    /// 30 seconds matches the skill-script default
    /// (<see cref="AgentPrismSkillScriptOptions.Timeout"/>); this value was
    /// not measured against production traffic and should be revisited after
    /// the first real run.
    /// </remarks>
    public TimeSpan DefaultTimeout { get; set; } = TimeSpan.FromSeconds(30);
}

/// <summary>Defines options for the <c>POST /api/agents/validate</c> endpoint.</summary>
public sealed class AgentPrismValidationOptions
{
    /// <summary>
    /// Gets or sets the maximum duration for fetching a fresh tool list from an MCP server.
    /// </summary>
    /// <remarks>
    /// This is triggered only for a missing tool name when <c>AgentPrism.Mcp</c>
    /// is registered. A slow MCP server must not overrun the validation endpoint.
    /// On timeout, the result is <c>Inconclusive</c>, not <c>Valid</c>.
    /// </remarks>
    public TimeSpan McpTimeout { get; set; } = TimeSpan.FromSeconds(5);
}

/// <summary>
/// Defines limits that apply when an agent calls another agent.
/// </summary>
/// <remarks>
/// <para>
/// Child-agent calls <strong>multiply</strong> cost because every layer makes its
/// own model calls. Without limits, one incorrectly written definition can start
/// dozens of runs for one request. Depth, token, and count limits therefore have defaults.
/// </para>
/// <para>
/// Limits apply <em>per tree</em>: the root run creates an
/// <see cref="AgentRunBudget"/>, and every run in the tree shares it.
/// </para>
/// </remarks>
public sealed class AgentPrismAgentGraphOptions
{
    /// <summary>
    /// Gets or sets the largest allowed call depth. The root run has depth zero,
    /// so the default allows a three-layer tree.
    /// </summary>
    public int MaxDepth { get; set; } = 3;

    /// <summary>
    /// Gets or sets the largest token count that a tree can spend. Zero or a
    /// negative value removes the limit.
    /// </summary>
    /// <remarks>
    /// The default intentionally <em>exists</em>. An unlimited installation learns
    /// about its first invalid definition from the bill.
    /// </remarks>
    public long MaxTotalTokens { get; set; } = 200_000;

    /// <summary>
    /// Gets or sets the largest number of <em>child</em> runs a tree can start.
    /// The root run does not count. Zero or a negative value removes the limit.
    /// </summary>
    public int MaxTotalRuns { get; set; } = 25;

    /// <summary>Creates a tree budget from these options.</summary>
    /// <returns>The budget that the root run shares through its tree.</returns>
    public AgentRunBudget CreateBudget()
        => new()
        {
            MaxDepth = Math.Max(MaxDepth, 0),
            MaxTotalTokens = MaxTotalTokens > 0 ? MaxTotalTokens : null,
            MaxTotalRuns = MaxTotalRuns > 0 ? MaxTotalRuns : null,
        };
}

/// <summary>Defines limits for skill content and agent attachment.</summary>
public sealed class AgentPrismSkillOptions
{
    /// <summary>Gets or sets the largest skill count that can attach to one agent.</summary>
    public int MaxSkillsPerAgent { get; set; } = 10;

    /// <summary>Gets or sets the largest byte count for Markdown instructions.</summary>
    public int MaxInstructionsLength { get; set; } = 64 * 1024;

    /// <summary>Gets or sets the largest byte count for one resource's content.</summary>
    public int MaxResourceContentLength { get; set; } = 256 * 1024;

    /// <summary>Gets or sets the largest resource count that a skill can carry.</summary>
    public int MaxResourcesPerSkill { get; set; } = 20;

    /// <summary>Gets or sets script-execution options. They are disabled by default.</summary>
    public AgentPrismSkillScriptOptions Scripts { get; set; } = new();
}

/// <summary>
/// Defines options that control server-side skill script execution.
/// </summary>
/// <remarks>
/// <para>
/// <strong>AgentPrism does not provide operating-system isolation.</strong>
/// .NET cannot portably disable network access, isolate the file system, apply
/// CPU or memory quotas, or drop privileges. The host environment must provide
/// this. Run AgentPrism with script execution enabled <em>in a container, under
/// an unprivileged user, and with restricted network access</em>.
/// </para>
/// <para>
/// <see cref="PlatformIsolationAcknowledged"/> is the explicit acknowledgment
/// that this limit was read. <see cref="Enabled"/> cannot be enabled without it,
/// and the application fails during startup.
/// </para>
/// </remarks>
public sealed class AgentPrismSkillScriptOptions
{
    /// <summary>Gets or sets whether script execution is enabled. Defaults to <see langword="false"/>.</summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Gets or sets whether the consumer acknowledges that AgentPrism does not
    /// provide operating-system isolation.
    /// </summary>
    public bool PlatformIsolationAcknowledged { get; set; }

    /// <summary>
    /// Gets or sets whether scripts stored in the database can run. Defaults to
    /// <see langword="false"/>.
    /// </summary>
    /// <remarks>
    /// When enabled, an Admin with UI access can write code that runs on the
    /// server. Three gates are required: this flag, the Admin role, and a grant record.
    /// </remarks>
    public bool AllowStoredScripts { get; set; }

    /// <summary>
    /// Gets the roots used to search skill directories on disk.
    /// </summary>
    /// <remarks>
    /// Roots are supplied <strong>in code or configuration</strong> and cannot be
    /// changed through the UI. The person who writes script content deploys the application.
    /// </remarks>
    public IList<string> SkillRoots { get; } = [];

    /// <summary>
    /// Gets the extension-to-interpreter-path allow list. Example:
    /// <c>["py"] = "/usr/bin/python3"</c>.
    /// </summary>
    /// <remarks>
    /// No script runs while the list is empty. Extensions are written without a
    /// leading dot and in lowercase.
    /// </remarks>
    public IDictionary<string, string> Interpreters { get; }
        = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets the allow list of environment variables passed to the script process.
    /// </summary>
    /// <remarks>
    /// Variables outside the list are not passed. A connection string and API key
    /// therefore never reach the process.
    /// </remarks>
    public IList<string> EnvironmentAllowList { get; } = ["PATH", "HOME"];

    /// <summary>Gets or sets the longest duration for one script to run.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Gets or sets the largest combined stdout and stderr byte count. Excess output is truncated.</summary>
    public int MaxOutputBytes { get; set; } = 256 * 1024;

    /// <summary>Gets or sets the largest byte count for JSON arguments supplied to a script.</summary>
    public int MaxArgumentBytes { get; set; } = 16 * 1024;

    /// <summary>Gets or sets the largest byte count for script content stored in the database.</summary>
    public int MaxScriptContentLength { get; set; } = 64 * 1024;

    /// <summary>Gets or sets the largest script count that a skill can carry.</summary>
    public int MaxScriptsPerSkill { get; set; } = 10;

    /// <summary>Gets or sets the largest concurrent script count per tenant.</summary>
    public int MaxConcurrentPerTenant { get; set; } = 2;

    /// <summary>Gets or sets the largest total concurrent script count.</summary>
    public int MaxConcurrentTotal { get; set; } = 8;

    /// <summary>Gets or sets the largest directory depth traversed under skill roots.</summary>
    public int SearchDepth { get; set; } = 2;
}

/// <summary>
/// Defines attachment upload limits for size and media-type allow lists.
/// </summary>
/// <remarks>
/// Binary content lives in <c>attachments</c> and messages carry only a reference.
/// These options apply only during upload. Type validation uses magic bytes, not
/// the client-supplied <c>Content-Type</c>; see <see cref="AttachmentTypeGuard"/>.
/// </remarks>
public sealed class AgentPrismAttachmentOptions
{
    /// <summary>Gets or sets the largest byte count for one attachment. Defaults to 20 MB.</summary>
    public long MaxBytes { get; set; } = 20 * 1024 * 1024;

    /// <summary>
    /// Gets allowed MIME types. A subtype wildcard such as <c>"audio/*"</c> is
    /// accepted. Executable content types are deliberately excluded.
    /// </summary>
    public ISet<string> AllowedMediaTypes { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "image/png",
        "image/jpeg",
        "image/webp",
        "image/gif",
        "application/pdf",
        "text/plain",
        "audio/*",
    };
}

/// <summary>Defines options for audit-trail actor resolution.</summary>
public sealed class AgentPrismAuditOptions
{
    /// <summary>
    /// Gets or sets the claim type used to read the actor. When <see langword="null"/>,
    /// the default order is used: <c>ClaimTypes.NameIdentifier</c> →
    /// <c>ClaimTypes.Name</c> → <c>sub</c>.
    /// </summary>
    public string? ActorClaimType { get; set; }
}

/// <summary>
/// Defines circuit-breaker options that temporarily stop requests after a model
/// provider returns consecutive failures.
/// </summary>
/// <remarks>
/// The circuit breaker decorates the <see cref="IChatClient"/> pipeline instead
/// of being embedded in a provider implementation. Every provider, including
/// OpenAI, compatible servers, and future Anthropic or Gemini providers, gets
/// the same protection.
/// </remarks>
public sealed class AgentPrismCircuitBreakerOptions
{
    /// <summary>
    /// Gets or sets whether the circuit breaker is enabled. When disabled,
    /// requests always reach the provider and no consecutive-failure counter is kept.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the consecutive failure count required to open the circuit.
    /// </summary>
    public int FailureThreshold { get; set; } = 5;

    /// <summary>
    /// Gets or sets the duration to wait after opening the circuit before one
    /// half-open retry.
    /// </summary>
    public TimeSpan BreakDuration { get; set; } = TimeSpan.FromSeconds(30);
}

/// <summary>Defines model-provider health-check options.</summary>
/// <remarks>
/// The check calls <c>GET {endpoint}/models</c> and does not incur cost. Results
/// are cached, so <c>/api/models/health</c> does not call a provider on every request.
/// </remarks>
public sealed class AgentPrismHealthOptions
{
    /// <summary>Gets or sets how long a health-check result remains fresh in cache.</summary>
    public TimeSpan CacheTtl { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Gets or sets the automatic background health-check interval. When
    /// <see langword="null"/>, the default, no background timer runs. The check
    /// is triggered only from the UI's "check now" action or <c>/api/models/health</c>.
    /// </summary>
    /// <remarks>
    /// This is intentionally empty because regular requests from an idle
    /// installation are an undesirable default.
    /// </remarks>
    public TimeSpan? BackgroundInterval { get; set; }
}

/// <summary>
/// Defines telemetry collection and persistence options.
/// </summary>
/// <remarks>
/// AgentPrism <strong>does not take over the pipeline</strong>: the consumer's
/// OTLP exporter continues to run. These options only control what AgentPrism
/// writes to its <em>own</em> span store.
/// </remarks>
public sealed class AgentPrismObservabilityOptions
{
    /// <summary>
    /// Gets or sets whether span and metric creation is enabled. When disabled,
    /// no <c>Activity</c> starts and no measurement is recorded.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets whether spans are written to <see cref="ITraceStore"/>. When
    /// disabled, spans are still created and sent to the consumer exporter, but
    /// are not written to AgentPrism's own store.
    /// </summary>
    public bool PersistSpans { get; set; } = true;

    /// <summary>
    /// Gets or sets the fraction from 0 to 1 of successful run spans to persist.
    /// Defaults to 0.1, or one tenth.
    /// </summary>
    /// <remarks>
    /// Persisting every span bottlenecks the database at high volume. Failed runs
    /// are valuable for debugging and are separately protected by
    /// <see cref="AlwaysPersistFailures"/>.
    /// </remarks>
    public double SuccessSampleRatio { get; set; } = 0.1;

    /// <summary>
    /// Gets or sets whether failed run spans are persisted regardless of sampling ratio.
    /// </summary>
    public bool AlwaysPersistFailures { get; set; } = true;

    /// <summary>
    /// Gets or sets the largest span count held in memory for one run. Excess
    /// spans are dropped and a warning is logged.
    /// </summary>
    /// <remarks>
    /// The sampling decision is made when a run <em>finishes</em>, when success
    /// or failure is known. Spans remain in memory until then. This limit bounds
    /// memory use by the concurrent run count.
    /// </remarks>
    public int MaxSpansPerRun { get; set; } = 200;

    /// <summary>
    /// Gets or sets whether request and response text is written to spans.
    /// <strong>Disabled by default</strong> because this content can contain personal data.
    /// </summary>
    public bool RecordSensitiveData { get; set; }

    /// <summary>
    /// Gets or sets whether the <c>agentprism.agent.version</c> tag is added to
    /// spans and the <c>agentprism.runs</c> and <c>agentprism.run.duration</c> metrics.
    /// </summary>
    /// <remarks>
    /// Defaults to <see langword="true"/>. A version number creates dozens of time
    /// series per agent over time, which is acceptable cardinality. Deployments
    /// that change versions very frequently can disable it.
    /// </remarks>
    public bool IncludeAgentVersionTag { get; set; } = true;

    /// <summary>
    /// Gets or sets whether the <c>agentprism.quota.usage</c> and
    /// <c>agentprism.quota.limit</c> observable gauges are enabled.
    /// </summary>
    /// <remarks>
    /// <strong>Disabled by default</strong> under the no-surprises rule because the gauge reads the
    /// database. Unlike the cost counter, this consumes additional resources and
    /// must be explicitly requested.
    /// </remarks>
    public bool EnableQuotaUsageGauge { get; set; }

    /// <summary>
    /// Gets or sets the quota-gauge cache refresh interval. Consecutive polls do
    /// not reach the database before this interval elapses.
    /// </summary>
    public TimeSpan QuotaUsageRefreshInterval { get; set; } = TimeSpan.FromSeconds(30);
}

/// <summary>Defines how much detail run recording retains.</summary>
public sealed class AgentPrismRunRecordingOptions
{
    /// <summary>Gets or sets whether run recording is enabled. When disabled, no event is written.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets whether every text chunk in streaming runs is written as a
    /// separate event. When disabled, only completed messages are recorded and
    /// event volume decreases substantially.
    /// </summary>
    public bool RecordMessageDeltas { get; set; } = true;

    /// <summary>Gets or sets whether tool arguments and results are recorded.</summary>
    /// <remarks>
    /// Tool arguments can contain personal data. A data-retention policy can
    /// disable this flag.
    /// </remarks>
    public bool RecordToolPayloads { get; set; } = true;

    /// <summary>
    /// Gets or sets the upper character limit for one event payload. Excess
    /// payloads are truncated and receive a trailing marker.
    /// </summary>
    public int MaxPayloadLength { get; set; } = 8 * 1024;

    /// <summary>
    /// Gets or sets whether run input is written to <c>run_inputs</c>.
    /// Replay does not work when disabled.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This defaults to <strong>enabled</strong> and is not a "zero
    /// surprise" exception. Input does not introduce a new data class: the same
    /// messages already exist in <c>conversation_items</c> for a session run,
    /// and <see cref="RecordToolPayloads"/> currently defaults to
    /// <see langword="true"/>. If disabled, replay would be dead for every
    /// existing run.
    /// </para>
    /// <para>
    /// Input is not truncated. <see cref="MaxPayloadLength"/> applies to event
    /// payloads; a truncated input would silently produce incorrect replay.
    /// Growth is bounded by the <c>run_inputs</c> retention target.
    /// </para>
    /// </remarks>
    public bool RecordRunInput { get; set; } = true;

    /// <summary>
    /// Gets or sets whether a reasoning (thinking) delta from the model is written as a
    /// <see cref="RunEventType.ReasoningDelta"/> event.
    /// </summary>
    /// <remarks>
    /// Defaults to <see langword="false"/>, unlike <see cref="RecordMessageDeltas"/>, for
    /// three reasons: reasoning output can run far longer than the answer and its recorded
    /// volume was not measured; intermediate reasoning can repeat user data in forms the
    /// final answer never shows; and some providers restrict storing raw reasoning text,
    /// a restriction that has not been verified. Turning it on is a one-line change and
    /// does not conflict with the retention policy — reasoning shares
    /// <see cref="RecordMessageDeltas"/>'s retention bucket, it is not tracked separately.
    /// </remarks>
    public bool RecordReasoningDeltas { get; set; }
}

/// <summary>
/// Defines a run-cost price source. It is a secondary source for a model that
/// has no price in the <see cref="ModelDescriptor"/> catalog.
/// </summary>
/// <remarks>
/// AgentPrism does <strong>not invent prices</strong>. This stores only
/// values supplied by the consumer in configuration. Configuration paths:
/// <list type="bullet">
///   <item><c>AgentPrism:Pricing:Currency</c></item>
///   <item><c>AgentPrism:Pricing:{provider}:{model}:Input|Output</c></item>
///   <item><c>AgentPrism:Pricing:Voice:{provider}:{model}:PerMillionCharacters|PerMinute</c></item>
///   <item><c>AgentPrism:Pricing:Images:{provider}:{model}:PerImage|OutputCostPerMillionTokens</c></item>
/// </list>
/// Wildcards are not supported.
/// <para>
/// The section is bound manually for AOT. Every child of <c>Pricing</c> is
/// treated as a provider name, so the <c>Currency</c> and <c>Voice</c> keys are
/// reserved and cannot be provider names. When adding a reserved key, also update
/// the skip list in <c>BindPricing</c>.
/// </para>
/// </remarks>
public sealed class AgentPrismPricingOptions
{
    /// <summary>Gets or sets the currency label displayed in reports. No conversion occurs.</summary>
    public string? Currency { get; set; }

    /// <summary>
    /// Gets price overrides per model, keyed by provider name.
    /// </summary>
    public IDictionary<string, IDictionary<string, ModelPriceOverride>> Providers { get; }
        = new Dictionary<string, IDictionary<string, ModelPriceOverride>>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets voice prices per model, keyed by voice provider name.
    /// </summary>
    /// <remarks>
    /// Voice pricing uses characters or duration rather than tokens, so it cannot
    /// share a dictionary with <see cref="Providers"/>. The two sections are not
    /// combined because their units differ;
    /// </remarks>
    public IDictionary<string, IDictionary<string, VoicePriceOverride>> Voice { get; }
        = new Dictionary<string, IDictionary<string, VoicePriceOverride>>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Gets image-generation prices per model, keyed by provider name.</summary>
    /// <remarks>
    /// A model is priced either per generated image or per output token. The two
    /// modes are deliberately separate from <see cref="Providers"/>, which prices
    /// chat-model input and output tokens.
    /// </remarks>
    public IDictionary<string, IDictionary<string, ImagePriceOverride>> Images { get; }
        = new Dictionary<string, IDictionary<string, ImagePriceOverride>>(StringComparer.OrdinalIgnoreCase);
}

/// <summary>Defines the configured price for one voice model.</summary>
/// <remarks>
/// The fields are mutually exclusive. A model either produces speech from text,
/// billed by character, or transcribes speech to text, billed by duration. When
/// both are set, a tool selects the field matching its own unit.
/// </remarks>
public sealed class VoicePriceOverride
{
    /// <summary>Gets or sets cost per million characters for text-to-speech.</summary>
    public decimal? PerMillionCharacters { get; set; }

    /// <summary>Gets or sets cost per minute for speech-to-text.</summary>
    public decimal? PerMinute { get; set; }
}

/// <summary>Defines the configured price for one image-generation model.</summary>
public sealed class ImagePriceOverride
{
    /// <summary>Gets or sets the cost per generated image.</summary>
    public decimal? PerImage { get; set; }

    /// <summary>Gets image-size multipliers, keyed by the provider's size string.</summary>
    /// <remarks>
    /// Keys are not normalized. A provider can add a size without a library release;
    /// retaining the provider string avoids a stale built-in image-size catalogue.
    /// </remarks>
    public IDictionary<string, decimal> SizeMultipliers { get; }
        = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Gets or sets the cost per million output tokens.</summary>
    public decimal? OutputCostPerMillionTokens { get; set; }
}

/// <summary>Defines a configured price override for one model.</summary>
public sealed class ModelPriceOverride
{
    /// <summary>Gets or sets cost per million input tokens.</summary>
    public decimal? InputCostPerMillionTokens { get; set; }

    /// <summary>Gets or sets cost per million output tokens.</summary>
    public decimal? OutputCostPerMillionTokens { get; set; }

    /// <summary>
    /// Gets or sets cost per million input tokens served from the prompt cache.
    /// </summary>
    /// <remarks>
    /// Leaving it unset prices cached tokens at
    /// <see cref="InputCostPerMillionTokens"/>; it never makes the price
    /// unknown. See <see cref="ModelDescriptor.CachedInputCostPerMillionTokens"/>.
    /// </remarks>
    public decimal? CachedInputCostPerMillionTokens { get; set; }
}
