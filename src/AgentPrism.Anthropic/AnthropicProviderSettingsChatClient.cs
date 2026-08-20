using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Anthropic.Models.Messages;
using Microsoft.Extensions.AI;

namespace AgentPrism;

/// <summary>
/// Applies the <c>anthropic.*</c> settings from <see cref="ModelBinding.ProviderSettings"/>
/// to every request.
/// </summary>
/// <remarks>
/// <para>
/// Settings are sent through <c>Microsoft.Extensions.AI</c>'s official escape
/// hatch, <see cref="ChatOptions.RawRepresentationFactory"/>: the factory produces
/// a <see cref="MessageCreateParams"/> and the Anthropic adapter builds the
/// request on top of that object. Measured: the adapter
/// <strong>keeps</strong> the fields we write and only adds the fields it
/// produces itself, such as <c>messages</c>/<c>system</c>/<c>tools</c> — so writing
/// <c>model</c> and <c>max_tokens</c> here is required, otherwise the request goes
/// out with our placeholder values.
/// </para>
/// <para>
/// The decorator sits <strong>inside</strong> <c>UseFunctionInvocation()</c>; every
/// turn of the tool loop goes out with the same settings.
/// </para>
/// <para>
/// The caller's <see cref="ChatOptions"/> instance is <strong>never mutated</strong>.
/// A compiled agent shares a single <see cref="ChatOptions"/> instance across all
/// calls; writing over it would mix up concurrent runs.
/// </para>
/// </remarks>
internal sealed class AnthropicProviderSettingsChatClient : DelegatingChatClient
{
    private readonly string _model;
    private readonly int _defaultMaxOutputTokens;
    private readonly bool _promptCaching;
    private readonly int? _thinkingBudgetTokens;

    public AnthropicProviderSettingsChatClient(
        IChatClient inner,
        string model,
        int defaultMaxOutputTokens,
        bool promptCaching,
        int? thinkingBudgetTokens)
        : base(inner)
    {
        _model = model;
        _defaultMaxOutputTokens = defaultMaxOutputTokens;
        _promptCaching = promptCaching;
        _thinkingBudgetTokens = thinkingBudgetTokens;
    }

    /// <summary>Gets whether there is a setting to apply to the binding.</summary>
    internal static bool HasSettings(bool promptCaching, int? thinkingBudgetTokens)
        => promptCaching || thinkingBudgetTokens is not null;

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

        // Left untouched when the caller has already given its own raw
        // representation: this is a path the consumer deliberately took over, and
        // writing over it would silently change its behavior.
        copy.RawRepresentationFactory ??= _ => BuildParams(copy);

        return copy;
    }

    private MessageCreateParams BuildParams(ChatOptions options)
    {
        var body = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["model"] = JsonSerializer.SerializeToElement(
                options.ModelId ?? _model,
                AnthropicRawJsonContext.Default.String),
            ["max_tokens"] = JsonSerializer.SerializeToElement(
                options.MaxOutputTokens ?? _defaultMaxOutputTokens,
                AnthropicRawJsonContext.Default.Int32),

            // The adapter writes the actual messages here; the key's presence is
            // required for the SDK's client-side validation ("'messages' cannot be absent").
            ["messages"] = JsonSerializer.SerializeToElement(
                Array.Empty<string>(),
                AnthropicRawJsonContext.Default.StringArray),
        };

        if (_promptCaching)
        {
            body["cache_control"] = JsonSerializer.SerializeToElement(
                new AnthropicCacheControlPayload("ephemeral"),
                AnthropicRawJsonContext.Default.AnthropicCacheControlPayload);
        }

        if (_thinkingBudgetTokens is { } budget)
        {
            body["thinking"] = JsonSerializer.SerializeToElement(
                new AnthropicThinkingPayload("enabled", budget),
                AnthropicRawJsonContext.Default.AnthropicThinkingPayload);
        }

        return MessageCreateParams.FromRawUnchecked(
            new Dictionary<string, JsonElement>(StringComparer.Ordinal),
            new Dictionary<string, JsonElement>(StringComparer.Ordinal),
            body);
    }
}

/// <summary>The <c>cache_control</c> body fragment.</summary>
internal sealed record AnthropicCacheControlPayload(
    [property: JsonPropertyName("type")] string Type);

/// <summary>The <c>thinking</c> body fragment.</summary>
internal sealed record AnthropicThinkingPayload(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("budget_tokens")] int BudgetTokens);

/// <summary>
/// The source generator context for the small fragments written into the raw request body.
/// </summary>
/// <remarks>
/// These small records are serialized instead of the SDK's own model types:
/// reflection-based <c>JsonSerializer</c> overloads produce <c>IL2026</c>/<c>IL3050</c>,
/// and <c>AgentPrism.Anthropic</c> is marked AOT compatible.
/// </remarks>
[JsonSourceGenerationOptions(JsonSerializerDefaults.General)]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(string[]))]
[JsonSerializable(typeof(AnthropicCacheControlPayload))]
[JsonSerializable(typeof(AnthropicThinkingPayload))]
internal sealed partial class AnthropicRawJsonContext : JsonSerializerContext;
