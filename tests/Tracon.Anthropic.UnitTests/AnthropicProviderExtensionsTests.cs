using Tracon.Anthropic.UnitTests.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tracon.Anthropic.UnitTests;

/// <summary>
/// The shape of the <c>UseAnthropic()</c> registration, its behavior when repeated,
/// and binding from configuration.
/// </summary>
public sealed class AnthropicProviderExtensionsTests
{
    [Fact]
    public void Registers_a_single_provider()
    {
        using var provider = Build(builder => builder.UseAnthropic(TestData.ApiKey));

        var names = provider.GetServices<IModelProvider>().Select(static p => p.Name).ToList();

        names.ShouldBe([AnthropicProviderNames.Anthropic]);
    }

    [Fact]
    public void Second_call_does_not_duplicate_the_provider()
    {
        using var provider = Build(builder => builder
            .UseAnthropic(TestData.ApiKey)
            .UseAnthropic(options => options.DefaultModel = TestData.Model));

        provider.GetServices<IModelProvider>().Count().ShouldBe(1);

        // A second call merges settings; the registry does not raise a "same name twice" error.
        provider.GetRequiredService<IOptions<AnthropicProviderOptions>>().Value.DefaultModel
            .ShouldBe(TestData.Model);
        provider.GetRequiredService<IModelProviderRegistry>().List().Count.ShouldBe(1);
    }

    [Fact]
    public void A_single_factory_is_shared()
    {
        using var provider = Build(builder => builder.UseAnthropic(TestData.ApiKey));

        // One client, one HTTP connection pool.
        provider.GetRequiredService<AnthropicChatClientFactory>()
            .ShouldBeSameAs(provider.GetRequiredService<AnthropicChatClientFactory>());
    }

    [Fact]
    public void Reads_from_configuration()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["ApiKey"] = TestData.ApiKey,
                ["DefaultModel"] = TestData.Model,
                ["Endpoint"] = "https://example.gateway/v1",
                ["DefaultMaxOutputTokens"] = "8192",
                ["MaxRetries"] = "0",
                ["Timeout"] = "00:00:30",
                ["Models:0:Name"] = TestData.Model,
                ["Models:0:ContextWindowTokens"] = "200000",
                ["Models:0:InputCostPerMillionTokens"] = "3",
            })
            .Build();

        using var provider = Build(builder => builder.UseAnthropic(configuration));

        var options = provider.GetRequiredService<IOptions<AnthropicProviderOptions>>().Value;

        options.ApiKey.ShouldBe(TestData.ApiKey);
        options.DefaultModel.ShouldBe(TestData.Model);
        options.Endpoint.ShouldBe(new Uri("https://example.gateway/v1"));
        options.DefaultMaxOutputTokens.ShouldBe(8192);
        options.MaxRetries.ShouldBe(0);
        options.Timeout.ShouldBe(TimeSpan.FromSeconds(30));
        options.Models.Single().ContextWindowTokens.ShouldBe(200000);
        options.Models.Single().InputCostPerMillionTokens.ShouldBe(3);
    }

    [Fact]
    public void Relative_address_from_configuration_is_rejected()
    {
        // HATA-S3-003: Bind() used to leave Endpoint unassigned when
        // Uri.TryCreate(..., UriKind.Absolute, ...) failed; the value was silently
        // dropped before it ever reached the validator.
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["ApiKey"] = TestData.ApiKey,
                ["Endpoint"] = "just-a-path",
            })
            .Build();

        using var provider = Build(builder => builder.UseAnthropic(configuration));

        Should.Throw<OptionsValidationException>(
                () => provider.GetRequiredService<IOptions<AnthropicProviderOptions>>().Value)
            .Message.ShouldContain(nameof(AnthropicProviderOptions.Endpoint));
    }

    [Fact]
    public void Nameless_model_from_configuration_is_rejected()
    {
        // HATA-S3-004: BindModels() used to never add an entry with an empty Name
        // to the list; the validator never saw an empty-named entry.
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["ApiKey"] = TestData.ApiKey,
                ["Models:0:Name"] = TestData.Model,
                ["Models:1:Name"] = "",
            })
            .Build();

        using var provider = Build(builder => builder.UseAnthropic(configuration));

        Should.Throw<OptionsValidationException>(
                () => provider.GetRequiredService<IOptions<AnthropicProviderOptions>>().Value)
            .Message.ShouldContain(nameof(AnthropicProviderOptions.Models));
    }

    [Fact]
    public void Registration_without_a_key_fails_at_startup()
    {
        var services = new ServiceCollection();
        services.AddTracon().UseAnthropic(options => options.DefaultModel = TestData.Model);

        using var provider = services.BuildServiceProvider();

        var exception = Should.Throw<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<AnthropicProviderOptions>>().Value);

        exception.Message.ShouldContain(nameof(AnthropicProviderOptions.ApiKey));
    }

    [Fact]
    public void Empty_key_argument_is_rejected()
    {
        var services = new ServiceCollection();

        Should.Throw<ArgumentException>(() => services.AddTracon().UseAnthropic("   "));
    }

    [Fact]
    public void Catalog_comes_from_configuration_and_is_reflected_in_the_provider()
    {
        using var provider = Build(builder => builder.UseAnthropic(TestData.ApiKey, options =>
        {
            options.Models.Add(new ModelDescriptor { Name = "claude-opus-5" });
            options.Models.Add(new ModelDescriptor { Name = TestData.Model });
        }));

        var descriptor = provider.GetRequiredService<IModelProviderRegistry>().List().Single();

        descriptor.Name.ShouldBe(AnthropicProviderNames.Anthropic);
        descriptor.Models.Select(static m => m.Name).ShouldBe(["claude-opus-5", TestData.Model]);
    }

    private static ServiceProvider Build(Action<ITraconBuilder> configure)
    {
        var services = new ServiceCollection();
        configure(services.AddTracon());

        return services.BuildServiceProvider();
    }
}
