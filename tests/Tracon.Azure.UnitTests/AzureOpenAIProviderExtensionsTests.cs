using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Tracon.Azure.UnitTests.Infrastructure;

namespace Tracon.Azure.UnitTests;

/// <summary>
/// The shape of the <c>UseAzureOpenAI()</c> registration, its repetition,
/// and binding from configuration.
/// </summary>
public sealed class AzureOpenAIProviderExtensionsTests
{
#pragma warning disable MEAI001
    [Fact]
    public void UseAzureOpenAIImages_shares_the_authenticated_factory_and_uses_the_deployment()
    {
        var services = new ServiceCollection();
        services.AddTracon()
            .UseAzureOpenAI(TestData.Endpoint, TestData.ApiKey)
            .UseAzureOpenAIImages(options => options.Model = "image-deployment");

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredKeyedService<IImageGenerator>(AzureOpenAIProviderNames.AzureOpenAI).ShouldNotBeNull();
        provider.GetRequiredService<IOptions<TraconImageOptions>>().Value.Provider
            .ShouldBe(AzureOpenAIProviderNames.AzureOpenAI);
    }
#pragma warning restore MEAI001

    [Fact]
    public void Single_provider_is_registered()
    {
        using var provider = Build(builder => builder.UseAzureOpenAI(TestData.Endpoint, TestData.ApiKey));

        var names = provider.GetServices<IModelProvider>().Select(static p => p.Name).ToList();

        names.ShouldBe([AzureOpenAIProviderNames.AzureOpenAI]);
    }

    [Fact]
    public void Second_call_does_not_duplicate_the_provider()
    {
        using var provider = Build(builder => builder
            .UseAzureOpenAI(TestData.Endpoint, TestData.ApiKey)
            .UseAzureOpenAI(options => options.DefaultDeployment = TestData.Deployment));

        provider.GetServices<IModelProvider>().Count().ShouldBe(1);

        // The second call merges the settings; the registry does not throw
        // a "same name twice" error.
        provider.GetRequiredService<IOptions<AzureOpenAIProviderOptions>>().Value.DefaultDeployment
            .ShouldBe(TestData.Deployment);
        provider.GetRequiredService<IModelProviderRegistry>().List().Count.ShouldBe(1);
    }

    [Fact]
    public void Single_factory_is_shared()
    {
        using var provider = Build(builder => builder.UseAzureOpenAI(TestData.Endpoint, TestData.ApiKey));

        // A single client, a single HTTP connection pool.
        provider.GetRequiredService<AzureOpenAIChatClientFactory>()
            .ShouldBeSameAs(provider.GetRequiredService<AzureOpenAIChatClientFactory>());
    }

    [Fact]
    public void Reads_from_configuration()
    {
        using var provider = Build(builder => builder.UseAzureOpenAI(Configuration()));

        var options = provider.GetRequiredService<IOptions<AzureOpenAIProviderOptions>>().Value;

        options.Endpoint.ShouldBe(TestData.Endpoint);
        options.ApiKey.ShouldBe(TestData.ApiKey);
        options.DefaultDeployment.ShouldBe(TestData.Deployment);
        options.Audience.ShouldBe("https://cognitiveservices.azure.us/.default");
        options.Timeout.ShouldBe(TimeSpan.FromSeconds(30));
        options.Models.Single().Name.ShouldBe(TestData.Deployment);
        options.Models.Single().ContextWindowTokens.ShouldBe(128000);
        options.Models.Single().InputCostPerMillionTokens.ShouldBe(0.15m);
    }

    [Fact]
    public void Credential_factory_can_be_given_in_code_after_configuration_is_read()
    {
        // CredentialFactory is a delegate and cannot be read from
        // configuration; chaining a second call sets it in code afterward.
        var credential = new FakeTokenCredential();

        using var provider = Build(builder => builder
            .UseAzureOpenAI(Configuration())
            .UseAzureOpenAI(options => options.CredentialFactory = () => credential));

        var options = provider.GetRequiredService<IOptions<AzureOpenAIProviderOptions>>().Value;

        options.CredentialFactory.ShouldNotBeNull();
        options.CredentialFactory().ShouldBeSameAs(credential);
        options.ApiKey.ShouldBe(TestData.ApiKey);
    }

    [Fact]
    public void Registration_without_an_endpoint_fails_at_startup()
    {
        var services = new ServiceCollection();
        services.AddTracon().UseAzureOpenAI(options => options.ApiKey = TestData.ApiKey);

        using var provider = services.BuildServiceProvider();

        var exception = Should.Throw<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<AzureOpenAIProviderOptions>>().Value);

        exception.Message.ShouldContain(nameof(AzureOpenAIProviderOptions.Endpoint));
    }

    [Fact]
    public void Relative_address_from_configuration_is_rejected_as_relative_not_as_missing()
    {
        // Phase 181: the other three providers fixed this in HATA-S3-003; the
        // Azure copy kept UriKind.Absolute, so a relative value was silently
        // dropped and the operator was told the endpoint was MISSING.
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["ApiKey"] = TestData.ApiKey,
                ["Endpoint"] = "just-a-path",
            })
            .Build();

        using var provider = Build(builder => builder.UseAzureOpenAI(configuration));

        var message = Should.Throw<OptionsValidationException>(
                () => provider.GetRequiredService<IOptions<AzureOpenAIProviderOptions>>().Value)
            .Message;

        message.ShouldContain("must be an absolute address");
        message.ShouldNotContain("cannot be empty");
        message.ShouldNotContain("just-a-path");
    }

    [Fact]
    public void Nameless_deployment_from_configuration_is_rejected()
    {
        // Phase 181: the Azure copy of BindModels skipped an empty Name
        // (HATA-S3-002/004 fixed the other three), which left the validator's
        // Models[i] branch unreachable from configuration.
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["Endpoint"] = TestData.EndpointText,
                ["ApiKey"] = TestData.ApiKey,
                ["Models:0:Name"] = TestData.Deployment,
                ["Models:1:Name"] = "",
            })
            .Build();

        using var provider = Build(builder => builder.UseAzureOpenAI(configuration));

        Should.Throw<OptionsValidationException>(
                () => provider.GetRequiredService<IOptions<AzureOpenAIProviderOptions>>().Value)
            .Message.ShouldContain($"{nameof(AzureOpenAIProviderOptions.Models)}[1]");
    }

    [Fact]
    public void Registration_without_credentials_fails_at_startup()
    {
        var services = new ServiceCollection();
        services.AddTracon().UseAzureOpenAI(options => options.Endpoint = TestData.Endpoint);

        using var provider = services.BuildServiceProvider();

        var exception = Should.Throw<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<AzureOpenAIProviderOptions>>().Value);

        exception.Message.ShouldContain(nameof(AzureOpenAIProviderOptions.ApiKey));
        exception.Message.ShouldContain(nameof(AzureOpenAIProviderOptions.CredentialFactory));
    }

    [Fact]
    public void Credential_factory_is_sufficient_instead_of_a_key()
    {
        // On the managed-identity path there is NO API key at all;
        // validation must not treat this as a missing setting.
        var services = new ServiceCollection();
        services.AddTracon().UseAzureOpenAI(options =>
        {
            options.Endpoint = TestData.Endpoint;
            options.CredentialFactory = static () => new FakeTokenCredential();
        });

        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<AzureOpenAIProviderOptions>>().Value;

        options.ApiKey.ShouldBeNull();
        provider.GetServices<IModelProvider>().Single().Name.ShouldBe(AzureOpenAIProviderNames.AzureOpenAI);
    }

    [Fact]
    public void Empty_key_argument_is_rejected()
    {
        var services = new ServiceCollection();

        Should.Throw<ArgumentException>(
            () => services.AddTracon().UseAzureOpenAI(TestData.Endpoint, "   "));
    }

    [Fact]
    public void Catalog_comes_from_configuration_and_is_reflected_in_the_provider()
    {
        using var provider = Build(builder => builder.UseAzureOpenAI(TestData.Endpoint, TestData.ApiKey, options =>
        {
            options.Models.Add(new ModelDescriptor { Name = "test-gpt" });
            options.Models.Add(new ModelDescriptor { Name = TestData.Deployment });
        }));

        var descriptor = provider.GetRequiredService<IModelProviderRegistry>().List().Single();

        descriptor.Name.ShouldBe(AzureOpenAIProviderNames.AzureOpenAI);

        // AzureOpenAIModelCatalog.Build sorts by name (ordinal); "production-gpt" < "test-gpt".
        descriptor.Models.Select(static m => m.Name).ShouldBe([TestData.Deployment, "test-gpt"]);
    }

    private static IConfiguration Configuration()
        => new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["Endpoint"] = TestData.EndpointText,
                ["ApiKey"] = TestData.ApiKey,
                ["DefaultDeployment"] = TestData.Deployment,
                ["Audience"] = "https://cognitiveservices.azure.us/.default",
                ["Timeout"] = "00:00:30",
                ["Models:0:Name"] = TestData.Deployment,
                ["Models:0:ContextWindowTokens"] = "128000",
                ["Models:0:InputCostPerMillionTokens"] = "0.15",
            })
            .Build();

    private static ServiceProvider Build(Action<ITraconBuilder> configure)
    {
        var services = new ServiceCollection();
        configure(services.AddTracon());

        return services.BuildServiceProvider();
    }
}
