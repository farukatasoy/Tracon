using System.Text.Json;

namespace AgentPrism.StoreContracts;

/// <summary>
/// <see cref="IAuditLog"/> sozlesmesinin davranis testleri.
/// </summary>
/// <remarks>
/// Faz 9'da eklendi. Bellek ici defter ile PostgreSQL defteri ayni senaryolari
/// gecmelidir.
/// </remarks>
public abstract class AuditLogContract : IAsyncLifetime
{
    /// <summary>Test edilen defter.</summary>
    protected IAuditLog Log { get; private set; } = null!;

    /// <summary>Test icin bos bir defter uretir.</summary>
    /// <returns>Kullanima hazir defter.</returns>
    protected abstract ValueTask<IAuditLog> CreateLogAsync();

    /// <inheritdoc />
    public async ValueTask InitializeAsync() => Log = await CreateLogAsync();

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await OnDisposeAsync();
        GC.SuppressFinalize(this);
    }

    /// <summary>Turetilmis sinifin kendi kaynaklarini birakmasi icin kanca.</summary>
    /// <returns>Tamamlanma gorevi.</returns>
    protected virtual ValueTask OnDisposeAsync() => default;

    [Fact]
    public async Task Yazilan_kayit_gidip_gelir()
    {
        var entry = Entry(tenantId: "kiraci-a", action: "agent.update", entity: "agent:support");
        await Log.WriteAsync(entry);

        var found = (await Log.QueryAsync(new AuditQuery { TenantId = "kiraci-a" })).ShouldHaveSingleItem();

        found.Id.ShouldBe(entry.Id);
        string.Equals(found.Actor, entry.Actor, StringComparison.Ordinal).ShouldBeTrue();
        found.Action.ShouldBe(entry.Action);
        found.Entity.ShouldBe(entry.Entity);

        // PostgreSQL'in jsonb sutunu bicimlendirmeyi (bosluk) degistirebilir;
        // K-027'nin konusu ancak anahtar SIRASIYLA ilgilidir, burada onemli olan
        // anlamsal esitliktir.
        JsonSemanticallyEquals(found.Before, entry.Before);
        JsonSemanticallyEquals(found.After, entry.After);
    }

    [Fact]
    public async Task Kiracilar_arasi_sizinti_yok()
    {
        await Log.WriteAsync(Entry(tenantId: "kiraci-a", action: "agent.update", entity: "agent:x"));
        await Log.WriteAsync(Entry(tenantId: "kiraci-b", action: "agent.update", entity: "agent:x"));

        var mine = await Log.QueryAsync(new AuditQuery { TenantId = "kiraci-a" });

        mine.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Eylem_ve_varlik_filtresi_calisir()
    {
        const string Tenant = "kiraci-c";

        await Log.WriteAsync(Entry(Tenant, "agent.update", "agent:support"));
        await Log.WriteAsync(Entry(Tenant, "agent.delete", "agent:support"));
        await Log.WriteAsync(Entry(Tenant, "mcp.create", "mcp:github"));

        var byAction = await Log.QueryAsync(new AuditQuery { TenantId = Tenant, Action = "agent.delete" });
        byAction.ShouldHaveSingleItem().Entity.ShouldBe("agent:support");

        var byEntity = await Log.QueryAsync(new AuditQuery { TenantId = Tenant, Entity = "agent:support" });
        byEntity.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Kayitlar_en_yeniden_eskiye_doner()
    {
        const string Tenant = "kiraci-d";
        var start = DateTimeOffset.UtcNow;

        // Bilerek ters sirada yaziliyor: siralamayi defter yapmalidir.
        await Log.WriteAsync(Entry(Tenant, "agent.create", "agent:ikinci") with { CreatedAt = start.AddSeconds(2) });
        await Log.WriteAsync(Entry(Tenant, "agent.create", "agent:birinci") with { CreatedAt = start });

        var entries = await Log.QueryAsync(new AuditQuery { TenantId = Tenant });

        entries.Select(static entry => entry.Entity).ShouldBe(["agent:ikinci", "agent:birinci"]);
    }

    [Fact]
    public async Task Limit_sinirlar()
    {
        const string Tenant = "kiraci-e";

        for (var index = 0; index < 3; index++)
        {
            await Log.WriteAsync(Entry(Tenant, "agent.create", $"agent:{index}"));
        }

        var entries = await Log.QueryAsync(new AuditQuery { TenantId = Tenant, Limit = 2 });

        entries.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Aktoru_bilinmeyen_kayit_null_tasir()
    {
        var entry = Entry("kiraci-f", "session.delete", "session:abc") with { Actor = null };
        await Log.WriteAsync(entry);

        var found = (await Log.QueryAsync(new AuditQuery { TenantId = "kiraci-f" })).ShouldHaveSingleItem();

        found.Actor.ShouldBeNull();
    }

    private static void JsonSemanticallyEquals(string? actual, string? expected)
    {
        if (expected is null)
        {
            actual.ShouldBeNull();
            return;
        }

        actual.ShouldNotBeNull();

        using var actualDoc = JsonDocument.Parse(actual);
        using var expectedDoc = JsonDocument.Parse(expected);

        actualDoc.RootElement.GetRawText().Replace(" ", "", StringComparison.Ordinal)
            .ShouldBe(expectedDoc.RootElement.GetRawText().Replace(" ", "", StringComparison.Ordinal));
    }

    private static AuditEntry Entry(string tenantId, string action, string entity)
        => new()
        {
            Id = AgentPrismId.NewId(),
            TenantId = tenantId,
            Actor = "kullanici-1",
            Action = action,
            Entity = entity,
            Before = """{"version":1}""",
            After = """{"version":2}""",
            CreatedAt = DateTimeOffset.UtcNow,
        };
}
