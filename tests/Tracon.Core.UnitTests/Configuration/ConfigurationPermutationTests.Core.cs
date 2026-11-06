namespace Tracon.Core.UnitTests.Configuration;

/// <summary>
/// The core scope of the probe: what is recorded about the options of <c>Tracon.Core</c> and <c>Tracon.Abstractions</c>.
/// </summary>
public sealed partial class ConfigurationPermutationTests
{
    /// <summary>
    /// Scalars whose text is NOT read from configuration on purpose, each with the reason. The probe skips them.
    /// </summary>
    private static readonly Dictionary<string, string> NotBoundOnPurpose = new(StringComparer.Ordinal)
    {
        ["Tracon:Skills:Scripts:AllowStoredScripts"] = "a security boundary: configuration must not widen the executable surface",
    };

    /// <summary>
    /// Limits that accept <c>0</c> because it has a meaning, each with that meaning. A limit that is not here and
    /// accepts <c>0</c> stops the build: the operator who wrote it may have meant something else.
    /// </summary>
    private static readonly Dictionary<string, string> ZeroIsDocumented = new(StringComparer.Ordinal)
    {
        ["Tracon:AgentGraph:MaxDepth"] = "no sub-agent call is allowed",
        ["Tracon:AgentGraph:MaxDuration"] = "documented: zero removes the limit",
        ["Tracon:AgentGraph:MaxTotalCost"] = "documented: zero removes the limit",
        ["Tracon:AgentGraph:MaxTotalRuns"] = "documented: zero removes the limit",
        ["Tracon:AgentGraph:MaxTotalTokens"] = "documented: zero removes the limit",
        ["Tracon:Attachments:MaxBytes"] = "every attachment is over the limit (fail-closed)",
        ["Tracon:Idempotency:ReservationTimeout"] = "documented: zero never takes a reserved key over",
        ["Tracon:Knowledge:ChunkOverlap"] = "no overlap between chunks",
        ["Tracon:Observability:MaxJobLaneCardinality"] = "clamped to one series where it is read",
        ["Tracon:Observability:MaxSpansPerRun"] = "every span is dropped and a warning is logged (fail-closed, diagnostics only)",
        ["Tracon:Observability:SuccessSampleRatio"] = "persists no successful run",
        ["Tracon:OnlineEvaluation:LowScoreThreshold"] = "marks no score as low",
        ["Tracon:OnlineEvaluation:MaxScoresPerHour"] = "scores nothing (the cost cap is zero)",
        ["Tracon:OnlineEvaluation:SampleRate"] = "samples nothing",
        ["Tracon:Preflight:ReserveRatio"] = "reserves nothing for the answer",
        ["Tracon:RateLimit:PermitLimit"] = "documented: a value that is not positive keeps the one-permit meaning",
        ["Tracon:RateLimit:QueueLimit"] = "no waiting line",
        ["Tracon:RateLimit:Window"] = "documented: a value that is not positive keeps the one-minute meaning",
        ["Tracon:Retention:BatchDelay"] = "no pause between batches",
        ["Tracon:RunRecording:MaxPayloadLength"] = "zero is inside the validated range 0 to 1048576",
        ["Tracon:RunRecording:SinkTimeout"] = "documented: zero waits for the sink for ever",
        ["Tracon:Voice:Conversation:MaxConcurrentConnectionsPerTenant"] = "refuses every connection (fail-closed)",
        ["Tracon:Voice:Conversation:MaxSpokenCharactersPerTurn"] = "speaks nothing; the answer still streams as captions",
        ["Tracon:Voice:Conversation:MaxUtteranceBytes"] = "refuses every utterance (fail-closed)",
        ["Tracon:Voice:Live:MaxAppendsPerDelegation"] = "a delegation sends no append",
        ["Tracon:Voice:Live:MaxConcurrentDelegations"] = "refuses every delegation (fail-closed)",
        ["Tracon:Voice:Live:MaxConcurrentSessionsPerTenant"] = "refuses every session (fail-closed)",
        ["Tracon:Voice:Live:MaxLedgerEntriesPerDelegation"] = "a delegation carries no transcript",
        ["Tracon:Voice:Live:MaxTranscriptLedgerCharacters"] = "the ledger keeps nothing",
        ["Tracon:Webhooks:MaxExtraHeaders"] = "no extra header is allowed",
        ["Tracon:Webhooks:SignatureTolerance"] = "the recommended tolerance is none",
    };

    /// <summary>
    /// Limits that accept <c>-1</c> because it has a meaning, each with that meaning. A limit that is not here and
    /// accepts <c>-1</c> stops the build.
    /// </summary>
    private static readonly Dictionary<string, string> NegativeIsDocumented = new(StringComparer.Ordinal)
    {
        ["Tracon:AgentGraph:MaxDepth"] = "no sub-agent call is allowed (fail-closed)",
        ["Tracon:AgentGraph:MaxDuration"] = "documented: a negative value removes the limit",
        ["Tracon:AgentGraph:MaxTotalCost"] = "documented: a negative value removes the limit",
        ["Tracon:AgentGraph:MaxTotalRuns"] = "documented: a negative value removes the limit",
        ["Tracon:AgentGraph:MaxTotalTokens"] = "documented: a negative value removes the limit",
        ["Tracon:Attachments:MaxBytes"] = "every attachment is over the limit (fail-closed)",
        ["Tracon:Idempotency:ReservationTimeout"] = "documented: zero or less never takes a reserved key over",
        ["Tracon:Observability:MaxJobLaneCardinality"] = "clamped to one series where it is read",
        ["Tracon:Observability:MaxSpansPerRun"] = "every span is dropped and a warning is logged (fail-closed, diagnostics only)",
        ["Tracon:RateLimit:PermitLimit"] = "documented: a value that is not positive keeps the one-permit meaning",
        ["Tracon:RateLimit:QueueLimit"] = "clamped to zero where it is read",
        ["Tracon:RateLimit:Window"] = "documented: a value that is not positive keeps the one-minute meaning",
    };

    /// <summary>
    /// Text options that keep a whitespace-only value as written, where a number option treats it as not given. The
    /// behaviour is recorded here until it is decided; a key that starts to refuse the text stops the build, so the list
    /// only shrinks. None in this scope: every text option of the core sections refuses blanks.
    /// </summary>
    private static readonly Dictionary<string, string> WhitespaceTextKept = new(StringComparer.Ordinal);

    /// <summary>The fractions this scope holds to a range of 0 to 1.</summary>
    private static readonly string[] RatioKeys =
    [
        "Tracon:Observability:SuccessSampleRatio",
        "Tracon:Preflight:ReserveRatio",
        "Tracon:OnlineEvaluation:SampleRate",
    ];

    /// <summary>The fewest number scalars the scan of this scope has to find, so that a scan that found none does not pass.</summary>
    private const int MinimumNumberScalars = 4;

    /// <summary>The fewest text scalars the whitespace scan has to probe.</summary>
    private const int MinimumTextScalars = 16;
}
