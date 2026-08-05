using AgentPrism.Core.UnitTests.Fakes;

namespace AgentPrism.Core.UnitTests.Retention;

/// <summary>
/// <see cref="RetentionPolicyResolver"/>'in veritabani politikasi ile
/// yapilandirma varsayilanini birlestirme kurallarinin testleri.
/// </summary>
public sealed class RetentionPolicyResolverTests
{
    private const string Tenant = "acme";

    [Fact]
    public async Task Politika_yok_ve_yapilandirma_kapaliysa_hicbir_sey_donmez()
    {
        var resolver = Build(new AgentPrismRetentionOptions { Enabled = false });

        var resolved = await resolver.ResolveAsync(Tenant, RetentionTargets.RunEvents);

        resolved.ShouldBeNull();
    }

    [Fact]
    public async Task Politika_yoksa_yapilandirma_varsayilani_kullanilir()
    {
        var options = new AgentPrismRetentionOptions { Enabled = true };
        var resolver = Build(options);

        var resolved = await resolver.ResolveAsync(Tenant, RetentionTargets.RunEvents);

        resolved.ShouldNotBeNull();
        resolved.Target.ShouldBe(RetentionTargets.RunEvents);
        resolved.MaxAgeDays.ShouldBe(options.RunEvents.MaxAgeDays!.Value);
    }

    [Fact]
    public async Task Kullanici_verisi_hedefleri_yapilandirma_acik_olsa_bile_varsayilan_kapalidir()
    {
        // Sessions/Conversations icin RetentionTargetOptions.MaxAgeDays varsayilan
        // null'dur — Enabled=true tek basina yeterli DEGILDIR, ayrica MaxAgeDays
        // acikca verilmelidir (25.1: kullanici verisi, varsayilan KAPALI).
        var resolver = Build(new AgentPrismRetentionOptions { Enabled = true });

        (await resolver.ResolveAsync(Tenant, RetentionTargets.Sessions)).ShouldBeNull();
        (await resolver.ResolveAsync(Tenant, RetentionTargets.Conversations)).ShouldBeNull();
    }

    [Fact]
    public async Task Veritabani_politikasi_yapilandirmayi_hic_gormeden_kazanir()
    {
        var options = new AgentPrismRetentionOptions { Enabled = true };
        var store = new InMemoryRetentionPolicyStore();
        var now = DateTimeOffset.UtcNow;

        // Veritabaninda ACIKCA kapali bir kayit var; yapilandirma acik olsa
        // bile hicbir sey silinmemelidir.
        await store.SavePolicyAsync(new RetentionPolicy
        {
            Id = Guid.NewGuid(),
            TenantId = Tenant,
            Target = RetentionTargets.RunEvents,
            MaxAgeDays = 999,
            Enabled = false,
            CreatedAt = now,
            UpdatedAt = now,
        });

        var resolver = new RetentionPolicyResolver(store, new StaticOptionsMonitor<AgentPrismRetentionOptions>(options));

        (await resolver.ResolveAsync(Tenant, RetentionTargets.RunEvents)).ShouldBeNull();
    }

    [Fact]
    public async Task Kiraciya_ozel_politika_genel_yildizdan_once_gelir()
    {
        var store = new InMemoryRetentionPolicyStore();
        var now = DateTimeOffset.UtcNow;

        await store.SavePolicyAsync(new RetentionPolicy
        {
            Id = Guid.NewGuid(),
            TenantId = "*",
            Target = RetentionTargets.RunEvents,
            MaxAgeDays = 30,
            Enabled = true,
            CreatedAt = now,
            UpdatedAt = now,
        });

        await store.SavePolicyAsync(new RetentionPolicy
        {
            Id = Guid.NewGuid(),
            TenantId = Tenant,
            Target = RetentionTargets.RunEvents,
            MaxAgeDays = 7,
            Enabled = true,
            CreatedAt = now,
            UpdatedAt = now,
        });

        var resolver = new RetentionPolicyResolver(
            store,
            new StaticOptionsMonitor<AgentPrismRetentionOptions>(new AgentPrismRetentionOptions()));

        var resolved = await resolver.ResolveAsync(Tenant, RetentionTargets.RunEvents);

        resolved.ShouldNotBeNull();
        resolved.MaxAgeDays.ShouldBe(7);
    }

    private static RetentionPolicyResolver Build(AgentPrismRetentionOptions options)
        => new(new InMemoryRetentionPolicyStore(), new StaticOptionsMonitor<AgentPrismRetentionOptions>(options));
}
