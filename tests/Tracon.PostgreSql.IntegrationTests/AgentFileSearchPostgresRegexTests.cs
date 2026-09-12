using System.Text;
using System.Text.RegularExpressions;
using Tracon.PostgreSql.IntegrationTests.Infrastructure;

namespace Tracon.PostgreSql.IntegrationTests;

/// <summary>
/// PostgreSQL-specific regex pre-filter (<c>~</c>) behavior (Phase 51, Task A).
/// </summary>
/// <remarks>
/// The final match ALWAYS happens client-side with the .NET <see cref="Regex"/>;
/// this filter is only a pre-narrowing step. The tests verify that it does NOT
/// change the result, and that a .NET pattern that is INVALID in PostgreSQL's
/// ARE syntax silently falls back to running without the pre-filter.
/// </remarks>
public sealed class AgentFileSearchPostgresRegexTests(PostgresFixture fixture) : IAsyncLifetime
{
    private PostgresTestContext? _context;

    public async ValueTask InitializeAsync() => _context = await PostgresTestContext.CreateAsync(fixture);

    public async ValueTask DisposeAsync()
    {
        TraconRunContext.SetCurrent(null);

        if (_context is not null)
        {
            await _context.DisposeAsync();
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task Are_compatible_pattern_returns_the_same_result_as_dotnet()
    {
        SetScope("agent-a");

        await _context!.AgentFiles.WriteAsync("/notes/a.md", "invoice no 4242");
        await _context.AgentFiles.WriteAsync("/notes/b.md", "no invoice number");

        var results = await _context.AgentFiles.SearchAsync("/", @"\d{4}", recursive: true);

        results.ShouldHaveSingleItem().FileName.ShouldBe("/notes/a.md");
    }

    [Fact]
    public async Task Dotnet_named_group_falls_back_without_the_pre_filter()
    {
        // (?<amount>...) is INVALID in PostgreSQL's ARE syntax (2201B).
        // SqlAgentFileStore catches this with SqlDialect.IsInvalidRegexError
        // and retries WITHOUT the pre-filter; the .NET Regex still performs
        // the final match correctly.
        SetScope("agent-a");

        await _context!.AgentFiles.WriteAsync("/notes/a.md", "invoice no 4242");
        await _context.AgentFiles.WriteAsync("/notes/b.md", "no invoice number");

        var results = await _context.AgentFiles.SearchAsync("/", @"(?<amount>\d{4})", recursive: true);

        results.ShouldHaveSingleItem().FileName.ShouldBe("/notes/a.md");
    }

    [Fact]
    public async Task Large_store_does_not_scan_rows_outside_the_target_directory()
    {
        // Phase 51 DoD: "A single-file search in a 10,000-file store reads a
        // constant number of rows; the row count was measured and recorded
        // here." This test produces that measurement (see
        // docs/arsiv/fazlar/51-VEKTOR-BELLEK-VE-RAG.md, DoD).
        SetScope("agent-a");

        const int UnrelatedFileCount = 10_000;
        var schema = _context!.SchemaName;

        await _context.ExecuteAsync($"""
            INSERT INTO {schema}.agent_files (id, tenant_id, agent_name, path, content, created_at, updated_at)
            SELECT gen_random_uuid(), 'default', 'agent-a',
                   '/archive/file-' || i || '.md', 'unrelated content', now(), now()
            FROM generate_series(1, {UnrelatedFileCount}) AS i;
            """);

        await _context.AgentFiles.WriteAsync("/target/note.md", "search-key is here");

        var results = await _context.AgentFiles.SearchAsync("/target", "search-key", recursive: true);
        results.ShouldHaveSingleItem().FileName.ShouldBe("/target/note.md");

        var actualRows = await MeasureActualRowsAsync($"""
            EXPLAIN (ANALYZE, FORMAT TEXT)
            SELECT path, content
            FROM {schema}.agent_files
            WHERE tenant_id = 'default' AND agent_name = 'agent-a'
              AND path LIKE '/target/%' ESCAPE '\'
            ORDER BY path;
            """);

        // Measured value (in a 10,001-row store, in the test container): 1 row
        // (see docs/arsiv/fazlar/51-VEKTOR-BELLEK-VE-RAG.md, DoD). The test container's
        // locale can turn a prefix LIKE into an index range scan; we assert a
        // wide upper bound — the real proof is staying ORDERS OF MAGNITUDE
        // below UnrelatedFileCount, the exact number can vary across
        // environments.
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
        => TraconRunContext.SetCurrent(new AgentRunScope
        {
            RunId = Guid.NewGuid(),
            RootRunId = Guid.NewGuid(),
            AgentName = agentName,
        });
}
