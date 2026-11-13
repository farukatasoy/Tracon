using System.Runtime.CompilerServices;

namespace Tracon.Core.UnitTests.Models;

/// <summary>Verifies <see cref="ProviderCredentialClientCache{TFactory}"/> (BYOK).</summary>
/// <remarks>
/// A client is kept under the tenant binding it serves, and the credential is what the cache compares under that name:
/// a rotated key replaces the entry of its binding instead of adding a second one beside it, and the key never becomes
/// part of a dictionary key. A credential that does not name a binding is kept by its value.
/// </remarks>
public sealed class ProviderCredentialClientCacheTests
{
    private const string Provider = "openai";

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

    [Fact]
    public void A_key_and_an_endpoint_that_join_to_the_same_text_are_two_credentials()
    {
        var cache = new ProviderCredentialClientCache<object>();

        var first = cache.GetOrAdd(new ModelProviderCredential { ApiKey = "sk|https://a/" }, static _ => new object());
        var second = cache.GetOrAdd(new ModelProviderCredential { ApiKey = "sk", Endpoint = "|https://a/" }, static _ => new object());
        var third = cache.GetOrAdd(new ModelProviderCredential { ApiKey = "sk|https://a/", Endpoint = string.Empty }, static _ => new object());

        second.ShouldNotBeSameAs(first);
        third.ShouldBeSameAs(first);
    }

    [Fact]
    public void Credentials_that_name_no_binding_are_kept_by_value_and_never_replace_each_other()
    {
        var cache = new ProviderCredentialClientCache<object>();
        var a = new ModelProviderCredential { ApiKey = "sk-a" };
        var b = new ModelProviderCredential { ApiKey = "sk-b" };

        var first = cache.GetOrAdd(a, static _ => new object());
        cache.GetOrAdd(b, static _ => new object());

        // Two callers that alternate between two keys of no binding must keep getting their own client back.
        cache.GetOrAdd(a, static _ => new object()).ShouldBeSameAs(first);
        cache.Count.ShouldBe(2);
    }

    [Fact]
    public void A_rotated_key_replaces_the_entry_of_its_binding_and_leaves_one_entry()
    {
        var cache = new ProviderCredentialClientCache<TrackedFactory>();
        var builds = 0;

        TrackedFactory Build(ModelProviderCredential credential)
        {
            builds++;

            return TrackedFactory.From(credential);
        }

        var first = cache.GetOrAdd(Bound("tenant-a", "sk-one"), Build);
        var second = cache.GetOrAdd(Bound("tenant-a", "sk-two"), Build);
        var third = cache.GetOrAdd(Bound("tenant-a", "sk-three"), Build);

        cache.Count.ShouldBe(1);
        new[] { first, second, third }.Distinct().Count().ShouldBe(3);
        third.ApiKey.ShouldBe("sk-three");

        // The entry that stays is the current one, and asking again for the same key builds nothing.
        cache.GetOrAdd(Bound("tenant-a", "sk-three"), Build).ShouldBeSameAs(third);
        builds.ShouldBe(3);
    }

    [Fact]
    public void A_changed_endpoint_replaces_the_entry_of_its_binding_as_a_changed_key_does()
    {
        var cache = new ProviderCredentialClientCache<TrackedFactory>();

        var first = cache.GetOrAdd(Bound("tenant-a", "sk-one", "https://a.example.com/"), TrackedFactory.From);
        var second = cache.GetOrAdd(Bound("tenant-a", "sk-one", "https://b.example.com/"), TrackedFactory.From);

        cache.Count.ShouldBe(1);
        second.ShouldNotBeSameAs(first);
        second.Endpoint.ShouldBe("https://b.example.com/");
    }

    [Fact]
    public void The_factory_a_rotation_replaces_is_not_disposed_because_a_call_in_flight_may_still_use_it()
    {
        var cache = new ProviderCredentialClientCache<TrackedFactory>();

        var first = cache.GetOrAdd(Bound("tenant-a", "sk-one"), TrackedFactory.From);
        cache.GetOrAdd(Bound("tenant-a", "sk-two"), TrackedFactory.From);

        first.Disposed.ShouldBeFalse();
    }

    [Fact]
    public void The_replaced_factory_and_the_old_key_leave_memory()
    {
        var cache = new ProviderCredentialClientCache<TrackedFactory>();

        var (factory, key) = Build(cache, "tenant-a");

        // The old key is held by nothing the test keeps: the cache holds neither the factory nor the value.
        cache.GetOrAdd(Bound("tenant-a", "sk-rotated"), TrackedFactory.From);

        CollectGarbage();

        factory.IsAlive.ShouldBeFalse("the replaced factory is still reachable from the cache");
        key.IsAlive.ShouldBeFalse("the old key value is still reachable from the cache");
    }

    [Fact]
    public void The_cache_never_keys_on_the_credential_value()
    {
        const string Secret = "canary-cache-key-value-3b7d09e1";
        var cache = new ProviderCredentialClientCache<object>();

        cache.GetOrAdd(Bound("tenant-a", Secret, "https://endpoint.example.com/"), static _ => new object());
        cache.GetOrAdd(new ModelProviderCredential { ApiKey = Secret + "-unbound", Endpoint = "https://endpoint.example.com/" }, static _ => new object());

        cache.Keys.Count.ShouldBe(2);
        cache.Keys.ShouldAllBe(key => !key.Contains(Secret, StringComparison.Ordinal));
    }

    [Fact]
    public void Two_tenants_that_hold_the_same_key_value_get_their_own_entries()
    {
        var cache = new ProviderCredentialClientCache<TrackedFactory>();

        var a = cache.GetOrAdd(Bound("tenant-a", "sk-shared"), TrackedFactory.From);
        var b = cache.GetOrAdd(Bound("tenant-b", "sk-shared"), TrackedFactory.From);

        cache.Count.ShouldBe(2);
        b.ShouldNotBeSameAs(a);

        // A rotation of one tenant's key does not touch the other tenant's entry.
        cache.GetOrAdd(Bound("tenant-a", "sk-rotated"), TrackedFactory.From);
        cache.GetOrAdd(Bound("tenant-b", "sk-shared"), TrackedFactory.From).ShouldBeSameAs(b);
        cache.Count.ShouldBe(2);
    }

    [Fact]
    public void A_build_that_fails_leaves_the_entry_in_service()
    {
        var cache = new ProviderCredentialClientCache<TrackedFactory>();
        var current = cache.GetOrAdd(Bound("tenant-a", "sk-one"), TrackedFactory.From);

        Should.Throw<InvalidOperationException>(
            () => cache.GetOrAdd(Bound("tenant-a", "sk-two"), static _ => throw new InvalidOperationException("build failed")));

        cache.GetOrAdd(Bound("tenant-a", "sk-one"), TrackedFactory.From).ShouldBeSameAs(current);
        cache.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Two_threads_that_rotate_one_binding_leave_one_entry_and_each_gets_the_factory_of_its_own_key()
    {
        var cache = new ProviderCredentialClientCache<TrackedFactory>();
        using var start = new ManualResetEventSlim();
        var credentials = new[] { Bound("tenant-a", "sk-left"), Bound("tenant-a", "sk-right") };

        var callers = Enumerable.Range(0, 8)
            .Select(index => Task.Run(
                () =>
                {
                    var credential = credentials[index % 2];
                    start.Wait();
                    var wrong = 0;

                    for (var round = 0; round < 200; round++)
                    {
                        // A caller that holds a key is never handed the client of another key, whatever the other callers do.
                        if (!string.Equals(cache.GetOrAdd(credential, TrackedFactory.From).ApiKey, credential.ApiKey, StringComparison.Ordinal))
                        {
                            wrong++;
                        }
                    }

                    return wrong;
                },
                TestContext.Current.CancellationToken))
            .ToArray();

        start.Set();

        (await Task.WhenAll(callers)).ShouldAllBe(static wrong => wrong == 0);
        cache.Count.ShouldBe(1);
        cache.Keys.ShouldHaveSingleItem();

        // After the dust settles the entry serves whichever key asks last, and then stays.
        var settled = cache.GetOrAdd(credentials[0], TrackedFactory.From);
        settled.ApiKey.ShouldBe("sk-left");
        cache.GetOrAdd(credentials[0], TrackedFactory.From).ShouldBeSameAs(settled);
    }

    [Fact]
    public async Task Callers_of_one_unchanged_credential_share_one_factory_under_concurrency()
    {
        var cache = new ProviderCredentialClientCache<TrackedFactory>();
        using var start = new ManualResetEventSlim();
        var credential = Bound("tenant-a", "sk-same");
        var builds = 0;

        var callers = Enumerable.Range(0, 16)
            .Select(_ => Task.Run(
                () =>
                {
                    start.Wait();

                    return cache.GetOrAdd(
                        credential,
                        built =>
                        {
                            Interlocked.Increment(ref builds);

                            return TrackedFactory.From(built);
                        });
                },
                TestContext.Current.CancellationToken))
            .ToArray();

        start.Set();
        var factories = await Task.WhenAll(callers);

        factories.Distinct().Count().ShouldBe(1);
        cache.Count.ShouldBe(1);
        builds.ShouldBe(1);
    }

    [Fact]
    public void The_binding_name_is_the_tenant_and_the_provider_and_two_bindings_never_share_one()
    {
        TenantCredentialScope.For("tenant-a", Provider).ShouldBe("tenant-a/openai");

        var names = new[]
        {
            TenantCredentialScope.For("tenant-a", Provider),
            TenantCredentialScope.For("tenant-b", Provider),
            TenantCredentialScope.For("tenant-a", "anthropic"),
            TenantCredentialScope.For("tenant.a", Provider),
        };

        names.Distinct(StringComparer.Ordinal).Count().ShouldBe(names.Length);
    }

    private static ModelProviderCredential Bound(string tenant, string apiKey, string? endpoint = null)
        => TenantCredentialScope.Attach(new ModelProviderCredential { ApiKey = apiKey, Endpoint = endpoint }, tenant, Provider);

    // NoInlining keeps the key and the factory off the caller's stack, so the collection below can see them go.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WeakReference Factory, WeakReference Key) Build(ProviderCredentialClientCache<TrackedFactory> cache, string tenant)
    {
        var key = new string('k', 24) + Guid.NewGuid().ToString("N");
        var factory = cache.GetOrAdd(Bound(tenant, key), TrackedFactory.From);

        return (new WeakReference(factory), new WeakReference(key));
    }

    private static void CollectGarbage()
    {
        for (var pass = 0; pass < 3; pass++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
    }

    private sealed class TrackedFactory : IDisposable
    {
        private TrackedFactory(ModelProviderCredential credential)
        {
            ApiKey = credential.ApiKey;
            Endpoint = credential.Endpoint;
        }

        public string ApiKey { get; }

        public string? Endpoint { get; }

        public bool Disposed { get; private set; }

        public static TrackedFactory From(ModelProviderCredential credential) => new(credential);

        public void Dispose() => Disposed = true;
    }
}
