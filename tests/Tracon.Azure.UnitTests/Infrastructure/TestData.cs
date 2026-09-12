using System.Text.Json;
using Azure.Core;

namespace Tracon.Azure.UnitTests.Infrastructure;

/// <summary>Helpers that produce objects that repeat across tests.</summary>
internal static class TestData
{
    /// <summary>
    /// Fake API key used in tests. It is not a real key, and no real call is made.
    /// </summary>
    public const string ApiKey = "test-key-1234567890";

    /// <summary>
    /// Fake resource endpoint used in tests. It is not a real Azure resource.
    /// </summary>
    public const string EndpointText = "https://test-resource.openai.azure.com/";

    /// <summary>
    /// Deployment name used in tests. In Azure, this field carries a
    /// DEPLOYMENT name, not a MODEL name; the catalog is not a validation
    /// list (K-032).
    /// </summary>
    public const string Deployment = "production-gpt";

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
/// A credential that makes no network call and returns a fixed token. Tests
/// exercise the managed-identity path with this.
/// </summary>
internal sealed class FakeTokenCredential(string token = "fake-token") : TokenCredential
{
    /// <summary>Gets how many times the credential was asked to produce a token.</summary>
    public int RequestedTokenCount { get; private set; }

    /// <summary>Gets the last requested scope (audience).</summary>
    public string? LastScope { get; private set; }

    public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
    {
        RequestedTokenCount++;
        LastScope = requestContext.Scopes.Length > 0 ? requestContext.Scopes[0] : null;

        return new AccessToken(token, DateTimeOffset.UtcNow.AddHours(1));
    }

    public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
        => ValueTask.FromResult(GetToken(requestContext, cancellationToken));
}
