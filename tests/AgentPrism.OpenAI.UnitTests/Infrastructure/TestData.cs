namespace AgentPrism.OpenAI.UnitTests.Infrastructure;

/// <summary>Helpers that produce objects repeated across tests.</summary>
internal static class TestData
{
    /// <summary>
    /// The fake API key used in tests. It is not a real key and no real call is made;
    /// it is chosen so it does not trip the secret scan pattern.
    /// </summary>
    public const string ApiKey = "test-api-key-1234567890";

    public static ModelBinding Binding(string model = "gpt-4o-mini")
        => new() { Provider = OpenAIProviderNames.ChatCompletions, Model = model };

    public static OpenAIProviderOptions Options(Action<OpenAIProviderOptions>? configure = null)
    {
        var options = new OpenAIProviderOptions { ApiKey = ApiKey };
        configure?.Invoke(options);
        return options;
    }
}
