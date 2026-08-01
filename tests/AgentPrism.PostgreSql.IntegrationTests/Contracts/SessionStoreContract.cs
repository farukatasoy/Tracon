using AgentPrism.PostgreSql.IntegrationTests.Infrastructure;

namespace AgentPrism.PostgreSql.IntegrationTests.Contracts;

/// <summary>
/// <see cref="ISessionStore"/> sozlesmesinin davranis testleri.
/// </summary>
/// <remarks>
/// Oturum durumu opaktir; depo icerigi yorumlamadan aynen geri vermelidir.
/// </remarks>
public abstract class SessionStoreContract : IAsyncLifetime
{
    /// <summary>Test edilen depo.</summary>
    protected ISessionStore Store { get; private set; } = null!;

    /// <summary>Test icin bos bir depo uretir.</summary>
    /// <returns>Kullanima hazir depo.</returns>
    protected abstract ValueTask<ISessionStore> CreateStoreAsync();

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

    [Fact]
    public async Task Oturum_durumu_bozulmadan_geri_gelir()
    {
        var state = TestData.State(
            """
            {"messages":[{"role":"user","text":"merhaba üği"},{"role":"assistant","text":"selam"}],
             "nested":{"deep":{"value":3.14159}},"flag":true,"nothing":null}
            """);

        await Store.SaveAsync(TestData.Session("s1", state));

        var loaded = await Store.GetAsync("s1");

        loaded.ShouldNotBeNull();
        loaded.State.GetRawText().ShouldBe(state.GetRawText());
        loaded.State.GetProperty("nested").GetProperty("deep").GetProperty("value").GetDouble().ShouldBe(3.14159);
        loaded.State.GetProperty("messages")[0].GetProperty("text").GetString().ShouldBe("merhaba üği");
    }

    [Fact]
    public async Task Ayni_kimlikle_kayit_uzerine_yazar_ve_olusturulma_zamanini_korur()
    {
        var created = DateTimeOffset.UtcNow.AddHours(-1);

        await Store.SaveAsync(TestData.Session("s1") with
        {
            CreatedAt = created,
            UpdatedAt = created,
            State = TestData.State("""{"turn":1}"""),
        });

        var later = DateTimeOffset.UtcNow;

        await Store.SaveAsync(TestData.Session("s1") with
        {
            CreatedAt = later,
            UpdatedAt = later,
            State = TestData.State("""{"turn":2}"""),
        });

        var loaded = await Store.GetAsync("s1");

        loaded.ShouldNotBeNull();
        loaded.State.GetProperty("turn").GetInt32().ShouldBe(2);
        loaded.CreatedAt.ShouldBe(created, TimeSpan.FromMilliseconds(1));
        loaded.UpdatedAt.ShouldBe(later, TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public async Task Silme_kaydi_kaldirir()
    {
        await Store.SaveAsync(TestData.Session("s1"));

        (await Store.DeleteAsync("s1")).ShouldBeTrue();
        (await Store.GetAsync("s1")).ShouldBeNull();
        (await Store.DeleteAsync("s1")).ShouldBeFalse();
    }

    [Fact]
    public async Task Olmayan_oturum_null_doner()
        => (await Store.GetAsync("yok")).ShouldBeNull();

    [Fact]
    public async Task Sorgu_en_son_guncellenenden_baslar()
    {
        var now = DateTimeOffset.UtcNow;

        await Store.SaveAsync(TestData.Session("eski") with { UpdatedAt = now.AddMinutes(-10) });
        await Store.SaveAsync(TestData.Session("yeni") with { UpdatedAt = now });
        await Store.SaveAsync(TestData.Session("orta") with { UpdatedAt = now.AddMinutes(-5) });

        var results = await Store.QueryAsync(new SessionQuery());

        results.Select(static session => session.Id).ShouldBe(["yeni", "orta", "eski"]);
    }

    [Fact]
    public async Task Sorgu_agent_adina_gore_filtreler()
    {
        await Store.SaveAsync(TestData.Session("s1") with { AgentName = "alpha" });
        await Store.SaveAsync(TestData.Session("s2") with { AgentName = "beta" });

        var results = await Store.QueryAsync(new SessionQuery { AgentName = "alpha" });

        results.ShouldHaveSingleItem().Id.ShouldBe("s1");
    }

    [Fact]
    public async Task Sorgu_sayfalama_uygular()
    {
        var now = DateTimeOffset.UtcNow;

        for (var i = 0; i < 5; i++)
        {
            await Store.SaveAsync(TestData.Session($"s{i}") with { UpdatedAt = now.AddMinutes(-i) });
        }

        var page = await Store.QueryAsync(new SessionQuery { Skip = 1, Take = 2 });

        page.Select(static session => session.Id).ShouldBe(["s1", "s2"]);
    }
}
