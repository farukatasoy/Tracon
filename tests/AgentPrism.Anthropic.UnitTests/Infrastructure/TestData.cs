using System.Text.Json;

namespace AgentPrism.Anthropic.UnitTests.Infrastructure;

/// <summary>Helpers that produce objects repeated across tests.</summary>
internal static class TestData
{
    /// <summary>
    /// A fake API key used in tests. Not a real key; no real call is made with it.
    /// </summary>
    public const string ApiKey = "test-api-key-1234567890";

    /// <summary>The model name used in tests. The catalog is not a validation list (K-032).</summary>
    public const string Model = "claude-sonnet-5";

    public static ModelBinding Binding(
        string model = Model,
        IReadOnlyDictionary<string, JsonElement>? providerSettings = null)
        => new()
        {
            Provider = AnthropicProviderNames.Anthropic,
            Model = model,
            MaxOutputTokens = 1024,
            ProviderSettings = providerSettings
                ?? new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase),
        };

    public static AnthropicProviderOptions Options(Action<AnthropicProviderOptions>? configure = null)
    {
        var options = new AnthropicProviderOptions { ApiKey = ApiKey };
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
