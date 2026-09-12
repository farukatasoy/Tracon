namespace Tracon.Core.UnitTests.Models;

/// <summary>Verifies <see cref="ProviderCredentialClientCache{TFactory}"/> (phase 65, BYOK).</summary>
public sealed class ProviderCredentialClientCacheTests
{
    [Fact]
    public void Same_credential_reuses_the_cached_factory()
    {
        var cache = new ProviderCredentialClientCache<object>();
        var credential = new ModelProviderCredential { ApiKey = "sk-test" };
        var buildCount = 0;

        var first = cache.GetOrAdd(credential, _ => { buildCount++; return new object(); });
        var second = cache.GetOrAdd(credential, _ => { buildCount++; return new object(); });

        second.ShouldBeSameAs(first);
        buildCount.ShouldBe(1);
    }

    [Fact]
    public void Equal_but_distinct_credential_instances_reuse_the_same_factory()
    {
        var cache = new ProviderCredentialClientCache<object>();
        var buildCount = 0;

        var first = cache.GetOrAdd(
            new ModelProviderCredential { ApiKey = "sk-test", Endpoint = "https://example.com/" },
            _ => { buildCount++; return new object(); });
        var second = cache.GetOrAdd(
            new ModelProviderCredential { ApiKey = "sk-test", Endpoint = "https://example.com/" },
            _ => { buildCount++; return new object(); });

        second.ShouldBeSameAs(first);
        buildCount.ShouldBe(1);
    }

    [Fact]
    public void Different_api_keys_produce_different_cached_factories()
    {
        var cache = new ProviderCredentialClientCache<object>();

        var first = cache.GetOrAdd(new ModelProviderCredential { ApiKey = "sk-a" }, static _ => new object());
        var second = cache.GetOrAdd(new ModelProviderCredential { ApiKey = "sk-b" }, static _ => new object());

        second.ShouldNotBeSameAs(first);
    }

    [Fact]
    public void Different_endpoints_with_the_same_key_produce_different_cached_factories()
    {
        var cache = new ProviderCredentialClientCache<object>();

        var first = cache.GetOrAdd(
            new ModelProviderCredential { ApiKey = "sk-test", Endpoint = "https://a.example.com/" },
            static _ => new object());
        var second = cache.GetOrAdd(
            new ModelProviderCredential { ApiKey = "sk-test", Endpoint = "https://b.example.com/" },
            static _ => new object());

        second.ShouldNotBeSameAs(first);
    }
}
