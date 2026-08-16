using Google.GenAI.Types;
using Microsoft.Extensions.AI;

namespace AgentPrism;

/// <summary>
/// Applies the <c>google.*</c> settings from <see cref="ModelBinding.ProviderSettings"/>
/// to every request.
/// </summary>
/// <remarks>
/// <para>
/// Settings are sent through <c>Microsoft.Extensions.AI</c>'s official escape
/// hatch, <see cref="ChatOptions.RawRepresentationFactory"/>: the factory builds a
/// <see cref="GenerateContentConfig"/> and the Google adapter builds the request on
/// top of this object. Measured (2026-08-05): the adapter <strong>preserves</strong>
/// the <c>SafetySettings</c> and <c>ThinkingConfig</c> fields we write and adds tool
/// definitions on top — tool calling and safety thresholds work together in the
/// same request.
/// </para>
/// <para>
/// The caller's <see cref="ChatOptions"/> instance is <strong>not mutated</strong>.
/// A compiled agent shares a single <see cref="ChatOptions"/> instance across all
/// calls; overwriting it would mix up concurrent runs.
/// </para>
/// </remarks>
internal sealed class GoogleProviderSettingsChatClient : DelegatingChatClient
{
    private readonly IReadOnlyList<SafetySetting> _safetySettings;
    private readonly int? _thinkingBudgetTokens;
    private readonly bool? _includeThoughts;

    public GoogleProviderSettingsChatClient(
        IChatClient inner,
        IReadOnlyList<SafetySetting> safetySettings,
        int? thinkingBudgetTokens,
        bool? includeThoughts)
        : base(inner)
    {
        _safetySettings = safetySettings;
        _thinkingBudgetTokens = thinkingBudgetTokens;
        _includeThoughts = includeThoughts;
    }

    /// <summary>Whether the binding has a setting to apply.</summary>
    internal static bool HasSettings(
        IReadOnlyList<SafetySetting> safetySettings,
        int? thinkingBudgetTokens,
        bool? includeThoughts)
        => safetySettings.Count > 0 || thinkingBudgetTokens is not null || includeThoughts is not null;

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
        copy.RawRepresentationFactory ??= _ => BuildConfig();

        return copy;
    }

    private GenerateContentConfig BuildConfig()
    {
        var config = new GenerateContentConfig();

        if (_safetySettings.Count > 0)
        {
            config.SafetySettings = [.. _safetySettings];
        }

        if (_thinkingBudgetTokens is not null || _includeThoughts is not null)
        {
            config.ThinkingConfig = new ThinkingConfig
            {
                ThinkingBudget = _thinkingBudgetTokens,
                IncludeThoughts = _includeThoughts,
            };
        }

        return config;
    }
}
