namespace AgentPrism.OpenAI.UnitTests.Infrastructure;

/// <summary>Testlerde tekrar eden nesneleri ureten yardimcilar.</summary>
internal static class TestData
{
    /// <summary>
    /// Testlerde kullanilan sahte API anahtari. Gercek bir anahtar degildir ve
    /// gercek bir cagri yapilmaz; sir tarama desenine takilmayacak sekilde secilmistir.
    /// </summary>
    public const string ApiKey = "test-anahtari-1234567890";

    public static ModelBinding Binding(string model = "gpt-4o-mini")
        => new() { Provider = OpenAIProviderNames.ChatCompletions, Model = model };

    public static OpenAIProviderOptions Options(Action<OpenAIProviderOptions>? configure = null)
    {
        var options = new OpenAIProviderOptions { ApiKey = ApiKey };
        configure?.Invoke(options);
        return options;
    }
}
