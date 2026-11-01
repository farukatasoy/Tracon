using Tracon.Sqlite.IntegrationTests.Infrastructure;

namespace Tracon.Sqlite.IntegrationTests;

/// <summary>
/// A currency label is stored in upper case, whatever case the caller wrote it in.
/// </summary>
/// <remarks>
/// The cost queries upper-case the column in SQL so that <c>usd</c> and <c>USD</c> are one currency, and that
/// function follows the locale of the database: under a Turkish locale <c>UPPER('inr')</c> is <c>İNR</c>. A value
/// stored in upper case reaches it unchanged in every locale.
/// </remarks>
public sealed class SqlCurrencyCaseTests(SqliteSchemaFixture schema) : IClassFixture<SqliteSchemaFixture>
{
    [Theory]
    [InlineData("inr", "INR")]
    [InlineData("Idr", "IDR")]
    [InlineData("USD", "USD")]
    public async Task A_run_cost_currency_is_stored_in_upper_case(string written, string stored)
    {
        await schema.ResetAsync();

        var runs = schema.Context.Runs;
        var runId = TraconId.NewId();

        await runs.StartRunAsync(new RunStartInfo { RunId = runId, AgentName = "agent", StartedAt = DateTimeOffset.UtcNow });
        await runs.CompleteRunAsync(new RunCompletion
        {
            RunId = runId,
            Status = RunStatus.Completed,
            CompletedAt = DateTimeOffset.UtcNow,
            Cost = new RunCost { InputCost = 1m, Currency = written, Source = PricingSource.Configuration },
        });

        var run = (await runs.GetRunAsync(runId)).ShouldNotBeNull();

        run.Cost.ShouldNotBeNull().Currency.ShouldBe(stored);
    }
}
