using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Tracon.ProviderCore.Tests;

/// <summary>
/// The shared model provider state and rules. Linked into the four
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
    public void Without_a_clock_the_provider_waits_on_the_system_clock()
        => new ModelProviderCore<object>([], egressGuard: null).Clock.ShouldBeSameAs(TimeProvider.System);

    [Fact]
    public void The_clock_the_host_registered_is_the_one_the_provider_hands_to_its_clients()
    {
        var clock = Substitute.For<TimeProvider>();

        new ModelProviderCore<object>([], egressGuard: null, clock).Clock.ShouldBeSameAs(clock);
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
    public void Tenant_factory_is_the_one_the_tenant_chat_client_is_built_from()
    {
        var core = new ModelProviderCore<object>([], null);
        var credential = new ModelProviderCredential { ApiKey = "tenant-key" };
        var factoryBuilds = 0;

        object Build(ModelProviderCredential _)
        {
            factoryBuilds++;
            return new object();
        }

        var factory = core.GetTenantFactory(credential, Build);
        object? seenByChat = null;

        // A capability beside chat asks for the factory first; chat must then reuse it, not build a second.
        _ = core.GetTenantChatClient(
            credential,
            Binding,
            Build,
            (chatFactory, _) =>
            {
                seenByChat = chatFactory;
                return Substitute.For<IChatClient>();
            });

        seenByChat.ShouldBeSameAs(factory);
        core.GetTenantFactory(new ModelProviderCredential { ApiKey = "tenant-key" }, Build).ShouldBeSameAs(factory);
        core.GetTenantFactory(new ModelProviderCredential { ApiKey = "other-key" }, Build).ShouldNotBeSameAs(factory);
        factoryBuilds.ShouldBe(2);
    }

    [Fact]
    public void A_rotated_key_replaces_every_client_of_its_binding_and_leaves_one_entry()
    {
        var core = new ModelProviderCore<object>([], null);
        var factoryBuilds = 0;

        IChatClient Resolve(string key, string model) => core.GetTenantChatClient(
            Bound("tenant-a", key),
            Binding with { Model = model },
            _ =>
            {
                factoryBuilds++;
                return new object();
            },
            static (_, _) => new TrackedChatClient());

        var firstA = Resolve("key-one", "model-a");
        var firstB = Resolve("key-one", "model-b");
        var secondA = Resolve("key-two", "model-a");
        var secondB = Resolve("key-two", "model-b");
        var thirdA = Resolve("key-three", "model-a");

        // The key is the thing that changed, so every client built with the old one goes, not only the one asked for.
        secondA.ShouldNotBeSameAs(firstA);
        secondB.ShouldNotBeSameAs(firstB);
        thirdA.ShouldNotBeSameAs(secondA);
        core.CachedBindingCount.ShouldBe(1);
        factoryBuilds.ShouldBe(3);

        // The clients of the current key stay, and the ones that were replaced are left to the collector.
        Resolve("key-three", "model-a").ShouldBeSameAs(thirdA);
        ((TrackedChatClient)firstA).Disposed.ShouldBeFalse();
        ((TrackedChatClient)secondB).Disposed.ShouldBeFalse();
    }

    [Fact]
    public void The_chat_client_a_rotation_replaces_leaves_memory_with_the_old_key()
    {
        var core = new ModelProviderCore<object>([], null);

        var (client, key) = ResolveWithKey(core, "tenant-a");

        core.GetTenantChatClient(
            Bound("tenant-a", "key-after-rotation"),
            Binding,
            static _ => new object(),
            static (_, _) => new TrackedChatClient());

        for (var pass = 0; pass < 3; pass++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        client.IsAlive.ShouldBeFalse("the replaced chat client is still reachable from the provider");
        key.IsAlive.ShouldBeFalse("the old key value is still reachable from the provider");
    }

    [Fact]
    public void Two_tenants_with_the_same_key_value_are_given_their_own_clients()
    {
        var core = new ModelProviderCore<object>([], null);

        IChatClient Resolve(string tenant, string key) => core.GetTenantChatClient(
            Bound(tenant, key),
            Binding,
            static _ => new object(),
            static (_, _) => new TrackedChatClient());

        var a = Resolve("tenant-a", "shared-key");
        var b = Resolve("tenant-b", "shared-key");

        b.ShouldNotBeSameAs(a);
        core.CachedBindingCount.ShouldBe(2);

        Resolve("tenant-a", "rotated-key");
        Resolve("tenant-b", "shared-key").ShouldBeSameAs(b);
    }

    private static ModelProviderCredential Bound(string tenant, string apiKey)
        => TenantCredentialScope.Attach(new ModelProviderCredential { ApiKey = apiKey }, tenant, "shared-test");

    // NoInlining keeps the client and the key off the caller's stack, so the collection can see them go.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WeakReference Client, WeakReference Key) ResolveWithKey(ModelProviderCore<object> core, string tenant)
    {
        var key = new string('k', 24) + Guid.NewGuid().ToString("N");
        var client = core.GetTenantChatClient(
            Bound(tenant, key),
            Binding,
            static _ => new object(),
            static (_, _) => new TrackedChatClient());

        return (new WeakReference(client), new WeakReference(key));
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

    private sealed class TrackedChatClient : IChatClient
    {
        public bool Disposed { get; private set; }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() => Disposed = true;
    }
}
