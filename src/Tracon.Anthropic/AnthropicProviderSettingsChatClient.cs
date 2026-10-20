using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Anthropic.Core;
using Anthropic.Models.Messages;
using Microsoft.Extensions.AI;

namespace Tracon;

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
/// <para>
/// It also keeps <see cref="ModelBinding.AllowMultipleToolCalls"/> from being dropped
/// without a word. Measured (Anthropic 12.58.0 adapter, decompiled): the adapter reads
/// <see cref="ChatOptions.AllowMultipleToolCalls"/> only while it builds <c>tool_choice</c>
/// from <see cref="ChatOptions.ToolMode"/>, so a request that sets the first and not the
/// second goes out without <c>disable_parallel_tool_use</c>. When the request carries tools
/// and no tool mode, the decorator sets <see cref="ChatToolMode.Auto"/>, which is what the
/// provider does without a <c>tool_choice</c>, so the flag has a place to travel in.
/// </para>
/// <para>
/// Every Anthropic client carries this decorator, also when no setting changes
/// the request: it answers <see cref="ModelOutputLimit"/> through
/// <see cref="GetService"/>, so the validation report knows the
/// <c>max_tokens</c> this client sends when the binding sets none. Without a
/// setting that changes the body, the caller's options pass through unchanged
/// — no copy and no raw body on the plain path.
/// </para>
/// </remarks>
internal sealed class AnthropicProviderSettingsChatClient : DelegatingChatClient
{
    private readonly string _model;
    private readonly int _defaultMaxOutputTokens;
    private readonly AnthropicRequestSettings _settings;
    private readonly ModelOutputLimit _outputLimit;

    public AnthropicProviderSettingsChatClient(
        IChatClient inner,
        string model,
        int defaultMaxOutputTokens,
        AnthropicRequestSettings settings)
        : base(inner)
    {
        _model = model;
        _defaultMaxOutputTokens = defaultMaxOutputTokens;
        _settings = settings;
        _outputLimit = new ModelOutputLimit
        {
            DefaultMaxOutputTokens = defaultMaxOutputTokens,
            HighReasoningEffort = settings.HighEffort,
        };
    }

    /// <inheritdoc />
    public override object? GetService(System.Type serviceType, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);

        return serviceKey is null && serviceType == typeof(ModelOutputLimit)
            ? _outputLimit
            : base.GetService(serviceType, serviceKey);
    }

    /// <inheritdoc />
    public override Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options = CarryParallelToolUse(options);

        if (!_settings.ChangesRequest)
        {
            return base.GetResponseAsync(messages, options, cancellationToken);
        }

        var (requestMessages, requestOptions) = Apply(messages, options);

        return base.GetResponseAsync(requestMessages, requestOptions, cancellationToken);
    }

    /// <inheritdoc />
    public override IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options = CarryParallelToolUse(options);

        if (!_settings.ChangesRequest)
        {
            return base.GetStreamingResponseAsync(messages, options, cancellationToken);
        }

        var (requestMessages, requestOptions) = Apply(messages, options);

        return base.GetStreamingResponseAsync(requestMessages, requestOptions, cancellationToken);
    }

    /// <summary>
    /// Gives <see cref="ChatOptions.AllowMultipleToolCalls"/> a <c>tool_choice</c> to travel in; see the class remarks.
    /// A new options object: the caller's is shared by every run of the compiled agent and is never written.
    /// </summary>
    private static ChatOptions? CarryParallelToolUse(ChatOptions? options)
    {
        if (options is not { AllowMultipleToolCalls: not null, ToolMode: null, Tools: { Count: > 0 } })
        {
            return options;
        }

        var copy = options.Clone();
        copy.ToolMode = ChatToolMode.Auto;

        return copy;
    }

    private (IEnumerable<ChatMessage> Messages, ChatOptions Options) Apply(IEnumerable<ChatMessage> messages, ChatOptions? options)
    {
        var copy = options?.Clone() ?? new ChatOptions();

        // Left untouched when the caller has already given its own raw
        // representation: this is a path the consumer deliberately took over, and
        // writing over it would silently change its behavior.
        if (copy.RawRepresentationFactory is not null)
        {
            return (messages, copy);
        }

        var markInstructions = _settings.PromptCaching
            && _settings.CachePlacement == AnthropicCachePlacement.System
            && !string.IsNullOrEmpty(copy.Instructions);

        if (markInstructions)
        {
            // 🚨 The adapter turns ChatOptions.Instructions into a system block
            // WITHOUT cache control, and reads cache control only from a
            // content's AdditionalProperties. The instructions therefore travel
            // as a system message of their own, placed after the caller's
            // LEADING system messages and before the first other message: the
            // adapter gathers the system messages in order and adds
            // Instructions last, so the block keeps today's position.
            // 🚨 Position matters since Anthropic SDK 12.58 (measured, adapter
            // decompiled): only system messages BEFORE the first user or
            // assistant message join the top-level `system` array; a later one
            // goes out as a `role: system` entry inside `messages`, and the
            // cache breakpoint lands in the conversation instead of after the
            // instructions. A new list, message and content — the caller's
            // list and contents are never mutated.
            var block = new TextContent(copy.Instructions).WithCacheControl(CacheControl());

            var requestMessages = messages.ToList();
            var firstConversational = requestMessages.FindIndex(static message => message.Role != ChatRole.System);

            requestMessages.Insert(
                firstConversational < 0 ? requestMessages.Count : firstConversational,
                new ChatMessage(ChatRole.System, [block]));

            messages = requestMessages;
            copy.Instructions = null;
        }

        copy.RawRepresentationFactory = _ => BuildParams(copy, topLevelCache: _settings.PromptCaching && !markInstructions);

        return (messages, copy);
    }

    // Init-only on the SDK type (CS8852), hence the two initializers.
    private CacheControlEphemeral CacheControl()
        => _settings.CacheTtl is { } ttl
            ? new CacheControlEphemeral { Ttl = SdkTtlOf(ttl) }
            : new CacheControlEphemeral();

    private static Ttl SdkTtlOf(string wireValue)
    {
        foreach (var candidate in Enum.GetValues<Ttl>())
        {
            if (string.Equals(((ApiEnum<string, Ttl>)candidate).Raw(), wireValue, StringComparison.Ordinal))
            {
                return candidate;
            }
        }

        // AnthropicRequestSettings accepted the value from this same list.
        throw new InvalidOperationException($"'{wireValue}' is not a cache TTL the Anthropic SDK knows.");
    }

    private MessageCreateParams BuildParams(ChatOptions options, bool topLevelCache)
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

        if (topLevelCache)
        {
            body["cache_control"] = JsonSerializer.SerializeToElement(
                new AnthropicCacheControlPayload("ephemeral", _settings.CacheTtl),
                AnthropicRawJsonContext.Default.AnthropicCacheControlPayload);
        }

        switch (_settings.ThinkingMode)
        {
            case AnthropicThinkingMode.Enabled:
                body["thinking"] = JsonSerializer.SerializeToElement(
                    new AnthropicThinkingPayload("enabled", _settings.ThinkingBudgetTokens),
                    AnthropicRawJsonContext.Default.AnthropicThinkingPayload);
                break;

            case AnthropicThinkingMode.Adaptive:
                body["thinking"] = JsonSerializer.SerializeToElement(
                    new AnthropicThinkingPayload("adaptive", BudgetTokens: null),
                    AnthropicRawJsonContext.Default.AnthropicThinkingPayload);
                break;

            default:
                break;
        }

        // 🚨 Measured (Anthropic SDK 12.39.0, decompiled adapter): the adapter
        // adds a structured-output schema with `OutputConfig ?? new OutputConfig()`
        // and a `with { Format = ... }` copy, so the effort written here survives
        // next to the format; AnthropicRequestSettingsTests pins the wire body.
        if (_settings.Effort is { } effort)
        {
            body["output_config"] = JsonSerializer.SerializeToElement(
                new AnthropicOutputConfigPayload(effort),
                AnthropicRawJsonContext.Default.AnthropicOutputConfigPayload);
        }

        return MessageCreateParams.FromRawUnchecked(
            new Dictionary<string, JsonElement>(StringComparer.Ordinal),
            new Dictionary<string, JsonElement>(StringComparer.Ordinal),
            body);
    }
}

/// <summary>The <c>cache_control</c> body fragment.</summary>
internal sealed record AnthropicCacheControlPayload(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("ttl")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Ttl);

/// <summary>The <c>thinking</c> body fragment.</summary>
/// <remarks>
/// <c>budget_tokens</c> is left out when it is <see langword="null"/>: the
/// adaptive mode takes none, and a written <c>null</c> is not "absent" to the
/// API (it rejects <c>"thinking": null</c> the same way).
/// </remarks>
internal sealed record AnthropicThinkingPayload(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("budget_tokens")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    int? BudgetTokens);

/// <summary>The <c>output_config</c> body fragment.</summary>
internal sealed record AnthropicOutputConfigPayload(
    [property: JsonPropertyName("effort")] string Effort);

/// <summary>
/// The source generator context for the small fragments written into the raw request body.
/// </summary>
/// <remarks>
/// These small records are serialized instead of the SDK's own model types:
/// reflection-based <c>JsonSerializer</c> overloads produce <c>IL2026</c>/<c>IL3050</c>,
/// and <c>Tracon.Anthropic</c> is marked AOT compatible.
/// </remarks>
[JsonSourceGenerationOptions(JsonSerializerDefaults.General)]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(string[]))]
[JsonSerializable(typeof(AnthropicCacheControlPayload))]
[JsonSerializable(typeof(AnthropicThinkingPayload))]
[JsonSerializable(typeof(AnthropicOutputConfigPayload))]
internal sealed partial class AnthropicRawJsonContext : JsonSerializerContext;
