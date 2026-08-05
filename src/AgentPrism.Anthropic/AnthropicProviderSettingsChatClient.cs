using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Anthropic.Models.Messages;
using Microsoft.Extensions.AI;

namespace AgentPrism;

/// <summary>
/// <see cref="ModelBinding.ProviderSettings"/> icindeki <c>anthropic.*</c> ayarlarini
/// her istege uygular.
/// </summary>
/// <remarks>
/// <para>
/// Ayarlar <c>Microsoft.Extensions.AI</c>'in resmi kacis kapisi olan
/// <see cref="ChatOptions.RawRepresentationFactory"/> uzerinden gonderilir: fabrika
/// bir <see cref="MessageCreateParams"/> uretir ve Anthropic adaptoru istegi bu
/// nesnenin uzerine kurar. Olculdu (2026-08-05): adaptor bizim yazdigimiz alanlari
/// <strong>korur</strong> ve yalnizca <c>messages</c>/<c>system</c>/<c>tools</c>
/// gibi kendi urettigi alanlari ekler — bu yuzden <c>model</c> ve <c>max_tokens</c>
/// degerlerini burada yazmak zorunludur, aksi halde istek bizim yer tutucumuzla gider.
/// </para>
/// <para>
/// Dekorator <c>UseFunctionInvocation()</c>'in <strong>icinde</strong> durur; tool
/// dongusunun her turu ayni ayarlarla gider.
/// </para>
/// <para>
/// Cagiranin <see cref="ChatOptions"/> ornegi <strong>degistirilmez</strong>. Derlenmis
/// bir agent tek bir <see cref="ChatOptions"/> ornegini tum cagrilarda paylasir;
/// uzerine yazmak es zamanli calistirmalari birbirine karistirirdi.
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

    /// <summary>Baglantida uygulanacak bir ayar var mi.</summary>
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

        // Cagiran kendi ham gosterimini vermisse ona dokunulmaz: bu, tuketicinin
        // bilincli olarak devraldigi bir yoldur ve ustune yazmak sessizce
        // davranisini degistirirdi.
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

            // Adaptor gercek mesajlari buraya yazar; anahtarin varligi SDK'nin
            // istemci tarafi dogrulamasi icin gereklidir ("'messages' cannot be absent").
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

/// <summary><c>cache_control</c> govde parcasi.</summary>
internal sealed record AnthropicCacheControlPayload(
    [property: JsonPropertyName("type")] string Type);

/// <summary><c>thinking</c> govde parcasi.</summary>
internal sealed record AnthropicThinkingPayload(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("budget_tokens")] int BudgetTokens);

/// <summary>
/// Ham istek govdesine yazilan kucuk parcalarin kaynak ureteci baglami.
/// </summary>
/// <remarks>
/// SDK'nin kendi model tipleri yerine bu kucuk kayitlar serilestirilir: yansimaya
/// dayanan <c>JsonSerializer</c> asiri yuklemeleri <c>IL2026</c>/<c>IL3050</c> uretir
/// ve <c>AgentPrism.Anthropic</c> AOT uyumlu isaretlidir (karar K-006).
/// </remarks>
[JsonSourceGenerationOptions(JsonSerializerDefaults.General)]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(string[]))]
[JsonSerializable(typeof(AnthropicCacheControlPayload))]
[JsonSerializable(typeof(AnthropicThinkingPayload))]
internal sealed partial class AnthropicRawJsonContext : JsonSerializerContext;
