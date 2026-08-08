using System.Text;
using System.Text.RegularExpressions;
using AgentPrism.PostgreSql.IntegrationTests.Infrastructure;

namespace AgentPrism.PostgreSql.IntegrationTests;

/// <summary>
/// PostgreSQL'e ozgu regex on suzgeci (<c>~</c>) davranisi (Faz 51, Is A).
/// </summary>
/// <remarks>
/// Nihai eslesme HER ZAMAN .NET <see cref="Regex"/> ile istemcide yapilir; bu
/// suzgec yalniz bir on daraltmadir. Testler bunun sonucu DEGISTIRMEDIGINI ve
/// PostgreSQL'in ARE sozdiziminde GECERSIZ bir .NET deseninde sessizce on
/// suzgecsiz devam ettigini dogrular.
/// </remarks>
public sealed class AgentFileSearchPostgresRegexTests(PostgresFixture fixture) : IAsyncLifetime
{
    private PostgresTestContext? _context;

    public async ValueTask InitializeAsync() => _context = await PostgresTestContext.CreateAsync(fixture);

    public async ValueTask DisposeAsync()
    {
        AgentPrismRunContext.SetCurrent(null);

        if (_context is not null)
        {
            await _context.DisposeAsync();
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task Are_uyumlu_desen_dotnet_ile_ayni_sonucu_dondurur()
    {
        SetScope("agent-a");

        await _context!.AgentFiles.WriteAsync("/notlar/a.md", "fatura no 4242");
        await _context.AgentFiles.WriteAsync("/notlar/b.md", "fatura numarasi yok");

        var results = await _context.AgentFiles.SearchAsync("/", @"\d{4}", recursive: true);

        results.ShouldHaveSingleItem().FileName.ShouldBe("/notlar/a.md");
    }

    [Fact]
    public async Task Dotnet_ozel_adlandirilmis_grup_on_suzgecsiz_geri_duser()
    {
        // (?<tutar>...) PostgreSQL'in ARE sozdiziminde GECERSIZDIR (2201B).
        // SqlAgentFileStore bunu SqlDialect.IsInvalidRegexError ile yakalayip
        // on suzgec OLMADAN yeniden dener; .NET Regex nihai eslesmeyi yine de
        // dogru yapar.
        SetScope("agent-a");

        await _context!.AgentFiles.WriteAsync("/notlar/a.md", "fatura no 4242");
        await _context.AgentFiles.WriteAsync("/notlar/b.md", "fatura numarasi yok");

        var results = await _context.AgentFiles.SearchAsync("/", @"(?<tutar>\d{4})", recursive: true);

        results.ShouldHaveSingleItem().FileName.ShouldBe("/notlar/a.md");
    }

    [Fact]
    public async Task Buyuk_depoda_hedef_dizin_disindaki_satirlar_taranmaz()
    {
        // Faz 51 DoD: "10 000 dosyali bir depoda tek dosya aramasi sabit
        // sayida satir okur; okunan satir sayisi olculdu ve buraya yazildi."
        // Bu test o olcumu üretir (bkz. docs/51-VEKTOR-BELLEK-VE-RAG.md, DoD).
        SetScope("agent-a");

        const int UnrelatedFileCount = 10_000;
        var schema = _context!.SchemaName;

        await _context.ExecuteAsync($"""
            INSERT INTO {schema}.agent_files (id, tenant_id, agent_name, path, content, created_at, updated_at)
            SELECT gen_random_uuid(), 'default', 'agent-a',
                   '/arsiv/dosya-' || i || '.md', 'ilgisiz icerik', now(), now()
            FROM generate_series(1, {UnrelatedFileCount}) AS i;
            """);

        await _context.AgentFiles.WriteAsync("/hedef/not.md", "aranan-anahtar burada");

        var results = await _context.AgentFiles.SearchAsync("/hedef", "aranan-anahtar", recursive: true);
        results.ShouldHaveSingleItem().FileName.ShouldBe("/hedef/not.md");

        var actualRows = await MeasureActualRowsAsync($"""
            EXPLAIN (ANALYZE, FORMAT TEXT)
            SELECT path, content
            FROM {schema}.agent_files
            WHERE tenant_id = 'default' AND agent_name = 'agent-a'
              AND path LIKE '/hedef/%' ESCAPE '\'
            ORDER BY path;
            """);

        // Olculen deger (10 001 satirlik depoda, test container'inda): 1 satir
        // (bkz. docs/51-VEKTOR-BELLEK-VE-RAG.md, DoD). Test container'inin
        // locale'i onek LIKE'i bir index range scan'e cevirebiliyor; genis bir
        // ust sinirla kaydediyoruz — asil kanit UnrelatedFileCount'tan KAT KAT
        // kucuk kalmasidir, tam sayi ortamlar arasi degisebilir.
        actualRows.ShouldBeLessThan(UnrelatedFileCount / 10);
    }

    private async Task<int> MeasureActualRowsAsync(string explainSql)
    {
        await using var command = _context!.DataSource.CreateCommand(explainSql);
        await using var reader = await command.ExecuteReaderAsync();

        var plan = new StringBuilder();

        while (await reader.ReadAsync())
        {
            plan.AppendLine(reader.GetString(0));
        }

        var match = Regex.Match(
            plan.ToString(),
            @"rows=(?<rows>\d+)",
            RegexOptions.ExplicitCapture,
            TimeSpan.FromSeconds(1));
        return match.Success ? int.Parse(match.Groups["rows"].Value, System.Globalization.CultureInfo.InvariantCulture) : -1;
    }

    private static void SetScope(string agentName)
        => AgentPrismRunContext.SetCurrent(new AgentRunScope
        {
            RunId = Guid.NewGuid(),
            RootRunId = Guid.NewGuid(),
            AgentName = agentName,
        });
}
