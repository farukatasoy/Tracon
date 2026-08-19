using AgentPrism.Core.UnitTests.Fakes;
using Microsoft.Extensions.Configuration;

namespace AgentPrism.Core.UnitTests.Tenancy;

/// <summary>Verifies <see cref="TenantProviderCredentialResolver"/> (section 65.2, 65.4).</summary>
public sealed class TenantProviderCredentialResolverTests
{
    [Fact]
    public void Name_outside_the_allowed_prefix_is_rejected()
    {
        var resolver = CreateResolver();

        var exception = Should.Throw<AgentPrismException>(
            () => resolver.ValidatePrefix("ConnectionStrings:Default"));

        exception.Message.ShouldContain("ConnectionStrings:Default");
        exception.Message.ShouldContain("AgentPrism:ProviderKeys:");
    }

    [Fact]
    public void Empty_name_is_rejected()
        => Should.Throw<AgentPrismException>(() => CreateResolver().ValidatePrefix(""));

    [Fact]
    public void Name_under_the_prefix_is_accepted()
        => Should.NotThrow(() => CreateResolver().ValidatePrefix("AgentPrism:ProviderKeys:Acme:OpenAI"));

    [Fact]
    public void A_custom_prefix_is_honored()
    {
        var resolver = CreateResolver(
            configuration: BuildConfiguration(),
            options: new AgentPrismTenantProviderOptions { AllowedConfigurationPrefix = "Custom:Prefix:" });

        Should.Throw<AgentPrismException>(() => resolver.ValidatePrefix("AgentPrism:ProviderKeys:Acme:OpenAI"));
        Should.NotThrow(() => resolver.ValidatePrefix("Custom:Prefix:Acme:OpenAI"));
    }

    [Fact]
    public void Resolve_returns_the_configured_value()
    {
        var resolver = CreateResolver(BuildConfiguration(("AgentPrism:ProviderKeys:Acme:OpenAI", "sk-test")));

        var credential = resolver.Resolve(Binding("AgentPrism:ProviderKeys:Acme:OpenAI"));

        credential.ShouldNotBeNull();
        credential.ApiKey.ShouldBe("sk-test");
    }

    [Fact]
    public void Resolve_returns_null_when_the_key_has_no_value()
    {
        var resolver = CreateResolver();

        resolver.Resolve(Binding("AgentPrism:ProviderKeys:Acme:OpenAI")).ShouldBeNull();
    }

    [Fact]
    public void Resolve_carries_the_bindings_endpoint_through()
    {
        var resolver = CreateResolver(BuildConfiguration(("AgentPrism:ProviderKeys:Acme:OpenAI", "sk-test")));

        var credential = resolver.Resolve(Binding("AgentPrism:ProviderKeys:Acme:OpenAI", "https://proxy.example.com/"));

        credential.ShouldNotBeNull();
        credential.Endpoint.ShouldBe("https://proxy.example.com/");
    }

    [Fact]
    public void Resolve_rejects_an_out_of_prefix_binding_even_if_it_was_saved_before_the_prefix_changed()
    {
        // Defense in two layers (section 65.2): the resolver re-checks the
        // prefix, it does not trust that the write path already validated it.
        var resolver = CreateResolver(BuildConfiguration(("Legacy:Key", "sk-test")));

        Should.Throw<AgentPrismException>(() => resolver.Resolve(Binding("Legacy:Key")));
    }

    [Fact]
    public void No_configuration_registered_resolves_to_null()
    {
        var resolver = new TenantProviderCredentialResolver(
            configuration: null,
            new StaticOptionsMonitor<AgentPrismTenantProviderOptions>(new AgentPrismTenantProviderOptions()));

        resolver.Resolve(Binding("AgentPrism:ProviderKeys:Acme:OpenAI")).ShouldBeNull();
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
        AgentPrismTenantProviderOptions? options = null)
        => new(
            configuration ?? BuildConfiguration(),
            new StaticOptionsMonitor<AgentPrismTenantProviderOptions>(options ?? new AgentPrismTenantProviderOptions()));

    private static IConfiguration BuildConfiguration(params (string Key, string Value)[] values)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(static pair => new KeyValuePair<string, string?>(pair.Key, pair.Value)))
            .Build();
}
