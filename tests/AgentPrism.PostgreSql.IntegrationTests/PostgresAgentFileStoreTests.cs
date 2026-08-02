using AgentPrism.PostgreSql.IntegrationTests.Infrastructure;

namespace AgentPrism.PostgreSql.IntegrationTests;

/// <summary>
/// <see cref="PostgresAgentFileStore"/>'un yol hiyerarsisi, kiraci/agent yalitimi
/// ve arama davranisi.
/// </summary>
/// <remarks>
/// Agent adi arayuzde bir parametre olmadigi icin ambient kapsamdan
/// (<see cref="AgentPrismRunContext"/>) okunur. Kapsam her test GOVDESININ
/// BASINDA kurulur, <c>InitializeAsync</c>'te degil: xunit v3 (MTP) yasam
/// dongusu kancalarini ve test govdesini ayri zamanlanmis islemler olarak
/// calistirabiliyor, bu da <c>AsyncLocal</c> akisini keser — olculdu.
/// </remarks>
public sealed class PostgresAgentFileStoreTests(PostgresFixture fixture) : IAsyncLifetime
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
    public async Task Yazilan_dosya_okunur()
    {
        SetScope("agent-a");

        await _context!.AgentFiles.WriteAsync("/notes/a.md", "merhaba");

        (await _context.AgentFiles.ReadAsync("/notes/a.md")).ShouldBe("merhaba");
    }

    [Fact]
    public async Task Olmayan_dosya_null_doner()
    {
        SetScope("agent-a");

        (await _context!.AgentFiles.ReadAsync("/yok")).ShouldBeNull();
    }

    [Fact]
    public async Task Var_olan_dosya_uzerine_yazilir()
    {
        SetScope("agent-a");

        await _context!.AgentFiles.WriteAsync("/a.md", "birinci");
        await _context.AgentFiles.WriteAsync("/a.md", "ikinci");

        (await _context.AgentFiles.ReadAsync("/a.md")).ShouldBe("ikinci");
    }

    [Fact]
    public async Task Silme_calisir_ve_ikinci_seferde_false_doner()
    {
        SetScope("agent-a");

        await _context!.AgentFiles.WriteAsync("/a.md", "icerik");

        (await _context.AgentFiles.DeleteAsync("/a.md")).ShouldBeTrue();
        (await _context.AgentFiles.FileExistsAsync("/a.md")).ShouldBeFalse();
        (await _context.AgentFiles.DeleteAsync("/a.md")).ShouldBeFalse();
    }

    [Fact]
    public async Task Farkli_agentlar_birbirinin_dosyasini_gormez()
    {
        SetScope("agent-a");
        await _context!.AgentFiles.WriteAsync("/a.md", "agent-a icerigi");

        SetScope("agent-b");
        (await _context.AgentFiles.FileExistsAsync("/a.md")).ShouldBeFalse();
    }

    [Fact]
    public async Task Dizin_listesi_dogrudan_alt_ogeleri_dondurur()
    {
        SetScope("agent-a");

        await _context!.AgentFiles.WriteAsync("/notes/a.md", "a");
        await _context.AgentFiles.WriteAsync("/notes/b.md", "b");
        await _context.AgentFiles.WriteAsync("/notes/alt/c.md", "c");
        await _context.AgentFiles.WriteAsync("/other.md", "d");

        var children = await _context.AgentFiles.ListChildrenAsync("/notes");

        children.Select(static entry => entry.Name)
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ShouldBe(["a.md", "alt", "b.md"]);

        children.Single(static entry => string.Equals(entry.Name, "alt", StringComparison.Ordinal))
            .Type.ShouldBe("directory");
        children.Single(static entry => string.Equals(entry.Name, "a.md", StringComparison.Ordinal))
            .Type.ShouldBe("file");
    }

    [Fact]
    public async Task Arama_eslesen_satiri_dondurur()
    {
        SetScope("agent-a");

        await _context!.AgentFiles.WriteAsync("/notes/a.md", "birinci satir\nfatura numarasi 42\nson satir");
        await _context.AgentFiles.WriteAsync("/notes/b.md", "ilgisiz icerik");

        var results = await _context.AgentFiles.SearchAsync("/", "fatura", recursive: true);

        var match = results.ShouldHaveSingleItem();
        match.FileName.ShouldBe("/notes/a.md");
        match.MatchingLines.ShouldHaveSingleItem().LineNumber.ShouldBe(2);
    }

    private static void SetScope(string agentName)
        => AgentPrismRunContext.SetCurrent(new AgentRunScope
        {
            RunId = Guid.NewGuid(),
            RootRunId = Guid.NewGuid(),
            AgentName = agentName,
        });
}
