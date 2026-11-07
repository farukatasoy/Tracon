using System.Data.Common;
using Tracon.SqlServer.IntegrationTests.Infrastructure;
using Tracon.Testing.Contracts.Storage;

namespace Tracon.SqlServer.IntegrationTests;

/// <summary>
/// A report that finishes no item does not touch the job's row, so it does not wait for whoever holds it and does not make
/// a lease skip the job.
/// </summary>
/// <remarks>
/// A lease with <c>READPAST</c> passes over a row another statement holds. The report statement used to write the job's
/// counters even when the item did not finish (they are added to by zero), so a report of a pending item held the row for
/// the length of the statement and a lease that came at that moment took the next job. The PostgreSQL twin is
/// <c>JobStoreConcurrencyTests</c>.
/// </remarks>
public sealed class JobReportRowTests(SqlServerSchemaFixture schema) : IClassFixture<SqlServerSchemaFixture>
{
    [Fact]
    public async Task A_report_that_finishes_no_item_does_not_wait_for_the_row_of_its_job()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await schema.ResetAsync();
        var jobs = schema.Context.Jobs;
        var job = await jobs.EnqueueAsync(TestData.Job(), ["input"], cancellationToken);

        await using var holder = await schema.Context.DataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await holder.BeginTransactionAsync(cancellationToken);

        await using (var hold = holder.CreateCommand())
        {
            hold.Transaction = transaction;
            hold.CommandText = $"SELECT id FROM [{schema.Context.SchemaName}].jobs WITH (UPDLOCK, ROWLOCK, HOLDLOCK) WHERE id = '{job.Id}';";
            _ = await hold.ExecuteScalarAsync(cancellationToken);
        }

        var report = jobs
            .ReportItemAsync(new JobItemResult { JobId = job.Id, Seq = 0, Status = JobItemStatus.Pending }, cancellationToken)
            .AsTask();

        await report.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);

        await transaction.RollbackAsync(cancellationToken);
    }
}
