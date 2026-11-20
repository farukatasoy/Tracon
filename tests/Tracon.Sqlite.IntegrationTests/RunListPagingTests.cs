using Tracon.Sqlite.IntegrationTests.Infrastructure;

namespace Tracon.Sqlite.IntegrationTests;

/// <summary>
/// A page of the run list is the same slice of the list whatever its offset, with the tree totals of each row.
/// </summary>
/// <remarks>
/// The page is cut before the correlated subqueries of the column list run, so the rows the offset skips cost no
/// subquery. The cut sits in a derived table, which promises no order: these cases hold the order, the page borders and
/// the totals of rows that have children to the full list read in one piece.
/// </remarks>
public sealed class RunListPagingTests(SqliteFixture fixture) : IAsyncLifetime
{
    private const int RootRuns = 40;

    private SqliteTestContext _context = null!;

    /// <inheritdoc />
    public async ValueTask InitializeAsync() => _context = await SqliteTestContext.CreateAsync(fixture);

    /// <inheritdoc />
    public async ValueTask DisposeAsync() => await _context.DisposeAsync();

    [Theory]
    [InlineData(0, 10)]
    [InlineData(10, 10)]
    [InlineData(35, 10)]
    [InlineData(39, 5)]
    [InlineData(40, 5)]
    [InlineData(500, 10)]
    public async Task A_page_is_the_slice_of_the_whole_list_it_stands_for(int skip, int take)
    {
        await SeedAsync();

        var all = await QueryAsync(0, 1_000);
        var page = await QueryAsync(skip, take);

        all.Count.ShouldBe(RootRuns);
        page.Select(Summary).ShouldBe(all.Skip(skip).Take(take).Select(Summary));
    }

    [Fact]
    public async Task The_list_is_newest_first_and_a_row_carries_the_totals_of_its_own_tree()
    {
        await SeedAsync();

        var all = await QueryAsync(0, 1_000);

        all.Select(static run => run.StartedAt).ShouldBe(all.Select(static run => run.StartedAt).OrderByDescending(static at => at));

        // The run started last has no children; the one before it has one, and so on around the cycle of four.
        all.Select(static run => run.ChildRunCount).Take(8).ShouldBe([0, 1, 2, 3, 0, 1, 2, 3]);
        all.ShouldAllBe(run => (run.TreeUsage == null ? 0L : run.TreeUsage.TotalTokens ?? 0L) == 10L * (1 + run.ChildRunCount));
    }

    private static string Summary(RunRecord run)
        => $"{run.Id}|{run.StartedAt:O}|{run.ChildRunCount}|{run.TreeUsage?.TotalTokens}";

    private async Task<IReadOnlyList<RunRecord>> QueryAsync(int skip, int take)
        => await _context.Runs.QueryRunsAsync(new RunQuery { TenantId = _context.TenantContext.TenantId, Skip = skip, Take = take });

    /// <summary>Forty root runs one second apart, the n-th with n mod 4 children, each run spending ten tokens.</summary>
    private async Task SeedAsync()
    {
        var tenant = _context.TenantContext.TenantId;
        var begin = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);

        for (var n = 0; n < RootRuns; n++)
        {
            var rootId = TraconId.NewId();
            var startedAt = begin.AddSeconds(RootRuns - n);

            await StartAndCompleteAsync(rootId, null, null, 0, startedAt, tenant);

            for (var child = 0; child < n % 4; child++)
            {
                await StartAndCompleteAsync(TraconId.NewId(), rootId, rootId, 1, startedAt.AddMilliseconds(child + 1), tenant);
            }
        }
    }

    private async Task StartAndCompleteAsync(Guid id, Guid? parent, Guid? root, int depth, DateTimeOffset startedAt, string tenant)
    {
        await _context.Runs.StartRunAsync(new RunStartInfo
        {
            RunId = id,
            AgentName = "paging-agent",
            StartedAt = startedAt,
            TenantId = tenant,
            ParentRunId = parent,
            RootRunId = root,
            Depth = depth,
        });

        await _context.Runs.CompleteRunAsync(new RunCompletion
        {
            RunId = id,
            Status = RunStatus.Completed,
            CompletedAt = startedAt.AddSeconds(1),
            TenantId = tenant,
            Usage = new RunUsage { InputTokens = 4, OutputTokens = 6, TotalTokens = 10 },
        });
    }
}
