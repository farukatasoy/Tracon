using Google.GenAI.Types;
using Microsoft.Extensions.AI;

namespace AgentPrism;

/// <summary>
/// <see cref="ModelBinding.ProviderSettings"/> icindeki <c>google.*</c> ayarlarini
/// her istege uygular.
/// </summary>
/// <remarks>
/// <para>
/// Ayarlar <c>Microsoft.Extensions.AI</c>'in resmi kacis kapisi olan
/// <see cref="ChatOptions.RawRepresentationFactory"/> uzerinden gonderilir: fabrika
/// bir <see cref="GenerateContentConfig"/> uretir ve Google adaptoru istegi bu
/// nesnenin uzerine kurar. Olculdu (2026-08-05): adaptor bizim yazdigimiz
/// <c>SafetySettings</c> ve <c>ThinkingConfig</c> alanlarini <strong>korur</strong>
/// ve tool tanimlarini uzerine ekler — tool cagrisi ile guvenlik esikleri ayni
/// istekte birlikte calisir.
/// </para>
/// <para>
/// Cagiranin <see cref="ChatOptions"/> ornegi <strong>degistirilmez</strong>. Derlenmis
/// bir agent tek bir <see cref="ChatOptions"/> ornegini tum cagrilarda paylasir;
/// uzerine yazmak es zamanli calistirmalari birbirine karistirirdi.
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

    /// <summary>Baglantida uygulanacak bir ayar var mi.</summary>
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

        // Cagiran kendi ham gosterimini vermisse ona dokunulmaz: bu, tuketicinin
        // bilincli olarak devraldigi bir yoldur.
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
