using Microsoft.Agents.AI;

namespace AgentPrism.StoreContracts;

/// <summary>
/// Kalici agent dosya belleginin kiraci yalitimi sozlesmesi.
/// </summary>
/// <remarks>
/// <para>
/// Faz 41'de eklendi. Dosya bellegi bir <c>IStore</c> degil, Microsoft Agent
/// Framework'un <see cref="AgentFileStore"/> tipidir; kiraci
/// <see cref="ITenantContext"/>'ten, agent adi ise suren calistirmanin ambient
/// kapsamindan (<c>AgentPrismRunContext</c>) okunur.
/// </para>
/// <para>
/// 🚨 Ambient kapsam her test <strong>govdesinin basinda</strong> kurulur,
/// <c>InitializeAsync</c>'te degil: xunit v3 (MTP) yasam dongusu kancasi ile
/// test govdesini ayri zamanlanmis isler olarak calistirabiliyor ve
/// <c>AsyncLocal</c> akisi kesiliyor.
/// </para>
/// </remarks>
#pragma warning disable MAAI001 // AgentFileStore "evaluation purposes only"; gerekce urun kodundaki ile ayni.
public abstract class AgentFileStoreContract : TenantIsolationContract<AgentFileStore>
{
    /// <inheritdoc />
    protected override async ValueTask<object> SeedAsync(string tenantId, string name)
    {
        Enter(tenantId);

        var path = $"/{name}.md";
        await Store.WriteAsync(path, $"{tenantId} icerigi");

        return path;
    }

    /// <inheritdoc />
    protected override async ValueTask<bool> ExistsAsync(string tenantId, object key)
    {
        Enter(tenantId);

        var path = (string)key;
        var content = await Store.ReadAsync(path);

        // Varlik denetimi ve arama ayni siniri tasimalidir.
        (await Store.FileExistsAsync(path)).ShouldBe(content is not null);

        return content is not null;
    }

    /// <inheritdoc />
    protected override async ValueTask<int> CountAsync(string tenantId)
    {
        Enter(tenantId);
        return (await Store.ListChildrenAsync("/")).Count;
    }

    /// <inheritdoc />
    protected override async ValueTask<bool?> TryDeleteAsync(string tenantId, object key)
    {
        Enter(tenantId);

        var path = (string)key;

        if (!await Store.FileExistsAsync(path))
        {
            return false;
        }

        await Store.DeleteAsync(path);
        return true;
    }

    [Fact]
    public async Task Arama_baska_kiracinin_dosyasini_bulmaz()
    {
        Enter(TenantA);
        await Store.WriteAsync("/notlar/a.md", "fatura numarasi 42");

        Enter(TenantB);
        (await Store.SearchAsync("/", "fatura", recursive: true)).ShouldBeEmpty();

        Enter(TenantA);
        (await Store.SearchAsync("/", "fatura", recursive: true)).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Dizin_olusturma_kayit_uretmez()
    {
        // MAF sozlesmesi bir dizin cagrisi bekler; kalici depoda dizinler
        // yollarin icinde ortuk yasar ve ayri bir satir olusmaz.
        Enter(TenantA);

        await Store.CreateDirectoryAsync("/notlar");

        (await Store.ListChildrenAsync("/")).ShouldBeEmpty();
    }

    [Fact]
    public async Task Recursive_false_alt_dizindeki_eslesmeyi_atlar()
    {
        // Faz 51, Is A: derinlik siniri SQL'e indi (prefix_deep_like). Bu test
        // davranisin degismedigini kanitlar.
        Enter(TenantA);

        await Store.WriteAsync("/notlar/ust.md", "anahtar kelime burada");
        await Store.WriteAsync("/notlar/alt/derin.md", "anahtar kelime burada da var");

        var shallow = await Store.SearchAsync("/notlar", "anahtar", recursive: false);
        shallow.ShouldHaveSingleItem().FileName.ShouldBe("/notlar/ust.md");

        var deep = await Store.SearchAsync("/notlar", "anahtar", recursive: true);
        deep.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Glob_suzgeci_dosya_adina_gore_daraltir()
    {
        // Faz 51, Is A: glob SQL'e indi (name_like). `*` dizin sinirini asar,
        // orijinal .NET regex tabanli eslemeyle ayni davranis.
        Enter(TenantA);

        await Store.WriteAsync("/notlar/a.md", "ortak deger");
        await Store.WriteAsync("/notlar/a.txt", "ortak deger");
        await Store.WriteAsync("/notlar/alt/b.md", "ortak deger");

        var results = await Store.SearchAsync("/notlar", "ortak", globPattern: "*.md", recursive: true);

        results.Select(static r => r.FileName)
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ShouldBe(["/notlar/a.md", "/notlar/alt/b.md"]);
    }

    [Fact]
    public async Task Buyuk_depoda_arama_yalniz_hedef_dizini_dondurur()
    {
        // Faz 51, Is A: `LoadAllAsync` kaldirildi. Bu test, cok sayida ILGISIZ
        // dosya varken hedef dizindeki tek eslesmenin dogru bulundugunu
        // kanitlar (satir sayisi olcumu Postgres'e ozgu EXPLAIN ile ayrica
        // yapilir, bkz. docs/51-VEKTOR-BELLEK-VE-RAG.md).
        Enter(TenantA);

        const int UnrelatedFileCount = 500;

        for (var i = 0; i < UnrelatedFileCount; i++)
        {
            await Store.WriteAsync($"/arsiv/dosya-{i:D4}.md", "ilgisiz icerik");
        }

        await Store.WriteAsync("/hedef/not.md", "aranan-anahtar burada");

        var results = await Store.SearchAsync("/hedef", "aranan-anahtar", recursive: true);

        results.ShouldHaveSingleItem().FileName.ShouldBe("/hedef/not.md");
    }

    /// <summary>
    /// Gecerli kiraciyi ayarlar ve ambient calistirma kapsamini kurar.
    /// </summary>
    /// <param name="tenantId">Kiraci kimligi.</param>
    protected void Enter(string tenantId)
    {
        AmbientTenant.TenantId = tenantId;

        AgentPrismRunContext.SetCurrent(new AgentRunScope
        {
            RunId = Guid.NewGuid(),
            RootRunId = Guid.NewGuid(),
            AgentName = "yalitim-agenti",
        });
    }

    /// <inheritdoc />
    protected override ValueTask OnDisposeAsync()
    {
        AgentPrismRunContext.SetCurrent(null);
        return default;
    }
}
#pragma warning restore MAAI001
