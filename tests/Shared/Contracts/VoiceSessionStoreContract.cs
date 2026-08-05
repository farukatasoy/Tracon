namespace AgentPrism.StoreContracts;

/// <summary>
/// <see cref="IVoiceSessionStore"/> sozlesmesinin davranis testleri.
/// </summary>
/// <remarks>
/// Bellek ici depo ile uc SQL saglayicisi ayni senaryolari gecmelidir. Iki kural
/// kritiktir: aynı kimlikle ikinci yazma <strong>gunceller</strong> (baglanti
/// once acilir, sonra kapanir) ve <c>input_seconds</c> ondalik kismini
/// <strong>kaybetmez</strong>.
/// </remarks>
public abstract class VoiceSessionStoreContract : IAsyncLifetime
{
    private const string Tenant = "test";

    private static readonly DateTimeOffset Started = new(2026, 8, 5, 10, 0, 0, TimeSpan.Zero);

    /// <summary>Test edilen depo.</summary>
    protected IVoiceSessionStore Store { get; private set; } = null!;

    /// <summary>Test icin bos bir depo uretir.</summary>
    protected abstract ValueTask<IVoiceSessionStore> CreateStoreAsync();

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
    public async Task Kaydedilen_konusma_geri_okunur()
    {
        var record = Record();

        await Store.SaveAsync(record);

        var loaded = (await Store.QueryAsync(Tenant, new VoiceSessionQuery())).ShouldHaveSingleItem();

        loaded.Id.ShouldBe(record.Id);
        loaded.SessionId.ShouldBe("oturum-1");
        loaded.AgentName.ShouldBe("destek");
        loaded.Turns.ShouldBe(3);
        loaded.OutputChars.ShouldBe(420);
        loaded.EndReason.ShouldBe(VoiceSessionEndReason.Client);
        loaded.CreatedBy.ShouldBe("operator@ornek");
    }

    [Fact]
    public async Task Sure_ondalik_kismini_KAYBETMEZ()
    {
        // 🚨 Olcum sutunu ondalik tasir. Tipi verilmemis bir parametre SQL
        // Server'da decimal(18,0) sayilir ve kesir SESSIZCE kesilirdi.
        await Store.SaveAsync(Record() with { InputSeconds = 12.345m });

        var loaded = (await Store.QueryAsync(Tenant, new VoiceSessionQuery())).ShouldHaveSingleItem();

        loaded.InputSeconds.ShouldBe(12.345m);
    }

    [Fact]
    public async Task Olcum_yoksa_null_kalir_SIFIR_degil()
    {
        // AgentPrism olcum uydurmaz (K-032): saglayici sure bildirmediyse alan
        // bos kalir.
        await Store.SaveAsync(Record() with { InputSeconds = null, OutputChars = null, EndedAt = null });

        var loaded = (await Store.QueryAsync(Tenant, new VoiceSessionQuery())).ShouldHaveSingleItem();

        loaded.InputSeconds.ShouldBeNull();
        loaded.OutputChars.ShouldBeNull();
        loaded.EndedAt.ShouldBeNull();
    }

    [Fact]
    public async Task Ayni_kimlikle_ikinci_yazma_GUNCELLER()
    {
        // Baglanti once acilir (turns = 0), sonra kapanir. Iki satir olusursa
        // ayni konusma iki kez sayilirdi.
        var record = Record() with { Turns = 0, EndedAt = null, EndReason = null };

        await Store.SaveAsync(record);
        await Store.SaveAsync(record with
        {
            Turns = 5,
            EndedAt = Started.AddMinutes(4),
            EndReason = VoiceSessionEndReason.IdleTimeout,
        });

        var loaded = (await Store.QueryAsync(Tenant, new VoiceSessionQuery())).ShouldHaveSingleItem();

        loaded.Turns.ShouldBe(5);
        loaded.EndReason.ShouldBe(VoiceSessionEndReason.IdleTimeout);
    }

    [Fact]
    public async Task Baska_kiracinin_kaydi_gorunmez()
    {
        await Store.SaveAsync(Record());
        await Store.SaveAsync(Record() with { Id = AgentPrismId.NewId(), TenantId = "baska" });

        (await Store.QueryAsync(Tenant, new VoiceSessionQuery())).Count.ShouldBe(1);
        (await Store.QueryAsync("baska", new VoiceSessionQuery())).Count.ShouldBe(1);
    }

    [Fact]
    public async Task Agent_ve_oturum_suzgeci_calisir()
    {
        await Store.SaveAsync(Record());
        await Store.SaveAsync(Record() with
        {
            Id = AgentPrismId.NewId(),
            AgentName = "arastirmaci",
            SessionId = "oturum-2",
        });

        (await Store.QueryAsync(Tenant, new VoiceSessionQuery { AgentName = "destek" }))
            .ShouldHaveSingleItem().SessionId.ShouldBe("oturum-1");

        (await Store.QueryAsync(Tenant, new VoiceSessionQuery { SessionId = "oturum-2" }))
            .ShouldHaveSingleItem().AgentName.ShouldBe("arastirmaci");
    }

    [Fact]
    public async Task Liste_en_yeniden_eskiye_sirali_gelir()
    {
        await Store.SaveAsync(Record() with { Id = AgentPrismId.NewId(), StartedAt = Started });
        await Store.SaveAsync(Record() with { Id = AgentPrismId.NewId(), StartedAt = Started.AddMinutes(10) });
        await Store.SaveAsync(Record() with { Id = AgentPrismId.NewId(), StartedAt = Started.AddMinutes(5) });

        var loaded = await Store.QueryAsync(Tenant, new VoiceSessionQuery());

        loaded.Count.ShouldBe(3);
        loaded[0].StartedAt.ShouldBe(Started.AddMinutes(10));
        loaded[2].StartedAt.ShouldBe(Started);
    }

    [Fact]
    public async Task Sayfalama_uygulanir()
    {
        for (var index = 0; index < 5; index++)
        {
            await Store.SaveAsync(Record() with
            {
                Id = AgentPrismId.NewId(),
                StartedAt = Started.AddMinutes(index),
            });
        }

        var page = await Store.QueryAsync(Tenant, new VoiceSessionQuery { Skip = 1, Take = 2 });

        page.Count.ShouldBe(2);
        page[0].StartedAt.ShouldBe(Started.AddMinutes(3));
    }

    private static VoiceSessionRecord Record()
        => new()
        {
            Id = AgentPrismId.NewId(),
            TenantId = Tenant,
            SessionId = "oturum-1",
            AgentName = "destek",
            StartedAt = Started,
            EndedAt = Started.AddMinutes(2),
            Turns = 3,
            InputSeconds = 7.5m,
            OutputChars = 420,
            EndReason = VoiceSessionEndReason.Client,
            CreatedBy = "operator@ornek",
        };
}
