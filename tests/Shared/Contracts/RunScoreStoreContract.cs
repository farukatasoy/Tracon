namespace AgentPrism.StoreContracts;

/// <summary>
/// <see cref="IRunScoreStore"/> sozlesmesinin davranis testleri.
/// </summary>
/// <remarks>
/// Bellek ici depo ile uc SQL saglayicisi ayni senaryolari gecmelidir. Kritik
/// kural: ayni yazar ayni hedefi (calistirma veya mesaj) ikinci kez
/// puanladiginda satir <strong>guncellenir</strong>, yeni satir acilmaz --
/// yazar bos ise (kimliksiz kurulum) bu kural uygulanmaz.
/// </remarks>
public abstract class RunScoreStoreContract : IAsyncLifetime
{
    private const string Tenant = "test";

    private static readonly DateTimeOffset Created = new(2026, 8, 6, 10, 0, 0, TimeSpan.Zero);

    /// <summary>Test edilen depo.</summary>
    protected IRunScoreStore Store { get; private set; } = null!;

    /// <summary>Test icin bos bir depo uretir.</summary>
    protected abstract ValueTask<IRunScoreStore> CreateStoreAsync();

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
    public async Task Yazilan_puan_geri_okunur()
    {
        var runId = AgentPrismId.NewId();
        var score = Score(runId);

        var saved = await Store.UpsertAsync(score);

        saved.Id.ShouldNotBe(Guid.Empty);

        var loaded = (await Store.ListAsync(Tenant, runId)).ShouldHaveSingleItem();

        loaded.RunId.ShouldBe(runId);
        loaded.Kind.ShouldBe(RunScoreKind.Binary);
        loaded.Value.ShouldBe(1);
        loaded.Comment.ShouldBe("dogru cevap");
        loaded.Source.ShouldBe("human");
        loaded.Author.ShouldBe("operator@ornek");
    }

    [Fact]
    public async Task Mesaj_kimligi_bos_ise_puan_tum_calistirmaya_aittir()
    {
        var runId = AgentPrismId.NewId();

        await Store.UpsertAsync(Score(runId) with { MessageId = null });

        (await Store.ListAsync(Tenant, runId)).ShouldHaveSingleItem().MessageId.ShouldBeNull();
    }

    [Fact]
    public async Task Ayni_yazar_ayni_hedefi_ikinci_kez_puanladiginda_satir_GUNCELLENIR()
    {
        var runId = AgentPrismId.NewId();

        var first = await Store.UpsertAsync(Score(runId) with { Value = 0 });
        var second = await Store.UpsertAsync(Score(runId) with { Value = 1, Comment = "guncellendi" });

        second.Id.ShouldBe(first.Id);

        var loaded = (await Store.ListAsync(Tenant, runId)).ShouldHaveSingleItem();
        loaded.Value.ShouldBe(1);
        loaded.Comment.ShouldBe("guncellendi");
    }

    [Fact]
    public async Task Farkli_yazarlar_ayni_hedefi_bagimsiz_puanlar()
    {
        var runId = AgentPrismId.NewId();

        await Store.UpsertAsync(Score(runId) with { Author = "alice" });
        await Store.UpsertAsync(Score(runId) with { Author = "bob", Value = 0 });

        (await Store.ListAsync(Tenant, runId)).Count.ShouldBe(2);
    }

    [Fact]
    public async Task Farkli_mesajlar_bagimsiz_puanlanir()
    {
        var runId = AgentPrismId.NewId();

        await Store.UpsertAsync(Score(runId) with { MessageId = "msg-1" });
        await Store.UpsertAsync(Score(runId) with { MessageId = "msg-2", Value = 0 });
        await Store.UpsertAsync(Score(runId) with { MessageId = null, Value = 0 });

        (await Store.ListAsync(Tenant, runId)).Count.ShouldBe(3);
    }

    [Fact]
    public async Task Yazar_bos_ise_HER_cagri_yeni_satir_acar()
    {
        // Kimliksiz kurulumda (author null) benzersizlik kurali uygulanmaz --
        // acik soru 4 (docs/31-GERI-BILDIRIM-VE-PUANLAMA.md).
        var runId = AgentPrismId.NewId();

        await Store.UpsertAsync(Score(runId) with { Author = null });
        await Store.UpsertAsync(Score(runId) with { Author = null, Value = 0 });

        (await Store.ListAsync(Tenant, runId)).Count.ShouldBe(2);
    }

    [Fact]
    public async Task Baska_kiracinin_puani_gorunmez()
    {
        var runId = AgentPrismId.NewId();

        await Store.UpsertAsync(Score(runId));
        await Store.UpsertAsync(Score(runId) with { TenantId = "baska", Author = "baska-yazar" });

        (await Store.ListAsync(Tenant, runId)).Count.ShouldBe(1);
        (await Store.ListAsync("baska", runId)).Count.ShouldBe(1);
    }

    [Fact]
    public async Task Puan_silinir()
    {
        var runId = AgentPrismId.NewId();
        var saved = await Store.UpsertAsync(Score(runId));

        var deleted = await Store.DeleteAsync(Tenant, saved.Id);

        deleted.ShouldBeTrue();
        (await Store.ListAsync(Tenant, runId)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Baska_kiracinin_puani_silinemez()
    {
        var runId = AgentPrismId.NewId();
        var saved = await Store.UpsertAsync(Score(runId));

        var deleted = await Store.DeleteAsync("baska", saved.Id);

        deleted.ShouldBeFalse();
        (await Store.ListAsync(Tenant, runId)).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Olmayan_puanin_silinmesi_false_doner()
        => (await Store.DeleteAsync(Tenant, AgentPrismId.NewId())).ShouldBeFalse();

    private static RunScore Score(Guid runId)
        => new()
        {
            TenantId = Tenant,
            RunId = runId,
            MessageId = "msg-1",
            Kind = RunScoreKind.Binary,
            Value = 1,
            Comment = "dogru cevap",
            Source = "human",
            Author = "operator@ornek",
            CreatedAt = Created,
        };
}
