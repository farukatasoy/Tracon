using AgentPrism.OpenAI.UnitTests.Infrastructure;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AgentPrism.OpenAI.UnitTests;

/// <summary>
/// Verifies the name validation, key-less local provider behavior, and provider
/// registration of the <c>UseOpenAICompatible()</c> call.
/// </summary>
public sealed class OpenAICompatibleProviderExtensionsTests
{
    private static readonly Uri OpenRouterEndpoint = new("https://openrouter.example.com/api/v1");
    private static readonly Uri LocalEndpoint = new("http://localhost:11434/v1");

    [Fact]
    public void Named_provider_appears_in_the_registry()
    {
        using var provider = BuildProvider(o => o.Endpoint = OpenRouterEndpoint);

        var names = provider.GetRequiredService<IModelProviderRegistry>()
            .List().Select(static descriptor => descriptor.Name).ToList();

        names.ShouldContain(static name => string.Equals(name, "openrouter", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(OpenAIProviderNames.ChatCompletions)]
    [InlineData(OpenAIProviderNames.Responses)]
    [InlineData("OPENAI")]
    public void Reserved_name_is_rejected(string name)
    {
        var builder = new ServiceCollection().AddAgentPrism();

        var exception = Should.Throw<ArgumentException>(
            () => builder.UseOpenAICompatible(name, o => o.Endpoint = OpenRouterEndpoint));

        exception.Message.ShouldContain("reserved");
    }

    [Theory]
    [InlineData("Openrouter")] // upper case
    [InlineData("open router")] // space
    [InlineData("-openrouter")] // starts with a hyphen
    [InlineData("open_router")] // underscore
    [InlineData("")]
    public void Invalid_name_pattern_is_rejected(string name)
    {
        var builder = new ServiceCollection().AddAgentPrism();

        Should.Throw<ArgumentException>(
            () => builder.UseOpenAICompatible(name, o => o.Endpoint = OpenRouterEndpoint));
    }

    [Fact]
    public void A_32_character_name_is_accepted_a_33_character_name_is_rejected()
    {
        var builder = new ServiceCollection().AddAgentPrism();
        var name32 = new string('a', 32);
        var name33 = new string('a', 33);

        Should.NotThrow(() => builder.UseOpenAICompatible(name32, o => o.Endpoint = OpenRouterEndpoint));
        Should.Throw<ArgumentException>(
            () => builder.UseOpenAICompatible(name33, o => o.Endpoint = OpenRouterEndpoint));
    }

    [Fact]
    public void Named_provider_without_an_endpoint_fails_validation()
    {
        var services = new ServiceCollection();
        services.AddAgentPrism().UseOpenAICompatible("openrouter", static _ => { });

        using var provider = services.BuildServiceProvider();

        var exception = Should.Throw<OptionsValidationException>(() => provider
            .GetRequiredService<IOptionsMonitor<OpenAIProviderOptions>>()
            .Get("openrouter"));

        exception.Message.ShouldContain(nameof(OpenAIProviderOptions.Endpoint));
    }

    [Fact]
    public void Key_less_local_provider_passes_validation_and_produces_a_client()
    {
        // F-05: local servers (Ollama, LM Studio) do not ask for an API key.
        using var provider = BuildProvider(o => o.Endpoint = LocalEndpoint, name: "ollama");

        using var chatClient = provider.GetRequiredService<IModelProviderRegistry>()
            .CreateChatClient(new ModelBinding { Provider = "ollama", Model = "llama3.1" });

        chatClient.ShouldNotBeNull();
    }

    [Fact]
    public void Key_less_providers_client_connects_to_the_right_address()
    {
        using var provider = BuildProvider(o => o.Endpoint = LocalEndpoint, name: "ollama");

        using var chatClient = provider.GetRequiredService<IModelProviderRegistry>()
            .CreateChatClient(new ModelBinding { Provider = "ollama", Model = "llama3.1" });

        var metadata = chatClient.GetService(typeof(ChatClientMetadata)).ShouldBeOfType<ChatClientMetadata>();
        metadata.ProviderUri.ShouldNotBeNull();
        metadata.ProviderUri!.Host.ShouldBe("localhost");
    }

    [Fact]
    public void Two_named_providers_connect_to_different_addresses()
    {
        var services = new ServiceCollection();
        services.AddAgentPrism()
            .UseOpenAICompatible("openrouter", o => o.Endpoint = OpenRouterEndpoint)
            .UseOpenAICompatible("ollama", o => o.Endpoint = LocalEndpoint);

        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<IModelProviderRegistry>();

        using var openRouterClient = registry.CreateChatClient(
            new ModelBinding { Provider = "openrouter", Model = "openai/gpt-5.4-mini" });
        using var ollamaClient = registry.CreateChatClient(
            new ModelBinding { Provider = "ollama", Model = "llama3.1" });

        var openRouterHost = openRouterClient.GetService(typeof(ChatClientMetadata))
            .ShouldBeOfType<ChatClientMetadata>().ProviderUri!.Host;
        var ollamaHost = ollamaClient.GetService(typeof(ChatClientMetadata))
            .ShouldBeOfType<ChatClientMetadata>().ProviderUri!.Host;

        openRouterHost.ShouldBe("openrouter.example.com");
        ollamaHost.ShouldBe("localhost");
    }

    [Fact]
    public void Responses_surface_is_not_registered_by_default()
    {
        using var provider = BuildProvider(o => o.Endpoint = OpenRouterEndpoint);

        var names = provider.GetRequiredService<IModelProviderRegistry>()
            .List().Select(static descriptor => descriptor.Name).ToList();

        names.ShouldNotContain(static name => string.Equals(name, "openrouter-responses", StringComparison.Ordinal));
    }

    [Fact]
    public void A_second_provider_is_registered_when_the_Responses_surface_is_explicitly_requested()
    {
        using var provider = BuildProvider(o =>
        {
            o.Endpoint = OpenRouterEndpoint;
            o.EnableResponsesSurface = true;
        });

        var names = provider.GetRequiredService<IModelProviderRegistry>()
            .List().Select(static descriptor => descriptor.Name).ToList();

        names.ShouldContain(static name => string.Equals(name, "openrouter", StringComparison.Ordinal));
        names.ShouldContain(static name => string.Equals(name, "openrouter-responses", StringComparison.Ordinal));
    }

    [Fact]
    public void Second_call_does_not_duplicate_the_provider()
    {
        var services = new ServiceCollection();
        services.AddAgentPrism()
            .UseOpenAICompatible("openrouter", o => o.Endpoint = OpenRouterEndpoint)
            .UseOpenAICompatible("openrouter", o => o.DefaultModel = "openai/gpt-5.4-mini");

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IModelProviderRegistry>().List()
            .Count(static descriptor => string.Equals(descriptor.Name, "openrouter", StringComparison.Ordinal))
            .ShouldBe(1);
    }

    [Fact]
    public void Options_are_read_from_configuration()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["ApiKey"] = TestData.ApiKey,
                ["Endpoint"] = "https://openrouter.example.com/api/v1",
                ["DefaultModel"] = "openai/gpt-5.4-mini",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddAgentPrism().UseOpenAICompatible("openrouter", configuration);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptionsMonitor<OpenAIProviderOptions>>().Get("openrouter");

        options.ApiKey.ShouldBe(TestData.ApiKey);
        options.Endpoint.ShouldBe(new Uri("https://openrouter.example.com/api/v1"));
        options.DefaultModel.ShouldBe("openai/gpt-5.4-mini");
    }

    [Fact]
    public void Null_chain_is_rejected()
        => Should.Throw<ArgumentNullException>(
            () => OpenAICompatibleProviderExtensions.UseOpenAICompatible(null!, "openrouter", static _ => { }));

    [Fact]
    public void Null_configure_callback_is_rejected()
    {
        var builder = new ServiceCollection().AddAgentPrism();

        Should.Throw<ArgumentNullException>(
            () => builder.UseOpenAICompatible("openrouter", (Action<OpenAIProviderOptions>)null!));
    }

    private static ServiceProvider BuildProvider(Action<OpenAIProviderOptions> configure, string name = "openrouter")
    {
        var services = new ServiceCollection();
        services.AddAgentPrism().UseOpenAICompatible(name, configure);

        return services.BuildServiceProvider();
    }
}
