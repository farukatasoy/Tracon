using System.Globalization;
using AgentPrism.Sqlite.IntegrationTests.Infrastructure;

namespace AgentPrism.Sqlite.IntegrationTests;

/// <summary>
/// Konusma dallandirmasinin depo davranisi (Faz 47).
/// </summary>
/// <remarks>
/// SQLite secildi cunku uc SQL saglayicisinin en ucuz kosanidir ve sorgular
/// paylasilan katmandadir (<c>SqlConversationBranchStore</c>). Kiraci suzgeci
/// ve <c>INSERT … SELECT</c> mantigi diyalektten bagimsizdir.
/// </remarks>
public sealed class ConversationBranchTests(SqliteFixture fixture)
{
    private const string Tenant = "default";

    /// <summary>Bos bir JSON nesnesi. Ham SQL icinde suslu parantez kacisini onler.</summary>
    private const string EmptyJsonObject = "{}";

    [Fact]
    public async Task Belirli_bir_noktaya_kadar_kopyalanir()
    {
        await using var context = await SqliteTestContext.CreateAsync(fixture);
        var parent = await SeedConversationAsync(context, itemCount: 4);

        var branch = await context.ConversationBranches.BranchAsync(Tenant, parent, upToSequence: 1);

        branch.ShouldNotBeNull();
        branch!.Value.CopiedItemCount.ShouldBe(2);
        branch.Value.BranchFromSequence.ShouldBe(1);

        var copied = await CountItemsAsync(context, branch.Value.ConversationId);

        copied.ShouldBe(2);
    }

    [Fact]
    public async Task Sinir_verilmezse_konusmanin_TAMAMI_kopyalanir()
    {
        await using var context = await SqliteTestContext.CreateAsync(fixture);
        var parent = await SeedConversationAsync(context, itemCount: 3);

        var branch = await context.ConversationBranches.BranchAsync(Tenant, parent, upToSequence: null);

        branch!.Value.CopiedItemCount.ShouldBe(3);
        branch.Value.BranchFromSequence.ShouldBe(2);
    }

    [Fact]
    public async Task Dala_yazmak_ANA_konusmayi_degistirmez()
    {
        await using var context = await SqliteTestContext.CreateAsync(fixture);
        var parent = await SeedConversationAsync(context, itemCount: 3);

        var branch = await context.ConversationBranches.BranchAsync(Tenant, parent, upToSequence: 1);

        // Dala yeni bir oge yaz.
        await InsertItemAsync(context, branch!.Value.ConversationId, sequence: 2, text: "dalda yeni");

        // 🚨 Kopyalama tasariminin butun degeri budur: iki konusma birbirinden
        // BAGIMSIZDIR. Isaretci zinciri olsaydi dala yazmak ana konusmanin
        // okumasini da degistirirdi.
        (await CountItemsAsync(context, parent)).ShouldBe(3);
        (await CountItemsAsync(context, branch.Value.ConversationId)).ShouldBe(3);
    }

    [Fact]
    public async Task Bos_sinir_gecerli_ama_bos_bir_dal_acar()
    {
        await using var context = await SqliteTestContext.CreateAsync(fixture);
        var parent = await SeedConversationAsync(context, itemCount: 2);

        // seq = -1: hicbir oge kopyalanmaz ama konusma satiri acilir. Bos bir
        // dal gecerlidir — kullanici konusmayi bastan baslatmak isteyebilir.
        var branch = await context.ConversationBranches.BranchAsync(Tenant, parent, upToSequence: -1);

        branch!.Value.CopiedItemCount.ShouldBe(0);
        branch.Value.BranchFromSequence.ShouldBe(-1);
        (await CountItemsAsync(context, branch.Value.ConversationId)).ShouldBe(0);
    }

    [Fact]
    public async Task Dal_isaretcisi_ve_kaynak_ustverisi_kopyalanir()
    {
        await using var context = await SqliteTestContext.CreateAsync(fixture);
        var parent = await SeedConversationAsync(context, itemCount: 2, agentName: "asistan");

        var branch = await context.ConversationBranches.BranchAsync(Tenant, parent, upToSequence: 0);

        var agentName = await context.ScalarAsync<string>(
            $"SELECT agent_name FROM {context.TablePrefix}conversations WHERE id = '{Sql(branch!.Value.ConversationId)}';");
        var parentPointer = await context.ScalarAsync<string>(
            $"SELECT parent_conversation_id FROM {context.TablePrefix}conversations WHERE id = '{Sql(branch.Value.ConversationId)}';");
        var branchFrom = await context.ScalarAsync<long>(
            $"SELECT branch_from_seq FROM {context.TablePrefix}conversations WHERE id = '{Sql(branch.Value.ConversationId)}';");

        agentName.ShouldBe("asistan");
        parentPointer.ShouldNotBeNull();
        Guid.Parse(parentPointer!, CultureInfo.InvariantCulture).ShouldBe(parent);
        branchFrom.ShouldBe(0);
    }

    [Fact]
    public async Task Ana_konusma_silinince_dal_YASAR()
    {
        await using var context = await SqliteTestContext.CreateAsync(fixture);
        var parent = await SeedConversationAsync(context, itemCount: 2);

        var branch = await context.ConversationBranches.BranchAsync(Tenant, parent, upToSequence: null);

        await context.ExecuteAsync($"DELETE FROM {context.TablePrefix}conversations WHERE id = '{Sql(parent)}';");

        // Dal yasamaya devam eder ve ogeleri KENDISINDEDIR. Isaretci artik
        // cozulemeyen bir kokeni gosterir; hicbir okuma yolu onu JOIN'lemez.
        (await CountItemsAsync(context, branch!.Value.ConversationId)).ShouldBe(2);
    }

    [Fact]
    public async Task Baska_kiracinin_konusmasi_dallandirilamaz()
    {
        await using var context = await SqliteTestContext.CreateAsync(fixture);
        var parent = await SeedConversationAsync(context, itemCount: 2);

        (await context.ConversationBranches.BranchAsync("baska-kiraci", parent, upToSequence: null))
            .ShouldBeNull();
    }

    [Fact]
    public async Task Olmayan_konusma_null_doner()
    {
        await using var context = await SqliteTestContext.CreateAsync(fixture);

        (await context.ConversationBranches.BranchAsync(Tenant, AgentPrismId.NewId(), upToSequence: null))
            .ShouldBeNull();
    }

    [Fact]
    public async Task Bin_ogelik_konusma_dallandirilabilir()
    {
        // Acik Soru 4 buyuk bir konusmanin kopyalanma maliyetinin OLCULMESINI
        // istiyordu. Kopyalama tek islemde satir satir yazar (uuid v7 kimligi
        // uygulamada uretilir, bkz. SqlConversationBranchStore); bu test o
        // yolun bin ogede de calistigini ve dogru sayidigini dogrular.
        await using var context = await SqliteTestContext.CreateAsync(fixture);
        var parent = await SeedConversationAsync(context, itemCount: 1_000);

        var branch = await context.ConversationBranches.BranchAsync(Tenant, parent, upToSequence: null);

        branch!.Value.CopiedItemCount.ShouldBe(1_000);
        (await CountItemsAsync(context, branch.Value.ConversationId)).ShouldBe(1_000);
    }

    private static async ValueTask<Guid> SeedConversationAsync(
        SqliteTestContext context,
        int itemCount,
        string agentName = "test-agent")
    {
        var conversationId = AgentPrismId.NewId();
        var now = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);

        await context.ExecuteAsync(
            $"""
            INSERT INTO {context.TablePrefix}conversations (id, tenant_id, agent_name, metadata, created_at, updated_at)
            VALUES ('{Sql(conversationId)}', '{Tenant}', '{agentName}', '{EmptyJsonObject}', '{now}', '{now}');
            """);

        for (var index = 0; index < itemCount; index++)
        {
            await InsertItemAsync(context, conversationId, index, $"mesaj-{index}");
        }

        return conversationId;
    }

    private static async ValueTask InsertItemAsync(
        SqliteTestContext context,
        Guid conversationId,
        long sequence,
        string text)
    {
        var now = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);

        // Icerik polimorfik bir ChatMessage'i taklit eder: `$type` ayraci ILK
        // ozelliktir ve kopyalama sirasinda oldugu gibi tasinmalidir (K-027).
        await context.ExecuteAsync(
            $$"""
            INSERT INTO {{context.TablePrefix}}conversation_items (id, conversation_id, seq, item, created_at)
            VALUES ('{{Sql(AgentPrismId.NewId())}}', '{{Sql(conversationId)}}', {{sequence}},
                    '{"$type":"text","text":"{{text}}"}', '{{now}}');
            """);
    }

    private static async ValueTask<long> CountItemsAsync(SqliteTestContext context, Guid conversationId)
        => await context.ScalarAsync<long>(
            $"SELECT count(*) FROM {context.TablePrefix}conversation_items WHERE conversation_id = '{Sql(conversationId)}';");

    /// <summary>
    /// Bir kimligi SQLite'in SAKLADIGI bicime cevirir.
    /// </summary>
    /// <remarks>
    /// 🚨 <c>Microsoft.Data.Sqlite</c> <see cref="Guid"/> degerlerini BUYUK
    /// harfli, tireli metin olarak yazar (K-191) ve SQLite metin
    /// karsilastirmasi harf buyuklugune duyarlidir. Elle yazilan SQL kucuk
    /// harfli bir kimlik kullanirsa <c>WHERE</c> SESSIZCE hicbir satir bulmaz —
    /// bu test dosyasi ilk kosusunda tam olarak boyle dustu.
    /// </remarks>
    private static string Sql(Guid id) => id.ToString("D", CultureInfo.InvariantCulture).ToUpperInvariant();
}
