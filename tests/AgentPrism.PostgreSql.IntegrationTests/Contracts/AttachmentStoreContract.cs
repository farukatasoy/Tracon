namespace AgentPrism.PostgreSql.IntegrationTests.Contracts;

/// <summary>
/// <see cref="IAttachmentStore"/> sozlesmesinin davranis testleri.
/// </summary>
/// <remarks>
/// Bellek ici depo ile PostgreSQL deposu ayni senaryolari gecmelidir; ozellikle
/// kiraci yalitimi ve oturum silindiginde eklerin gitmesi (docs/14-COK-MODLULUK.md,
/// acik soru 2) iki uygulamada da ayni davranmalidir.
/// </remarks>
public abstract class AttachmentStoreContract : IAsyncLifetime
{
    /// <summary>Test edilen depo.</summary>
    protected IAttachmentStore Store { get; private set; } = null!;

    /// <summary>Test icin bos bir depo uretir.</summary>
    protected abstract ValueTask<IAttachmentStore> CreateStoreAsync();

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
    public async Task Kayit_ustveri_dogru_saklanir()
    {
        var saved = await Store.SaveAsync(Content("tenant-a", fileName: "rapor.pdf", mediaType: "application/pdf"));

        var loaded = await Store.GetAsync("tenant-a", saved.Id);

        loaded.ShouldNotBeNull();
        loaded.FileName.ShouldBe("rapor.pdf");
        loaded.MediaType.ShouldBe("application/pdf");
        loaded.ByteSize.ShouldBe(saved.ByteSize);
        loaded.Sha256.ShouldBe(saved.Sha256);
    }

    [Fact]
    public async Task Icerik_oldugu_gibi_okunur()
    {
        var data = new byte[] { 10, 20, 30, 40, 50 };
        var saved = await Store.SaveAsync(Content("tenant-a", data: data));

        await using var stream = await Store.OpenReadAsync("tenant-a", saved.Id);
        stream.ShouldNotBeNull();

        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer);

        buffer.ToArray().ShouldBe(data);
    }

    [Fact]
    public async Task Baska_kiracinin_eki_gorulmez()
    {
        var saved = await Store.SaveAsync(Content("tenant-a"));

        (await Store.GetAsync("tenant-b", saved.Id)).ShouldBeNull();
        (await Store.OpenReadAsync("tenant-b", saved.Id)).ShouldBeNull();
        (await Store.DeleteAsync("tenant-b", saved.Id)).ShouldBeFalse();

        (await Store.GetAsync("tenant-a", saved.Id)).ShouldNotBeNull();
    }

    [Fact]
    public async Task Listeleme_oturuma_gore_filtreler()
    {
        await Store.SaveAsync(Content("tenant-a", sessionId: "s-1"));
        await Store.SaveAsync(Content("tenant-a", sessionId: "s-2"));
        await Store.SaveAsync(Content("tenant-a", sessionId: "s-1"));

        var results = await Store.ListAsync(new AttachmentQuery { TenantId = "tenant-a", SessionId = "s-1" });

        results.Count.ShouldBe(2);
        results.ShouldAllBe(static descriptor => string.Equals(descriptor.SessionId, "s-1", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Silme_bir_kez_basarili_ikinci_seferde_false_doner()
    {
        var saved = await Store.SaveAsync(Content("tenant-a"));

        (await Store.DeleteAsync("tenant-a", saved.Id)).ShouldBeTrue();
        (await Store.DeleteAsync("tenant-a", saved.Id)).ShouldBeFalse();
        (await Store.GetAsync("tenant-a", saved.Id)).ShouldBeNull();
    }

    [Fact]
    public async Task Oturuma_gore_toplu_silme_yalniz_eslesenleri_kaldirir()
    {
        var a = await Store.SaveAsync(Content("tenant-a", sessionId: "s-1"));
        var b = await Store.SaveAsync(Content("tenant-a", sessionId: "s-1"));
        var other = await Store.SaveAsync(Content("tenant-a", sessionId: "s-2"));

        var deleted = await Store.DeleteBySessionAsync("tenant-a", "s-1");

        deleted.ShouldBe(2);
        (await Store.GetAsync("tenant-a", a.Id)).ShouldBeNull();
        (await Store.GetAsync("tenant-a", b.Id)).ShouldBeNull();
        (await Store.GetAsync("tenant-a", other.Id)).ShouldNotBeNull();
    }

    private static AttachmentContent Content(
        string tenantId,
        string fileName = "test.png",
        string mediaType = "image/png",
        string? sessionId = null,
        byte[]? data = null)
        => new()
        {
            TenantId = tenantId,
            SessionId = sessionId,
            FileName = fileName,
            MediaType = mediaType,
            Data = data ?? [1, 2, 3, 4],
        };
}
