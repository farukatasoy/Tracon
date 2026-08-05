using System.Text.Json;
using Azure.Core;

namespace AgentPrism.Azure.UnitTests.Infrastructure;

/// <summary>Testlerde tekrar eden nesneleri ureten yardimcilar.</summary>
internal static class TestData
{
    /// <summary>
    /// Testlerde kullanilan sahte API anahtari. Gercek bir anahtar degildir ve
    /// gercek bir cagri yapilmaz.
    /// </summary>
    public const string ApiKey = "test-anahtari-1234567890";

    /// <summary>
    /// Testlerde kullanilan sahte kaynak adresi. Gercek bir Azure kaynagi degildir.
    /// </summary>
    public const string EndpointText = "https://test-kaynagi.openai.azure.com/";

    /// <summary>
    /// Testlerde kullanilan deployment adi. Azure'da bu alan MODEL adi degil
    /// DEPLOYMENT adi tasir; katalog bir dogrulama listesi degildir (K-032).
    /// </summary>
    public const string Deployment = "uretim-gpt";

    public static Uri Endpoint { get; } = new(EndpointText);

    public static ModelBinding Binding(
        string deployment = Deployment,
        IReadOnlyDictionary<string, JsonElement>? providerSettings = null)
        => new()
        {
            Provider = AzureOpenAIProviderNames.AzureOpenAI,
            Model = deployment,
            MaxOutputTokens = 1024,
            ProviderSettings = providerSettings
                ?? new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase),
        };

    public static AzureOpenAIProviderOptions Options(Action<AzureOpenAIProviderOptions>? configure = null)
    {
        var options = new AzureOpenAIProviderOptions { Endpoint = Endpoint, ApiKey = ApiKey };
        configure?.Invoke(options);
        return options;
    }

    public static Dictionary<string, JsonElement> Settings(params (string Key, object Value)[] entries)
    {
        var settings = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);

        foreach (var (key, value) in entries)
        {
            settings[key] = value switch
            {
                bool boolean => JsonSerializer.SerializeToElement(boolean),
                int number => JsonSerializer.SerializeToElement(number),
                _ => JsonSerializer.SerializeToElement(value.ToString()),
            };
        }

        return settings;
    }
}

/// <summary>
/// Ag cagrisi yapmayan, sabit bir token dondururen kimlik. Testler yonetilen
/// kimlik yolunu bununla dolasir.
/// </summary>
internal sealed class SahteTokenKimligi(string token = "sahte-token") : TokenCredential
{
    /// <summary>Kimligin kac kez token uretmesi istendi.</summary>
    public int IstenenTokenSayisi { get; private set; }

    /// <summary>Son istenen kapsam (audience).</summary>
    public string? SonKapsam { get; private set; }

    public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
    {
        IstenenTokenSayisi++;
        SonKapsam = requestContext.Scopes.Length > 0 ? requestContext.Scopes[0] : null;

        return new AccessToken(token, DateTimeOffset.UtcNow.AddHours(1));
    }

    public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
        => ValueTask.FromResult(GetToken(requestContext, cancellationToken));
}
