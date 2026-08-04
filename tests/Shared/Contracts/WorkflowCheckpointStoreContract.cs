using System.Text.Json;

namespace AgentPrism.StoreContracts;

/// <summary>
/// <see cref="IWorkflowCheckpointStore"/> sozlesmesinin davranis testleri.
/// </summary>
/// <remarks>
/// En kritik senaryo <em>polimorfik yukun bozulmadan</em> geri okunmasidir:
/// Microsoft Agent Framework'un kontrol noktasi JSON'unda <c>$type</c> ayraci
/// bulunur ve bulundugu nesnenin ilk ozelligi olmak zorundadir. PostgreSQL
/// <c>jsonb</c> anahtarlari yeniden siralar ve ayraci ilk olmaktan cikarir;
/// bu yuzden sutun <c>json</c>'dur (karar K-027).
/// </remarks>
public abstract class WorkflowCheckpointStoreContract : IAsyncLifetime
{
    /// <summary>Test edilen depo.</summary>
    protected IWorkflowCheckpointStore Store { get; private set; } = null!;

    /// <summary>Test icin bos bir depo uretir.</summary>
    protected abstract ValueTask<IWorkflowCheckpointStore> CreateStoreAsync();

    /// <inheritdoc />
    public async ValueTask InitializeAsync() => Store = await CreateStoreAsync();

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await OnDisposeAsync();
        GC.SuppressFinalize(this);
    }

    /// <summary>Turetilmis sinifin kendi kaynaklarini birakmasi icin kanca.</summary>
    protected virtual ValueTask OnDisposeAsync() => default;

    [Fact]
    public async Task Yazilan_nokta_geri_okunur()
    {
        await Store.CreateAsync(Record("tenant-a", "s-1", "c-1"));

        var state = await Store.ReadAsync("tenant-a", "s-1", "c-1");

        state.ShouldNotBeNull();
        state.Value.GetProperty("stepNumber").GetInt32().ShouldBe(3);
    }

    [Fact]
    public async Task Polimorfik_yuk_ANAHTAR_SIRASI_KORUNARAK_okunur()
    {
        // 🚨 Bu testin varlik sebebi: `jsonb` anahtarlari once uzunluga sonra
        // bayta gore yeniden sirilar. System.Text.Json'in `$type` ayraci
        // bulundugu nesnenin ILK ozelligi olmak zorundadir; sira bozulursa
        // okuma "The metadata property ... is not the first property" ile
        // patlar. Sutun bu yuzden `json`'dur.
        //
        // Ayrac ic ice bir nesnede tutulur cunku MAF'in gercek yuku de oyledir
        // (Faz 15'te olculdu: 7.537 baytlik bir noktada `{"$type":0,...}`).
        const string Payload = """
            {"stepNumber":0,"edges":{"yazar":[{"$type":0,"hasCondition":false,"kind":0}]},"zzzz":"son"}
            """;

        using var document = JsonDocument.Parse(Payload);

        await Store.CreateAsync(Record("tenant-a", "s-poly", "c-poly") with
        {
            State = document.RootElement.Clone(),
        });

        var state = await Store.ReadAsync("tenant-a", "s-poly", "c-poly");

        state.ShouldNotBeNull();

        var edge = state.Value
            .GetProperty("edges")
            .GetProperty("yazar")[0];

        // Ilk ozellik hala `$type` olmalidir.
        var firstProperty = edge.EnumerateObject().First();

        firstProperty.Name.ShouldBe("$type");
        firstProperty.Value.GetInt32().ShouldBe(0);

        // Kok nesnede de sira korunur: "stepNumber" (10 karakter) `jsonb`
        // altinda "edges" (5 karakter) ve "zzzz" (4 karakter) sonrasina duserdi.
        state.Value.EnumerateObject().Select(static property => property.Name)
            .ShouldBe(["stepNumber", "edges", "zzzz"]);
    }

    [Fact]
    public async Task Baska_kiracinin_noktasi_BULUNAMAZ()
    {
        await Store.CreateAsync(Record("tenant-a", "s-1", "c-1"));

        // "Yetkisiz" degil, "bulunamadi". Bir kontrol noktasi tum yurutme
        // durumunu tasir; varliginin bilgisi bile sizdirilmamalidir.
        (await Store.ReadAsync("tenant-b", "s-1", "c-1")).ShouldBeNull();
        (await Store.ListAsync("tenant-b", "s-1")).ShouldBeEmpty();
        (await Store.DeleteAsync("tenant-b", "s-1")).ShouldBe(0);

        (await Store.ReadAsync("tenant-a", "s-1", "c-1")).ShouldNotBeNull();
    }

    [Fact]
    public async Task Listeleme_olusma_sirasini_korur()
    {
        var start = DateTimeOffset.UtcNow.AddMinutes(-5);

        await Store.CreateAsync(Record("tenant-a", "s-1", "c-1") with { CreatedAt = start });
        await Store.CreateAsync(Record("tenant-a", "s-1", "c-2") with
        {
            CreatedAt = start.AddSeconds(1),
            ParentCheckpointId = "c-1",
        });
        await Store.CreateAsync(Record("tenant-a", "s-1", "c-3") with
        {
            CreatedAt = start.AddSeconds(2),
            ParentCheckpointId = "c-2",
        });

        var list = await Store.ListAsync("tenant-a", "s-1");

        list.Select(static record => record.CheckpointId).ShouldBe(["c-1", "c-2", "c-3"]);
        list[1].ParentCheckpointId.ShouldBe("c-1");
    }

    [Fact]
    public async Task Listeleme_durum_yukunu_TASIMAZ()
    {
        // Bir kontrol noktasi kilobaytlarca opak JSON tasir; listeye eklemek
        // arayuzun checkpoint ekranini acilamaz hale getirirdi.
        await Store.CreateAsync(Record("tenant-a", "s-1", "c-1"));

        var list = await Store.ListAsync("tenant-a", "s-1");

        WorkflowCheckpointState.IsOmitted(list[0].State).ShouldBeTrue();
    }

    [Fact]
    public async Task Calistirmaya_gore_listeleme_filtreler()
    {
        var runA = AgentPrismId.NewId();
        var runB = AgentPrismId.NewId();
        var start = DateTimeOffset.UtcNow.AddMinutes(-5);

        await Store.CreateAsync(Record("tenant-a", "s-1", "c-1") with { RunId = runA, CreatedAt = start });
        await Store.CreateAsync(Record("tenant-a", "s-1", "c-2") with
        {
            RunId = runB,
            CreatedAt = start.AddSeconds(1),
        });
        await Store.CreateAsync(Record("tenant-a", "s-1", "c-3") with
        {
            RunId = runA,
            CreatedAt = start.AddSeconds(2),
        });

        var list = await Store.ListByRunAsync("tenant-a", runA);

        list.Select(static record => record.CheckpointId).ShouldBe(["c-1", "c-3"]);
    }

    [Fact]
    public async Task Silme_oturumun_tamamini_temizler()
    {
        await Store.CreateAsync(Record("tenant-a", "s-1", "c-1"));
        await Store.CreateAsync(Record("tenant-a", "s-1", "c-2"));
        await Store.CreateAsync(Record("tenant-a", "s-2", "c-3"));

        (await Store.DeleteAsync("tenant-a", "s-1")).ShouldBe(2);
        (await Store.ListAsync("tenant-a", "s-1")).ShouldBeEmpty();
        (await Store.ListAsync("tenant-a", "s-2")).Count.ShouldBe(1);
    }

    [Fact]
    public async Task Var_olmayan_nokta_null_doner()
        => (await Store.ReadAsync("tenant-a", "s-1", "yok")).ShouldBeNull();

    private static WorkflowCheckpointRecord Record(string tenantId, string sessionId, string checkpointId)
        => new()
        {
            Id = AgentPrismId.NewId(),
            TenantId = tenantId,
            SessionId = sessionId,
            CheckpointId = checkpointId,
            CreatedAt = DateTimeOffset.UtcNow,
            State = DefaultState,
        };

    private static JsonElement DefaultState { get; } =
        JsonDocument.Parse("""{"stepNumber":3,"executors":["yazar","editor"]}""").RootElement.Clone();
}
