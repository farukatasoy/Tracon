using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Tracon.ProviderCore.Tests;

/// <summary>
/// The shared model provider state and rules (phase 181). Linked into the four
/// provider test projects.
/// </summary>
public sealed class ModelProviderCoreTests
{
    private static readonly ModelBinding Binding = new() { Provider = "shared-test", Model = "model-a" };

    [Fact]
    public void No_tenant_endpoint_means_no_guard()
    {
        var core = new ModelProviderCore<object>([], NewGuard());

        core.GuardFor(null).ShouldBeNull();
    }

    [Fact]
    public void Tenant_endpoint_gets_the_guard()
    {
        var guard = NewGuard();
        var core = new ModelProviderCore<object>([], guard);

        core.GuardFor(new Uri("https://tenant.example.test/")).ShouldBeSameAs(guard);
    }

    [Fact]
    public void Tenant_endpoint_without_a_configured_guard_stays_unguarded()
        => new ModelProviderCore<object>([], egressGuard: null)
            .GuardFor(new Uri("https://tenant.example.test/"))
            .ShouldBeNull();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("relative/path")]
    public void Credential_without_a_usable_endpoint_has_no_tenant_endpoint(string? endpoint)
        => ModelProviderCore.TenantEndpoint(new ModelProviderCredential { ApiKey = "k", Endpoint = endpoint })
            .ShouldBeNull();

    [Fact]
    public void Credential_endpoint_is_the_tenant_endpoint()
        => ModelProviderCore.TenantEndpoint(new ModelProviderCredential { ApiKey = "k", Endpoint = "https://tenant.example.test/v1" })
            .ShouldBe(new Uri("https://tenant.example.test/v1"));

    [Fact]
    public void Empty_catalog_never_reports_a_model_as_outside()
        => new ModelProviderCore<object>([], null).IsOutsideCatalog("anything").ShouldBeFalse();

    [Theory]
    [InlineData("model-a", false)]
    [InlineData("MODEL-A", false)]
    [InlineData("model-z", true)]
    [InlineData(null, false)]
    [InlineData("  ", false)]
    public void Catalog_membership_is_case_insensitive(string? model, bool outside)
        => new ModelProviderCore<object>([new ModelDescriptor { Name = "model-a" }], null)
            .IsOutsideCatalog(model)
            .ShouldBe(outside);

    [Fact]
    public void Tenant_client_is_built_once_per_credential_and_binding()
    {
        var core = new ModelProviderCore<object>([], null);
        var credential = new ModelProviderCredential { ApiKey = "tenant-key" };
        var factoryBuilds = 0;
        var clientBuilds = 0;

        IChatClient Resolve(ModelBinding binding) => core.GetTenantChatClient(
            credential,
            binding,
            _ =>
            {
                factoryBuilds++;
                return new object();
            },
            (_, _) =>
            {
                clientBuilds++;
                return Substitute.For<IChatClient>();
            });

        var first = Resolve(Binding);
        var second = Resolve(Binding);
        var otherModel = Resolve(Binding with { Model = "model-b" });

        second.ShouldBeSameAs(first);
        otherModel.ShouldNotBeSameAs(first);
        factoryBuilds.ShouldBe(1);
        clientBuilds.ShouldBe(2);
    }

    [Fact]
    public void Diagnostic_names_the_key_and_never_carries_a_value()
    {
        var diagnostic = ModelProviderCore.ConfigurationDiagnosticFor("Tracon:Providers:Test", resolved: false);

        diagnostic.Key.ShouldBe("Tracon:Providers:Test:ApiKey");
        diagnostic.Resolved.ShouldBeFalse();
        diagnostic.Hint.ShouldBe("dotnet user-secrets set \"Tracon:Providers:Test:ApiKey\" \"<key>\"");
    }

    [Fact]
    public void Resolved_diagnostic_has_no_hint()
        => ModelProviderCore.ConfigurationDiagnosticFor("Tracon:Providers:Test", resolved: true).Hint.ShouldBeNull();

    [Fact]
    public void Diagnostic_alternative_is_appended_to_the_hint()
        => ModelProviderCore.ConfigurationDiagnosticFor("S", resolved: false, alternative: "assign X.Y")
            .Hint.ShouldBe("dotnet user-secrets set \"S:ApiKey\" \"<key>\" or assign X.Y");

    [Fact]
    public void Unknown_health_names_the_provider()
    {
        var health = ModelProviderCore.UnknownHealth("shared-test");

        health.ProviderName.ShouldBe("shared-test");
        health.Status.ShouldBe(ModelProviderHealthStatus.Unknown);
    }

    private static EgressSocketGuard NewGuard()
        => new(Substitute.For<IOptionsMonitor<TraconEgressOptions>>());
}
