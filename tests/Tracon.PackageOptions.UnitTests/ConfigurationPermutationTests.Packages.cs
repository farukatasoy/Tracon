namespace Tracon.Core.UnitTests.Configuration;

/// <summary>
/// The package scope of the probe: what is recorded about the options of the model providers, MCP, voice, workflows
/// and the three SQL stores.
/// </summary>
/// <remarks>
/// Every entry is either a documented meaning of the value or an open finding that waits for a decision. An open finding
/// says so in its reason; the probe fails the day the key starts to refuse the value, so the list only shrinks.
/// </remarks>
public sealed partial class ConfigurationPermutationTests
{
    /// <summary>Scalars whose text is NOT read from configuration on purpose, each with the reason. None in this scope.</summary>
    private static readonly Dictionary<string, string> NotBoundOnPurpose = new(StringComparer.Ordinal);

    /// <summary>Limits that accept <c>0</c> because it has a meaning, each with that meaning.</summary>
    private static readonly Dictionary<string, string> ZeroIsDocumented = new(StringComparer.Ordinal)
    {
        ["Tracon:Providers:Anthropic:MaxRetries"] = "no retry: the SDK makes one attempt (documented in the remarks)",
        ["Tracon:Mcp:StaleToolRetention"] = "keeps no stale tool list; only a negative value is refused (compared with an age, it arms no timer)",
        ["Tracon:Mcp:MaxResourcesPerServer"] = "documented: a value below one keeps no resource",
        ["Tracon:Mcp:MaxPromptsPerServer"] = "documented: a value below one keeps no prompt",
        ["Tracon:Workflows:MaxConcurrentRuns"] = "clamped to one where it is read (the runner builds its semaphore from the larger of the value and one)",
        ["Tracon:Workflows:MaxResumeAttempts"] = "documented: zero turns continuation off and keeps the detection of an interrupted run",
        ["Tracon:PostgreSql:CommandTimeoutSeconds"] = "documented: 0 means unlimited",
        ["Tracon:SqlServer:CommandTimeoutSeconds"] = "documented: 0 means unlimited",
        ["Tracon:Sqlite:CommandTimeoutSeconds"] = "documented: 0 means unlimited",
    };

    /// <summary>Limits that accept <c>-1</c> because it has a meaning, each with that meaning.</summary>
    private static readonly Dictionary<string, string> NegativeIsDocumented = new(StringComparer.Ordinal)
    {
        ["Tracon:Mcp:MaxResourcesPerServer"] = "documented: a value below one keeps no resource",
        ["Tracon:Mcp:MaxPromptsPerServer"] = "documented: a value below one keeps no prompt",
        ["Tracon:Workflows:MaxConcurrentRuns"] = "clamped to one where it is read (the runner builds its semaphore from the larger of the value and one)",
    };

    /// <summary>
    /// Scalars read with the framework's own binder instead of Tracon's reader. An unreadable text is a raw
    /// <see cref="InvalidOperationException"/> out of the first read of the options (the host still stops, but not with an
    /// options failure that lists every key), and a time span written without a clock is read as days.
    /// </summary>
    private static readonly Dictionary<string, string> ReadByFrameworkBinder = new(StringComparer.Ordinal)
    {
        ["Tracon:Workflows:MaxConcurrentRunsPerTenant"] = "open finding: 'abc' is a raw InvalidOperationException, not the reader's failure naming every key",
        ["Tracon:Workflows:MaxRunTimeout"] = "open finding: '30' and '24:00:00' bind as 30 and 24 DAYS; every other duration option refuses them",
    };

    /// <summary>
    /// Text options that keep a whitespace-only value as written. Each is an open finding that waits for the decision the
    /// core options already have: the provider packages cannot see the core's helper, so the same rule needs one in the
    /// shared provider sources.
    /// </summary>
    private static readonly Dictionary<string, string> WhitespaceTextKept = new(StringComparer.Ordinal)
    {
        ["Tracon:Providers:Anthropic:DefaultModel"] = "open finding: a model called blanks, found at the first call",
        ["Tracon:Providers:Google:DefaultModel"] = "open finding: a model called blanks, found at the first call",
        ["Tracon:Providers:Google:ApiVersion"] = "open finding: an API version of blanks goes into the request path",
        ["Tracon:Providers:OpenAI:DefaultModel"] = "open finding: a model called blanks, found at the first call",
        ["Tracon:Providers:OpenAI:Organization"] = "open finding: an organization header of blanks",
        ["Tracon:Providers:OpenAI:Live:Voice"] = "open finding: a voice called blanks, found at the first session",
        ["Tracon:Providers:AzureOpenAI:DefaultDeployment"] = "open finding: a deployment called blanks, found at the first call",
        ["Tracon:Providers:AzureOpenAI:Audience"] = "open finding: a token audience of blanks, found at the first token request",
    };

    /// <summary>The fractions this scope holds to a range of 0 to 1. None in this scope.</summary>
    private static readonly string[] RatioKeys = [];

    /// <summary>The fewest number scalars the scan of this scope has to find. This scope has no fraction.</summary>
    private const int MinimumNumberScalars = 0;

    /// <summary>The fewest text scalars the whitespace scan has to probe.</summary>
    private const int MinimumTextScalars = 8;
}
