using Tracon.Core.UnitTests.Fakes;
using Microsoft.Extensions.Configuration;

namespace Tracon.Core.UnitTests.Tenancy;

/// <summary>Verifies <see cref="TenantProviderCredentialResolver"/> (section 65.2, 65.4).</summary>
public sealed class TenantProviderCredentialResolverTests
{
    [Fact]
    public void Name_outside_the_allowed_prefix_is_rejected()
    {
        var resolver = CreateResolver();

        var exception = Should.Throw<TraconException>(
            () => resolver.ValidatePrefix("ConnectionStrings:Default"));

        exception.Message.ShouldContain("ConnectionStrings:Default");
        exception.Message.ShouldContain("Tracon:ProviderKeys:");
    }

    [Fact]
    public void Empty_name_is_rejected()
        => Should.Throw<TraconException>(() => CreateResolver().ValidatePrefix(""));

    [Fact]
    public void Name_under_the_prefix_is_accepted()
        => Should.NotThrow(() => CreateResolver().ValidatePrefix("Tracon:ProviderKeys:Acme:OpenAI"));

    [Fact]
    public void A_custom_prefix_is_honored()
    {
        var resolver = CreateResolver(
            configuration: BuildConfiguration(),
            options: new TraconTenantProviderOptions { AllowedConfigurationPrefix = "Custom:Prefix:" });

        Should.Throw<TraconException>(() => resolver.ValidatePrefix("Tracon:ProviderKeys:Acme:OpenAI"));
        Should.NotThrow(() => resolver.ValidatePrefix("Custom:Prefix:Acme:OpenAI"));
    }

    [Fact]
    public void Resolve_returns_the_configured_value()
    {
        var resolver = CreateResolver(BuildConfiguration(("Tracon:ProviderKeys:Acme:OpenAI", "sk-test")));

        var credential = resolver.Resolve(Binding("Tracon:ProviderKeys:Acme:OpenAI"));

        credential.ShouldNotBeNull();
        credential.ApiKey.ShouldBe("sk-test");
    }

    [Fact]
    public void Resolve_returns_null_when_the_key_has_no_value()
    {
        var resolver = CreateResolver();

        resolver.Resolve(Binding("Tracon:ProviderKeys:Acme:OpenAI")).ShouldBeNull();
    }

    [Fact]
    public void Resolve_carries_the_bindings_endpoint_through()
    {
        var resolver = CreateResolver(BuildConfiguration(("Tracon:ProviderKeys:Acme:OpenAI", "sk-test")));

        var credential = resolver.Resolve(Binding("Tracon:ProviderKeys:Acme:OpenAI", "https://proxy.example.com/"));

        credential.ShouldNotBeNull();
        credential.Endpoint.ShouldBe("https://proxy.example.com/");
    }

    [Fact]
    public void Resolve_rejects_an_out_of_prefix_binding_even_if_it_was_saved_before_the_prefix_changed()
    {
        // Defense in two layers (section 65.2): the resolver re-checks the
        // prefix, it does not trust that the write path already validated it.
        var resolver = CreateResolver(BuildConfiguration(("Legacy:Key", "sk-test")));

        Should.Throw<TraconException>(() => resolver.Resolve(Binding("Legacy:Key")));
    }

    [Fact]
    public void No_configuration_registered_resolves_to_null()
    {
        var resolver = new TenantProviderCredentialResolver(
            configuration: null,
            new StaticOptionsMonitor<TraconTenantProviderOptions>(new TraconTenantProviderOptions()));

        resolver.Resolve(Binding("Tracon:ProviderKeys:Acme:OpenAI")).ShouldBeNull();
    }

    private static TenantProviderBinding Binding(string configurationName, string? endpoint = null)
        => new()
        {
            TenantId = "acme",
            ProviderName = "openai",
            ApiKeyConfigurationName = configurationName,
            Endpoint = endpoint,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

    private static TenantProviderCredentialResolver CreateResolver(
        IConfiguration? configuration = null,
        TraconTenantProviderOptions? options = null)
        => new(
            configuration ?? BuildConfiguration(),
            new StaticOptionsMonitor<TraconTenantProviderOptions>(options ?? new TraconTenantProviderOptions()));

    private static IConfiguration BuildConfiguration(params (string Key, string Value)[] values)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(static pair => new KeyValuePair<string, string?>(pair.Key, pair.Value)))
            .Build();
}
