using System.Text.Json;

namespace Tracon.Google.UnitTests.Infrastructure;

/// <summary>Helpers that produce objects repeated across tests.</summary>
internal static class TestData
{
    /// <summary>
    /// Fake API key used in tests. Not a real key, and no real call is made.
    /// </summary>
    public const string ApiKey = "test-api-key-1234567890";

    /// <summary>Model name used in tests. The catalog is not a validation list (K-032).</summary>
    public const string Model = "gemini-3.6-flash";

    public static ModelBinding Binding(
        string model = Model,
        IReadOnlyDictionary<string, JsonElement>? providerSettings = null)
        => new()
        {
            Provider = GoogleProviderNames.Google,
            Model = model,
            ProviderSettings = providerSettings
                ?? new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase),
        };

    public static GoogleProviderOptions Options(Action<GoogleProviderOptions>? configure = null)
    {
        var options = new GoogleProviderOptions { ApiKey = ApiKey };
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
