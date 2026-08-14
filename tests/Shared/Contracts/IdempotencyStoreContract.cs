namespace AgentPrism.StoreContracts;

/// <summary>
/// <see cref="IIdempotencyStore"/> sozlesmesinin davranis testleri (Faz 43).
/// </summary>
/// <remarks>
/// <para>
/// Bellek ici depo ile uc SQL saglayicisi ayni senaryolari gecmelidir.
/// </para>
/// <para>
/// Kiraci kimligi her cagriya <em>acikca</em> verilir (ambient <see cref="ITenantContext"/>
/// uzerinden degil) — sozlesme bu yuzden <c>TenantIsolationContract&lt;TStore&gt;</c>'in
/// genel CRUD sekline degil, dogrudan <see cref="IAsyncLifetime"/>'a dayanir;
/// <c>SingletonLeaseStoreContract</c> ile ayni desen.
/// </para>
/// </remarks>
public abstract class IdempotencyStoreContract : IAsyncLifetime
{
    /// <summary>Sinanan idempotency deposu.</summary>
    protected IIdempotencyStore Store { get; private set; } = null!;

    /// <summary>Test icin bos bir idempotency deposu uretir.</summary>
    /// <returns>Kullanima hazir depo.</returns>
    protected abstract ValueTask<IIdempotencyStore> CreateStoreAsync();

    /// <inheritdoc />
    public async ValueTask InitializeAsync() => Store = await CreateStoreAsync();

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await OnDisposeAsync();
        GC.SuppressFinalize(this);
    }

    /// <summary>Turetilmis sinifin kendi kaynaklarini birakmasi icin kanca.</summary>
    /// <returns>Tamamlanma gorevi.</returns>
    protected virtual ValueTask OnDisposeAsync() => default;

    private const string Tenant = "test";

    private static string Key() => $"key-{Guid.NewGuid():N}";

    private static IdempotencyRequest Request(string tenantId, string key, string fingerprint = "fp-a")
        => new()
        {
            TenantId = tenantId,
            Key = key,
            Fingerprint = fingerprint,
            CreatedAt = DateTimeOffset.UtcNow,
        };

    private static IdempotencyResponse Response(int statusCode = 200, IReadOnlyDictionary<string, string>? headers = null)
        => new()
        {
            StatusCode = statusCode,
            ContentType = "application/json",
            Body = """{"ok":true}""",
            Headers = headers ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
        };

    [Fact]
    public async Task Yeni_anahtar_Reserved_doner()
    {
        var reservation = await Store.ReserveAsync(Request(Tenant, Key()));

        reservation.State.ShouldBe(IdempotencyState.Reserved);
        reservation.Response.ShouldBeNull();
    }

    [Fact]
    public async Task Isleniyor_durumundaki_anahtar_ikinci_ayirmada_InProgress_doner()
    {
        var key = Key();

        await Store.ReserveAsync(Request(Tenant, key));

        var second = await Store.ReserveAsync(Request(Tenant, key));

        second.State.ShouldBe(IdempotencyState.InProgress);
    }

    [Fact]
    public async Task Tamamlanan_anahtar_ayni_parmak_iziyle_saklanan_yaniti_doner()
    {
        var key = Key();

        await Store.ReserveAsync(Request(Tenant, key));
        await Store.CompleteAsync(Tenant, key, Response(statusCode: 201));

        var replay = await Store.ReserveAsync(Request(Tenant, key));

        replay.State.ShouldBe(IdempotencyState.Completed);
        replay.Response.ShouldNotBeNull();
        replay.Response!.StatusCode.ShouldBe(201);
        replay.Response!.Body.ShouldBe("""{"ok":true}""");
    }

    [Fact]
    public async Task Tamamlanan_anahtar_saklanan_HTTP_basliklarini_da_doner()
    {
        var key = Key();
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Location"] = "/api/runs/abc",
            ["Preference-Applied"] = "respond-async",
        };

        await Store.ReserveAsync(Request(Tenant, key));
        await Store.CompleteAsync(Tenant, key, Response(statusCode: 202, headers: headers));

        var replay = await Store.ReserveAsync(Request(Tenant, key));

        replay.State.ShouldBe(IdempotencyState.Completed);
        replay.Response!.Headers["Location"].ShouldBe("/api/runs/abc");
        replay.Response!.Headers["Preference-Applied"].ShouldBe("respond-async");
    }

    [Fact]
    public async Task Tamamlanan_anahtar_baslik_YOKKEN_bos_sozluk_doner()
    {
        var key = Key();

        await Store.ReserveAsync(Request(Tenant, key));
        await Store.CompleteAsync(Tenant, key, Response());

        var replay = await Store.ReserveAsync(Request(Tenant, key));

        replay.Response!.Headers.ShouldBeEmpty();
    }

    [Fact]
    public async Task Tamamlanan_anahtar_FARKLI_parmak_iziyle_FingerprintMismatch_doner()
    {
        var key = Key();

        await Store.ReserveAsync(Request(Tenant, key, "fp-a"));
        await Store.CompleteAsync(Tenant, key, Response());

        var mismatch = await Store.ReserveAsync(Request(Tenant, key, "fp-b"));

        mismatch.State.ShouldBe(IdempotencyState.FingerprintMismatch);
        mismatch.Response.ShouldBeNull();
    }

    [Fact]
    public async Task Birakilan_anahtar_yeniden_ayrilabilir()
    {
        var key = Key();

        await Store.ReserveAsync(Request(Tenant, key));
        await Store.ReleaseAsync(Tenant, key);

        // 🚨 Basarisiz calistirmadan sonra ayni anahtarla yeniden deneme
        // CALISMALIDIR — kayit silinmis olmalidir (docs/43-IDEMPOTENCY-KEY.md, 43.2).
        var retried = await Store.ReserveAsync(Request(Tenant, key));

        retried.State.ShouldBe(IdempotencyState.Reserved);
    }

    [Fact]
    public async Task Ayni_anahtar_iki_kiracida_bagimsiz_yasar()
    {
        var key = Key();

        var first = await Store.ReserveAsync(Request("tenant-a", key));
        var second = await Store.ReserveAsync(Request("tenant-b", key));

        // Ikinci kiraci ILK kiracinin ayirmasindan ETKILENMEMELIDIR.
        first.State.ShouldBe(IdempotencyState.Reserved);
        second.State.ShouldBe(IdempotencyState.Reserved);
    }

    [Fact]
    public async Task Tamamlanmis_kayit_diger_kiraciyi_ETKILEMEZ()
    {
        var key = Key();

        await Store.ReserveAsync(Request("tenant-a", key));
        await Store.CompleteAsync("tenant-a", key, Response());

        var otherTenant = await Store.ReserveAsync(Request("tenant-b", key));

        otherTenant.State.ShouldBe(IdempotencyState.Reserved);
    }

    [Fact]
    public async Task Eszamanli_ayni_anahtar_ayirmasinda_yalniz_biri_Reserved_alir()
    {
        var key = Key();

        var results = await Task.WhenAll(
            Enumerable.Range(0, 8).Select(_ => Store.ReserveAsync(Request(Tenant, key)).AsTask()));

        results.Count(r => r.State == IdempotencyState.Reserved).ShouldBe(1);
        results.Count(r => r.State == IdempotencyState.InProgress).ShouldBe(7);
    }
}
