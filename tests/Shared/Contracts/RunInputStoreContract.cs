using Microsoft.Extensions.AI;

namespace AgentPrism.StoreContracts;

/// <summary>
/// <see cref="IRunInputStore"/> sozlesmesinin davranis testleri (Faz 47).
/// </summary>
/// <remarks>
/// <para>
/// Bellek ici depo ile uc SQL saglayicisi ayni senaryolari gecmelidir.
/// </para>
/// <para>
/// 🚨 En degerli test <see cref="Polimorfik_icerik_ANAHTAR_SIRASI_korunarak_geri_okunur"/>
/// olanidir: <c>messages</c> sutunu <c>jsonb</c> yazilirsa PostgreSQL nesne
/// anahtarlarini yeniden siralar, <c>$type</c> ayraci ilk ozellik olmaktan
/// cikar ve okuma <c>JsonException</c> ile duser (K-027). Derleme de diger
/// testler de bunu yakalamaz.
/// </para>
/// </remarks>
public abstract class RunInputStoreContract : IAsyncLifetime
{
    private const string Tenant = "test";

    /// <summary>Sinanan girdi deposu.</summary>
    protected IRunInputStore Store { get; private set; } = null!;

    /// <summary>Test icin bos bir girdi deposu uretir.</summary>
    /// <returns>Kullanima hazir depo.</returns>
    protected abstract ValueTask<IRunInputStore> CreateStoreAsync();

    /// <summary>
    /// Girdi yazilmadan once, verilen kimlikte bir calistirma satiri acar.
    /// </summary>
    /// <param name="runId">Calistirma kimligi.</param>
    /// <param name="tenantId">Kiraci kimligi.</param>
    /// <returns>Tamamlanma gorevi.</returns>
    /// <remarks>
    /// SQL uygulamalarinda <c>run_inputs.run_id</c> <c>runs</c> tablosuna
    /// yabanci anahtardir; bellek ici uygulamada boyle bir bag yoktur ve kanca
    /// hicbir sey yapmaz.
    /// </remarks>
    protected virtual ValueTask PrepareRunAsync(Guid runId, string tenantId) => default;

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
    public async Task Yazilan_girdi_aynen_geri_okunur()
    {
        var runId = await NewRunAsync();

        await Store.SaveAsync(Record(runId, [new ChatMessage(ChatRole.User, "istanbul hava durumu")]));

        var read = await Store.GetAsync(Tenant, runId);

        read.ShouldNotBeNull();
        read!.RunId.ShouldBe(runId);
        read.TenantId.ShouldBe(Tenant);
        read.Messages.Count.ShouldBe(1);
        read.Messages[0].Role.ShouldBe(ChatRole.User);
        read.Messages[0].Text.ShouldBe("istanbul hava durumu");
    }

    [Fact]
    public async Task Kaydi_olmayan_calistirma_null_doner()
        => (await Store.GetAsync(Tenant, AgentPrismId.NewId())).ShouldBeNull();

    [Fact]
    public async Task Ikinci_yazim_YOK_SAYILIR()
    {
        var runId = await NewRunAsync();

        await Store.SaveAsync(Record(runId, [new ChatMessage(ChatRole.User, "ilk")]));
        await Store.SaveAsync(Record(runId, [new ChatMessage(ChatRole.User, "ikinci")]));

        // 🚨 Kuyruga alinan bir calistirma (Faz 46) AYNI kimlikle iki kez baslar;
        // girdi degismemelidir.
        var read = await Store.GetAsync(Tenant, runId);

        read!.Messages[0].Text.ShouldBe("ilk");
    }

    [Fact]
    public async Task Polimorfik_icerik_ANAHTAR_SIRASI_korunarak_geri_okunur()
    {
        var runId = await NewRunAsync();

        // Metin + goruntu referansi + tool sonucu: ucu de AYRI birer AIContent
        // turudur ve `$type` ayraci olmadan geri okunamaz.
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, "Kisa yanit ver."),
            new(
                ChatRole.User,
                [
                    new TextContent("bu goruntuyu acikla"),
                    new UriContent("https://ornek/gorsel.png", "image/png"),
                ]),
            new(ChatRole.Tool, [new FunctionResultContent("call-1", "23 derece")]),
        };

        await Store.SaveAsync(Record(runId, messages));

        var read = await Store.GetAsync(Tenant, runId);

        read.ShouldNotBeNull();
        read!.Messages.Count.ShouldBe(3);
        read.Messages[0].Role.ShouldBe(ChatRole.System);

        var user = read.Messages[1];

        user.Contents.OfType<TextContent>().Single().Text.ShouldBe("bu goruntuyu acikla");
        user.Contents.OfType<UriContent>().Single().MediaType.ShouldBe("image/png");

        var toolResult = read.Messages[2].Contents.OfType<FunctionResultContent>().Single();

        toolResult.CallId.ShouldBe("call-1");
        toolResult.Result?.ToString().ShouldBe("23 derece");
    }

    [Fact]
    public async Task Baska_kiracinin_girdisi_okunamaz()
    {
        var runId = await NewRunAsync("tenant-a");

        await Store.SaveAsync(
            Record(runId, [new ChatMessage(ChatRole.User, "gizli")], tenantId: "tenant-a"));

        // "Yok" ile "baskasinin" cagiran icin AYNI sonuctur; varlik sizmaz.
        (await Store.GetAsync("tenant-b", runId)).ShouldBeNull();
        (await Store.GetAsync("tenant-a", runId)).ShouldNotBeNull();
    }

    private async ValueTask<Guid> NewRunAsync(string tenantId = Tenant)
    {
        var runId = AgentPrismId.NewId();

        await PrepareRunAsync(runId, tenantId);

        return runId;
    }

    private static RunInputRecord Record(
        Guid runId,
        IReadOnlyList<ChatMessage> messages,
        string tenantId = Tenant)
        => new()
        {
            RunId = runId,
            TenantId = tenantId,
            Messages = messages,
            CreatedAt = DateTimeOffset.UtcNow,
        };
}
