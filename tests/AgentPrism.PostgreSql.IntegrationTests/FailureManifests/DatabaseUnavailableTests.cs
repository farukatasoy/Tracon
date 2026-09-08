using AgentPrism.PostgreSql.IntegrationTests.Infrastructure;
using AgentPrism.Testing.Contracts.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Testcontainers.PostgreSql;

namespace AgentPrism.PostgreSql.IntegrationTests.FailureManifests;

/// <summary>
/// Failure manifest: <strong>the database is unreachable</strong> (Phase 157).
/// </summary>
/// <remarks>
/// <para>
/// The written expectation, which the tests below verify rather than define:
/// </para>
/// <list type="bullet">
///   <item>
///     <description>
///       <strong>Reading</strong> fails loudly. A store call raises the
///       provider's own exception; it never degrades into an empty list or a
///       <see langword="null"/> record, because an empty answer is
///       indistinguishable from "this tenant has no data" and would be read as
///       a successful query.
///     </description>
///   </item>
///   <item>
///     <description>
///       <strong>Run recording</strong> fails quietly. Recording is
///       observability, and observability must not break functionality: the
///       writer disables itself for that run, logs, and the run keeps going.
///       This is the one place where swallowing is the correct behaviour.
///     </description>
///   </item>
///   <item>
///     <description>
///       <strong>The queue</strong> stalls without dying. A worker process
///       whose database disappears stays up, keeps ticking, and resumes when
///       the database returns; a failed tick must not take the process with
///       it. The job row survives and nothing is reported as finished - the
///       outage costs the attempt that was in flight, the same way a crash
///       does, not the job.
///     </description>
///   </item>
/// </list>
/// <para>
/// 🚨 This class runs its own PostgreSQL container instead of the shared
/// assembly fixture, and stops it on purpose. Stopping the shared one would
/// fail every other class in the assembly for a reason that has nothing to do
/// with them.
/// </para>
/// </remarks>
[Collection(TwoProcessFailureProof.Name)]
public sealed class DatabaseUnavailableTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("pgvector/pgvector:pg18")
        .WithDatabase("agentprism_outage")
        .WithCleanUp(true)
        .Build();

    private string _logDirectory = null!;

    private string LogPath => Path.Combine(_logDirectory, "executions.log");

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        _logDirectory = Directory.CreateTempSubdirectory("agentprism-outage-").FullName;

        await _container.StartAsync();

        await using var connection = new NpgsqlConnection(_container.GetConnectionString());
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "CREATE EXTENSION IF NOT EXISTS vector;";
        await command.ExecuteNonQueryAsync();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _container.DisposeAsync();

        try
        {
            Directory.Delete(_logDirectory, recursive: true);
        }
        catch (IOException)
        {
            // The temp directory is reclaimed by the operating system anyway.
        }
    }

    [Fact]
    public async Task A_read_fails_loudly_instead_of_returning_an_empty_result()
    {
        var schemaName = PostgresTestContext.NewSchemaName();
        await using var context = await MigratedAsync(schemaName);

        var job = await context.Jobs.EnqueueAsync(TestData.Job(), ["only-item"]);

        // Proof that the query works while the database is up - otherwise an
        // "it threw" assertion below would prove nothing about the outage.
        (await context.Jobs.GetAsync(job.TenantId, job.Id)).ShouldNotBeNull();

        await _container.StopAsync();

        await Should.ThrowAsync<Exception>(async () => await context.Jobs.GetAsync(job.TenantId, job.Id));
        await Should.ThrowAsync<Exception>(async () => await context.Runs.QueryRunsAsync(new RunQuery()));
    }

    [Fact]
    public async Task Run_recording_disables_itself_instead_of_failing_the_run()
    {
        var schemaName = PostgresTestContext.NewSchemaName();
        await using var context = await MigratedAsync(schemaName);

        await _container.StopAsync();

        var runId = Guid.NewGuid();

        var writer = new RunEventWriter(
            context.Runs,
            new AgentPrismRunRecordingOptions(),
            NullLogger.Instance,
            runId);

        // No throw: the run would continue past this point in production.
        await writer.StartAsync(TestData.Run(runId), "the user's question");
        var appended = await writer.AppendAsync(new RunEventDraft(RunEventType.MessageDelta) { Text = "delta" });
        await writer.CompleteAsync(RunStatus.Completed);

        // The event is still PRODUCED for the client; only persistence stopped.
        appended.ShouldNotBeNull();
        writer.IsDisabled.ShouldBeTrue("the writer must record that it gave up, not pretend it wrote.");
    }

    [Fact]
    public async Task A_worker_process_survives_the_outage_and_the_job_is_not_lost()
    {
        var schemaName = PostgresTestContext.NewSchemaName();
        await using var context = await MigratedAsync(schemaName);

        var job = await context.Jobs.EnqueueAsync(
            TestData.Job() with { HandlerKey = WorkerHarnessContract.HandlerKey },
            ["only-item"]);

        await using var worker = await WorkerProcessHost.StartAsync(new WorkerProcessOptions
        {
            Name = "worker-outage",
            ConnectionString = _container.GetConnectionString(),
            SchemaName = schemaName,
            ExecutionLogPath = LogPath,
            LeaseDuration = TimeSpan.FromSeconds(30),
            PollInterval = TimeSpan.FromMilliseconds(250),
            WorkDuration = TimeSpan.FromMinutes(5),
        });

        await _container.StopAsync();

        // Several poll intervals with no database at all. Every tick fails.
        await Task.Delay(TimeSpan.FromSeconds(3));

        worker.HasExited.ShouldBeFalse(
            $"a failed queue tick must not take the process down.{Environment.NewLine}{worker.Output}");

        await _container.StartAsync();

        // 🚨 The claim is deliberately narrow. The worker very likely leased the
        // job in the 250 ms before the database went away, so asserting
        // "still Pending" would be asserting a race. What IS true, and what an
        // operator needs, is that the row survived and nothing was reported as
        // finished: the outage cost the in-flight attempt, not the job.
        await using var recovered = PostgresTestContext.Create(_container.GetConnectionString(), schemaName);
        var afterOutage = await WaitForAsync(recovered, job, TimeSpan.FromSeconds(30));

        afterOutage.ShouldNotBeNull();
        afterOutage.Status.ShouldNotBe(JobStatus.Completed);
        afterOutage.Status.ShouldNotBe(JobStatus.Failed);
        afterOutage.DoneItems.ShouldBe(0);
        afterOutage.FailedItems.ShouldBe(0);
    }

    private async Task<PostgresTestContext> MigratedAsync(string schemaName)
    {
        var context = PostgresTestContext.Create(_container.GetConnectionString(), schemaName);

        try
        {
            await context.Migrations.ApplyAsync();

            return context;
        }
        catch
        {
            await context.DisposeAsync();
            throw;
        }
    }

    private static async Task<JobRecord?> WaitForAsync(PostgresTestContext context, JobRecord job, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        Exception? last = null;

        while (DateTimeOffset.UtcNow < deadline)
        {
            try
            {
                return await context.Jobs.GetAsync(job.TenantId, job.Id);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // The container is up but PostgreSQL may still be starting.
                last = exception;
                await Task.Delay(200);
            }
        }

        throw new TimeoutException($"The database did not come back within {timeout}.", last);
    }
}
