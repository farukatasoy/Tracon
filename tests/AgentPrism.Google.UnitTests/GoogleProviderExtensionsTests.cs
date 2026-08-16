using AgentPrism.Google.UnitTests.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AgentPrism.Google.UnitTests;

/// <summary>
/// The shape of the <c>UseGoogle()</c> registration, its idempotency, and binding from configuration.
/// </summary>
public sealed class GoogleProviderExtensionsTests
{
    [Fact]
    public void Registers_a_single_provider()
    {
        using var provider = Build(builder => builder.UseGoogle(TestData.ApiKey));

        provider.GetServices<IModelProvider>().Select(static p => p.Name)
            .ShouldBe([GoogleProviderNames.Google]);
    }

    [Fact]
    public void Provider_name_is_google_not_gemini()
    {
        // The name is stable: agent definitions are stored in the database with this name.
        GoogleProviderNames.Google.ShouldBe("google");
    }

    [Fact]
    public void Second_call_does_not_duplicate_the_provider()
    {
        using var provider = Build(builder => builder
            .UseGoogle(TestData.ApiKey)
            .UseGoogle(options => options.DefaultModel = TestData.Model));

        provider.GetServices<IModelProvider>().Count().ShouldBe(1);
        provider.GetRequiredService<IOptions<GoogleProviderOptions>>().Value.DefaultModel
            .ShouldBe(TestData.Model);
        provider.GetRequiredService<IModelProviderRegistry>().List().Count.ShouldBe(1);
    }

    [Fact]
    public void Single_factory_is_shared()
    {
        using var provider = Build(builder => builder.UseGoogle(TestData.ApiKey));

        provider.GetRequiredService<GoogleChatClientFactory>()
            .ShouldBeSameAs(provider.GetRequiredService<GoogleChatClientFactory>());
    }

    [Fact]
    public void Reads_from_configuration()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["ApiKey"] = TestData.ApiKey,
                ["DefaultModel"] = TestData.Model,
                ["Endpoint"] = "https://example.test",
                ["ApiVersion"] = "v1beta",
                ["Timeout"] = "00:00:30",
                ["Models:0:Name"] = TestData.Model,
                ["Models:0:ContextWindowTokens"] = "1048576",
                ["Models:0:SupportsReasoning"] = "true",
            })
            .Build();

        using var provider = Build(builder => builder.UseGoogle(configuration));

        var options = provider.GetRequiredService<IOptions<GoogleProviderOptions>>().Value;

        options.ApiKey.ShouldBe(TestData.ApiKey);
        options.DefaultModel.ShouldBe(TestData.Model);
        options.Endpoint.ShouldBe(new Uri("https://example.test"));
        options.ApiVersion.ShouldBe("v1beta");
        options.Timeout.ShouldBe(TimeSpan.FromSeconds(30));
        options.Models.Single().ContextWindowTokens.ShouldBe(1048576);
        options.Models.Single().SupportsReasoning.ShouldBeTrue();
    }

    [Fact]
    public void Relative_address_from_configuration_is_rejected()
    {
        // HATA-S3-003: Bind() used to leave Endpoint unassigned whenever
        // Uri.TryCreate(..., UriKind.Absolute, ...) failed; the value was silently
        // dropped before it ever reached the validator.
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["ApiKey"] = TestData.ApiKey,
                ["Endpoint"] = "just-a-path",
            })
            .Build();

        using var provider = Build(builder => builder.UseGoogle(configuration));

        Should.Throw<OptionsValidationException>(
                () => provider.GetRequiredService<IOptions<GoogleProviderOptions>>().Value)
            .Message.ShouldContain(nameof(GoogleProviderOptions.Endpoint));
    }

    [Fact]
    public void Unnamed_model_from_configuration_is_rejected()
    {
        // HATA-S3-004: BindModels() used to never add an entry with an empty Name
        // to the list; the validator never saw an empty-named entry at all.
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["ApiKey"] = TestData.ApiKey,
                ["Models:0:Name"] = TestData.Model,
                ["Models:1:Name"] = "",
            })
            .Build();

        using var provider = Build(builder => builder.UseGoogle(configuration));

        Should.Throw<OptionsValidationException>(
                () => provider.GetRequiredService<IOptions<GoogleProviderOptions>>().Value)
            .Message.ShouldContain(nameof(GoogleProviderOptions.Models));
    }

    [Fact]
    public void Registration_without_a_key_fails_at_startup()
    {
        var services = new ServiceCollection();
        services.AddAgentPrism().UseGoogle(options => options.DefaultModel = TestData.Model);

        using var provider = services.BuildServiceProvider();

        Should.Throw<OptionsValidationException>(
                () => provider.GetRequiredService<IOptions<GoogleProviderOptions>>().Value)
            .Message.ShouldContain(nameof(GoogleProviderOptions.ApiKey));
    }

    [Fact]
    public void Empty_key_argument_is_rejected()
    {
        var services = new ServiceCollection();

        Should.Throw<ArgumentException>(() => services.AddAgentPrism().UseGoogle("   "));
    }

    [Fact]
    public void Catalog_comes_from_configuration_and_reflects_in_the_provider()
    {
        using var provider = Build(builder => builder.UseGoogle(TestData.ApiKey, options =>
        {
            options.Models.Add(new ModelDescriptor { Name = "gemini-3.6-flash" });
            options.Models.Add(new ModelDescriptor { Name = "gemini-3.1-pro-preview" });
        }));

        var descriptor = provider.GetRequiredService<IModelProviderRegistry>().List().Single();

        descriptor.Name.ShouldBe(GoogleProviderNames.Google);
        descriptor.Models.Select(static m => m.Name).ShouldBe(["gemini-3.1-pro-preview", "gemini-3.6-flash"]);
    }

    private static ServiceProvider Build(Action<IAgentPrismBuilder> configure)
    {
        var services = new ServiceCollection();
        configure(services.AddAgentPrism());

        return services.BuildServiceProvider();
    }
}
