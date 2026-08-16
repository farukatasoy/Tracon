using AgentPrism.OpenAI.UnitTests.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AgentPrism.OpenAI.UnitTests;

/// <summary>
/// Verifies the service registrations of the <c>UseOpenAI()</c> call.
/// </summary>
/// <remarks>
/// <c>UsePostgreSql()</c> uses <c>Replace</c> because it takes the place of existing
/// stores; this call <em>adds</em> a new provider, so <c>AddModelProvider</c> is enough.
/// Reason: <c>docs/KARARLAR.md</c>, decision K-025.
/// </remarks>
public sealed class OpenAIProviderExtensionsTests
{
    [Fact]
    public void UseOpenAI_registers_two_providers()
    {
        using var provider = BuildProvider();

        var names = provider.GetServices<IModelProvider>().Select(static p => p.Name).ToList();

        names.ShouldContain(static name => string.Equals(name, OpenAIProviderNames.ChatCompletions, StringComparison.Ordinal));
        names.ShouldContain(static name => string.Equals(name, OpenAIProviderNames.Responses, StringComparison.Ordinal));
    }

    [Fact]
    public void Providers_are_resolved_from_the_registry_by_name()
    {
        using var provider = BuildProvider();
        var registry = provider.GetRequiredService<IModelProviderRegistry>();

        using var chat = registry.CreateChatClient(
            new ModelBinding { Provider = OpenAIProviderNames.ChatCompletions, Model = "gpt-4o-mini" });
        using var responses = registry.CreateChatClient(
            new ModelBinding { Provider = OpenAIProviderNames.Responses, Model = "gpt-4o-mini" });

        chat.ShouldNotBeNull();
        responses.ShouldNotBeNull();
    }

    [Fact]
    public void Provider_name_is_case_insensitive()
    {
        using var provider = BuildProvider();

        using var chatClient = provider.GetRequiredService<IModelProviderRegistry>()
            .CreateChatClient(new ModelBinding { Provider = "OpenAI", Model = "gpt-4o-mini" });

        chatClient.ShouldNotBeNull();
    }

    [Fact]
    public void Both_providers_share_the_same_chat_client_factory()
    {
        // One OpenAIClient, one HTTP connection pool. Two factories would fragment the pool.
        using var provider = BuildProvider();

        var providers = provider.GetServices<IModelProvider>().OfType<OpenAIModelProvider>().ToList();

        providers.Count.ShouldBe(2);
        provider.GetRequiredService<OpenAIChatClientFactory>()
            .ShouldBeSameAs(provider.GetRequiredService<OpenAIChatClientFactory>());
    }

    [Fact]
    public void Second_call_does_not_duplicate_providers()
    {
        var services = new ServiceCollection();
        services.AddAgentPrism()
            .UseOpenAI(TestData.ApiKey)
            .UseOpenAI(options => options.DefaultModel = "gpt-4o");

        using var provider = services.BuildServiceProvider();

        provider.GetServices<IModelProvider>().Count().ShouldBe(2);
        provider.GetRequiredService<IOptions<OpenAIProviderOptions>>().Value.DefaultModel.ShouldBe("gpt-4o");
        provider.GetRequiredService<IModelProviderRegistry>().List().Count.ShouldBe(2);
    }

    [Fact]
    public void Catalog_is_empty_by_default()
    {
        // AgentPrism carries no built-in model list; decision K-032.
        using var provider = BuildProvider();

        var descriptors = provider.GetRequiredService<IModelProviderRegistry>().List();

        descriptors.Count.ShouldBe(2);
        descriptors.ShouldAllBe(static descriptor => descriptor.Models.Count == 0);
    }

    [Fact]
    public void Catalog_given_in_code_appears_in_the_registry()
    {
        using var provider = BuildProvider(options =>
            options.Models.Add(new ModelDescriptor { Name = "gpt-5.4-mini" }));

        var descriptors = provider.GetRequiredService<IModelProviderRegistry>().List();

        descriptors.ShouldAllBe(static descriptor => descriptor.Models.Count == 1);
    }

    [Fact]
    public void Options_are_read_from_configuration()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["ApiKey"] = TestData.ApiKey,
                ["DefaultModel"] = "gpt-4.1-mini",
                ["Endpoint"] = "https://intermediate-server.example.com/v1/",
                ["Organization"] = "org-test",
                ["Timeout"] = "00:01:30",
                ["Models:0:Name"] = "custom-model",
                ["Models:0:ContextWindowTokens"] = "65536",
                ["Models:0:InputCostPerMillionTokens"] = "1.25",
                ["Models:0:SupportsReasoning"] = "true",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddAgentPrism().UseOpenAI(configuration);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<OpenAIProviderOptions>>().Value;

        options.ApiKey.ShouldBe(TestData.ApiKey);
        options.DefaultModel.ShouldBe("gpt-4.1-mini");
        options.Endpoint.ShouldBe(new Uri("https://intermediate-server.example.com/v1/"));
        options.Organization.ShouldBe("org-test");
        options.Timeout.ShouldBe(TimeSpan.FromSeconds(90));

        var model = options.Models.ShouldHaveSingleItem();
        model.Name.ShouldBe("custom-model");
        model.ContextWindowTokens.ShouldBe(65_536);
        model.InputCostPerMillionTokens.ShouldBe(1.25m);
        model.SupportsReasoning.ShouldBeTrue();
        model.SupportsTools.ShouldBeTrue();
    }

    [Fact]
    public void Model_from_configuration_appears_in_the_catalog()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["ApiKey"] = TestData.ApiKey,
                ["Models:0:Name"] = "only-this-one",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddAgentPrism().UseOpenAI(configuration);

        using var provider = services.BuildServiceProvider();

        var descriptor = provider.GetRequiredService<IModelProviderRegistry>().List()[0];

        descriptor.Models.ShouldHaveSingleItem().Name.ShouldBe("only-this-one");
    }

    [Fact]
    public void Empty_api_key_fails_validation()
    {
        var services = new ServiceCollection();
        services.AddAgentPrism().UseOpenAI(options => options.ApiKey = "   ");

        using var provider = services.BuildServiceProvider();

        var exception = Should.Throw<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<OpenAIProviderOptions>>().Value);

        exception.Message.ShouldContain(nameof(OpenAIProviderOptions.ApiKey));
    }

    [Fact]
    public void Relative_address_is_rejected()
    {
        var services = new ServiceCollection();
        services.AddAgentPrism().UseOpenAI(TestData.ApiKey, options => options.Endpoint = new Uri("/v1", UriKind.Relative));

        using var provider = services.BuildServiceProvider();

        Should.Throw<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<OpenAIProviderOptions>>().Value);
    }

    [Fact]
    public void Relative_address_from_configuration_is_rejected()
    {
        // HATA-S3-001: Bind() used to never assign Endpoint at all when
        // Uri.TryCreate(..., UriKind.Absolute, ...) failed; the value was silently
        // dropped before it ever reached the validator.
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["ApiKey"] = TestData.ApiKey,
                ["Endpoint"] = "just-a-path",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddAgentPrism().UseOpenAI(configuration);

        using var provider = services.BuildServiceProvider();

        Should.Throw<OptionsValidationException>(
                () => provider.GetRequiredService<IOptions<OpenAIProviderOptions>>().Value)
            .Message.ShouldContain(nameof(OpenAIProviderOptions.Endpoint));
    }

    [Fact]
    public void Nameless_model_from_configuration_is_rejected()
    {
        // HATA-S3-002: BindModels() used to never add an item with an empty Name to the
        // list at all; the validator never saw an item with an empty name.
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["ApiKey"] = TestData.ApiKey,
                ["Models:0:Name"] = "gpt-4o-mini",
                ["Models:1:Name"] = "",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddAgentPrism().UseOpenAI(configuration);

        using var provider = services.BuildServiceProvider();

        Should.Throw<OptionsValidationException>(
                () => provider.GetRequiredService<IOptions<OpenAIProviderOptions>>().Value)
            .Message.ShouldContain(nameof(OpenAIProviderOptions.Models));
    }

    [Fact]
    public void Zero_timeout_is_rejected()
    {
        var services = new ServiceCollection();
        services.AddAgentPrism().UseOpenAI(TestData.ApiKey, options => options.Timeout = TimeSpan.Zero);

        using var provider = services.BuildServiceProvider();

        Should.Throw<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<OpenAIProviderOptions>>().Value);
    }

    [Fact]
    public void Empty_api_key_argument_is_rejected()
    {
        var builder = new ServiceCollection().AddAgentPrism();

        Should.Throw<ArgumentException>(() => builder.UseOpenAI("  "));
    }

    [Fact]
    public void Null_chain_is_rejected()
        => Should.Throw<ArgumentNullException>(
            () => OpenAIProviderExtensions.UseOpenAI(null!, TestData.ApiKey));

    private static ServiceProvider BuildProvider(Action<OpenAIProviderOptions>? configure = null)
    {
        var services = new ServiceCollection();

        services.AddAgentPrism().UseOpenAI(TestData.ApiKey, configure);

        return services.BuildServiceProvider();
    }
}
