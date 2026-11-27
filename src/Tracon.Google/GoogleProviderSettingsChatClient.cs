using Google.GenAI.Types;
using Microsoft.Extensions.AI;

namespace Tracon;

/// <summary>
/// Applies the <c>google.*</c> settings from <see cref="ModelBinding.ProviderSettings"/>
/// to every request.
/// </summary>
/// <remarks>
/// <para>
/// Settings are sent through <c>Microsoft.Extensions.AI</c>'s official escape
/// hatch, <see cref="ChatOptions.RawRepresentationFactory"/>: the factory builds a
/// <see cref="GenerateContentConfig"/> and the Google adapter builds the request on
/// top of this object. Measured: the adapter <strong>preserves</strong>
/// the <c>SafetySettings</c> and <c>ThinkingConfig</c> fields we write and adds tool
/// definitions on top — tool calling and safety thresholds work together in the
/// same request.
/// </para>
/// <para>
/// The caller's <see cref="ChatOptions"/> instance is <strong>not mutated</strong>.
/// A compiled agent shares a single <see cref="ChatOptions"/> instance across all
/// calls; overwriting it would mix up concurrent runs.
/// </para>
/// <para>
/// A thinking level or budget in the settings is the <strong>only</strong> depth control the
/// request carries. The adapter maps <see cref="ReasoningOptions.Effort"/> to a level by itself,
/// and only into a field the request leaves empty, so an effort left in the options would add a
/// level next to a budget, or a budget of zero (the mapping of <c>None</c>) next to a level. The copy
/// of the options therefore drops the effort when the settings give the depth, and the setting wins.
/// </para>
/// </remarks>
internal sealed class GoogleProviderSettingsChatClient : DelegatingChatClient
{
    private readonly IReadOnlyList<SafetySetting> _safetySettings;
    private readonly int? _thinkingBudgetTokens;
    private readonly ThinkingLevel? _thinkingLevel;
    private readonly bool? _includeThoughts;

    public GoogleProviderSettingsChatClient(
        IChatClient inner,
        IReadOnlyList<SafetySetting> safetySettings,
        int? thinkingBudgetTokens,
        ThinkingLevel? thinkingLevel,
        bool? includeThoughts)
        : base(inner)
    {
        _safetySettings = safetySettings;
        _thinkingBudgetTokens = thinkingBudgetTokens;
        _thinkingLevel = thinkingLevel;
        _includeThoughts = includeThoughts;
    }

    /// <summary>Whether the binding has a setting to apply.</summary>
    internal static bool HasSettings(
        IReadOnlyList<SafetySetting> safetySettings,
        int? thinkingBudgetTokens,
        ThinkingLevel? thinkingLevel,
        bool? includeThoughts)
        => safetySettings.Count > 0 || thinkingBudgetTokens is not null || thinkingLevel is not null || includeThoughts is not null;

    private bool SetsThinkingDepth => _thinkingBudgetTokens is not null || _thinkingLevel is not null;

    /// <inheritdoc />
    public override Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
        => base.GetResponseAsync(messages, Apply(options), cancellationToken);

    /// <inheritdoc />
    public override IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
        => base.GetStreamingResponseAsync(messages, Apply(options), cancellationToken);

    private ChatOptions Apply(ChatOptions? options)
    {
        var copy = options?.Clone() ?? new ChatOptions();

        // Left untouched when the caller already gave its own raw representation:
        // this is a path the consumer deliberately takes over.
        if (copy.RawRepresentationFactory is not null)
        {
            return copy;
        }

        copy.RawRepresentationFactory = _ => BuildConfig();

        // The setting that gives the depth of thinking wins over the portable effort. The reasoning options are
        // replaced, not edited: the instance the copy holds may be the one the caller holds.
        if (SetsThinkingDepth && copy.Reasoning is { Effort: not null } reasoning)
        {
            copy.Reasoning = reasoning.Output is { } output ? new ReasoningOptions { Output = output } : null;
        }

        return copy;
    }

    private GenerateContentConfig BuildConfig()
    {
        var config = new GenerateContentConfig();

        if (_safetySettings.Count > 0)
        {
            config.SafetySettings = [.. _safetySettings];
        }

        if (_thinkingBudgetTokens is not null || _thinkingLevel is not null || _includeThoughts is not null)
        {
            config.ThinkingConfig = new ThinkingConfig
            {
                ThinkingBudget = _thinkingBudgetTokens,
                ThinkingLevel = _thinkingLevel,
                IncludeThoughts = _includeThoughts,
            };
        }

        return config;
    }
}
