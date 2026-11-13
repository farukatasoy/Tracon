using System.Collections.Concurrent;
using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tracon.SqlServer.IntegrationTests.Infrastructure;

namespace Tracon.SqlServer.IntegrationTests;

/// <summary>
/// Retention in an application that no longer registers an <see cref="IAttachmentStorage"/> keeps the
/// attachment rows that point at external content, and says so once per run.
/// </summary>
/// <remarks>
/// <para>
/// The row is the only record of where the bytes are. A plain <c>DELETE</c> of a row with an
/// <c>external_uri</c> would leave the bytes in the storage with nothing that names them. Two stores are
/// built over one database: one WITH the storage, which writes the external attachments, and one WITHOUT,
/// which is the application after the registration was removed.
/// </para>
/// <para>
/// The kept rows are excluded from the batch by the predicate, not skipped after the read: a batch that
/// read them first would be full of rows it will not delete, and the inline rows behind them would never be
/// reached.
/// </para>
/// </remarks>
public sealed class RetentionWithoutAttachmentStorageTests(SqlServerSchemaFixture schema)
    : IClassFixture<SqlServerSchemaFixture>, IAsyncLifetime
{
    private const string Tenant = "tenant-a";

    private readonly RecordingAttachmentStorage _storage = new();
    private readonly ListLoggerProvider _logs = new();
    private ServiceProvider _withStorage = null!;
    private ServiceProvider _withoutStorage = null!;

    private IAttachmentStore ExternalAttachments => _withStorage.GetRequiredService<IAttachmentStore>();

    private IAttachmentStore InlineAttachments => _withoutStorage.GetRequiredService<IAttachmentStore>();

    private IRetentionStore Retention => _withoutStorage.GetRequiredService<IRetentionStore>();

    private string Schema => schema.Context.SchemaName;

    private static DateTimeOffset FutureCutoff => DateTimeOffset.UtcNow.AddDays(1);

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        await schema.ResetAsync();

        _withStorage = Build(storage: _storage);
        _withoutStorage = Build(storage: null);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _withStorage.DisposeAsync();
        await _withoutStorage.DisposeAsync();
    }

    [Fact]
    public async Task Retention_keeps_an_attachment_row_with_external_content_when_no_storage_is_registered()
    {
        var external = await SaveExternalAsync();

        var deleted = await Retention.DeleteBatchAsync(RetentionTargets.Attachments, Tenant, FutureCutoff, batchSize: 100);

        deleted.ShouldBe(0);
        (await ExternalAttachments.GetAsync(Tenant, external.Id)).ShouldNotBeNull("the row is the only record of where the bytes are");
        _storage.Stored.ShouldHaveSingleItem();
        _storage.Deleted.ShouldBeEmpty();
    }

    [Fact]
    public async Task Retention_deletes_the_inline_attachments_and_keeps_the_external_ones()
    {
        var external = await SaveExternalAsync();
        var inline = await SaveInlineAsync();

        var deleted = await Retention.DeleteBatchAsync(RetentionTargets.Attachments, Tenant, FutureCutoff, batchSize: 100);

        deleted.ShouldBe(1);
        (await InlineAttachments.GetAsync(Tenant, inline.Id)).ShouldBeNull();
        (await ExternalAttachments.GetAsync(Tenant, external.Id)).ShouldNotBeNull();
    }

    [Fact]
    public async Task Archive_mode_hands_over_and_deletes_only_the_rows_it_may_delete()
    {
        var external = await SaveExternalAsync();
        var inline = await SaveInlineAsync();
        var archived = new List<ArchiveRow>();

        var deleted = await Retention.ArchiveBatchAsync(
            RetentionTargets.Attachments,
            Tenant,
            FutureCutoff,
            batchSize: 100,
            (rows, _) =>
            {
                archived.AddRange(rows);

                return ValueTask.CompletedTask;
            });

        deleted.ShouldBe(1);
        archived.ShouldHaveSingleItem().Json.ShouldContain(inline.Id.ToString("D", CultureInfo.InvariantCulture), Case.Insensitive);
        (await ExternalAttachments.GetAsync(Tenant, external.Id)).ShouldNotBeNull();
    }

    [Fact]
    public async Task The_preview_count_leaves_out_the_rows_a_run_would_keep()
    {
        await SaveExternalAsync();
        await SaveInlineAsync();

        var count = await Retention.CountOlderThanAsync(RetentionTargets.Attachments, Tenant, FutureCutoff);

        count.ShouldBe(1, "a preview says how many rows a run would delete");
    }

    [Fact]
    public async Task A_run_that_keeps_rows_logs_one_warning_with_the_count_and_no_location_or_tenant()
    {
        // Three external rows are kept behind five inline rows; the batch size forces three batches.
        for (var i = 0; i < 3; i++)
        {
            await SaveExternalAsync();
        }

        for (var i = 0; i < 5; i++)
        {
            await SaveInlineAsync();
        }

        var executor = _withoutStorage.GetRequiredService<RetentionExecutor>();
        await SavePolicyAsync();
        await BackdateAllAsync(days: 30);

        var runs = await executor.RunAsync(Tenant, RetentionTargets.Attachments);

        // Every inline row was reached: the kept rows did not fill a batch.
        runs.ShouldHaveSingleItem().DeletedRows.ShouldBe(5);
        (await AttachmentCountAsync()).ShouldBe(3);

        var warning = _logs.Warnings.Where(static entry => entry.Message.Contains("IAttachmentStorage", StringComparison.Ordinal)).ShouldHaveSingleItem();

        warning.Message.ShouldContain("3");
        warning.Message.ShouldNotContain("recording://", Case.Insensitive);
        warning.Message.ShouldNotContain(Tenant, Case.Insensitive);
    }

    [Fact]
    public async Task A_second_run_logs_again_and_still_keeps_the_rows()
    {
        await SaveExternalAsync();
        var executor = _withoutStorage.GetRequiredService<RetentionExecutor>();
        await SavePolicyAsync();
        await BackdateAllAsync(days: 30);

        await executor.RunAsync(Tenant, RetentionTargets.Attachments);
        await executor.RunAsync(Tenant, RetentionTargets.Attachments);

        _logs.Warnings.Count(static entry => entry.Message.Contains("IAttachmentStorage", StringComparison.Ordinal)).ShouldBe(2);
        (await AttachmentCountAsync()).ShouldBe(1);
        _storage.Deleted.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_run_that_keeps_nothing_logs_nothing()
    {
        await SaveInlineAsync();
        var executor = _withoutStorage.GetRequiredService<RetentionExecutor>();
        await SavePolicyAsync();
        await BackdateAllAsync(days: 30);

        var runs = await executor.RunAsync(Tenant, RetentionTargets.Attachments);

        runs.ShouldHaveSingleItem().DeletedRows.ShouldBe(1);
        _logs.Warnings.Where(static entry => entry.Message.Contains("IAttachmentStorage", StringComparison.Ordinal)).ShouldBeEmpty();
    }

    [Fact]
    public async Task With_the_storage_registered_the_external_rows_are_still_deleted_with_their_content()
    {
        // The rule is for the missing storage only: with the storage back, the row and its bytes go.
        var external = await SaveExternalAsync();

        var deleted = await _withStorage.GetRequiredService<IRetentionStore>()
            .DeleteBatchAsync(RetentionTargets.Attachments, Tenant, FutureCutoff, batchSize: 100);

        deleted.ShouldBe(1);
        (await ExternalAttachments.GetAsync(Tenant, external.Id)).ShouldBeNull();
        _storage.Stored.ShouldBeEmpty();
    }

    private ServiceProvider Build(IAttachmentStorage? storage)
    {
        var services = new ServiceCollection();

        if (storage is not null)
        {
            services.AddSingleton(storage);
        }

        services.AddLogging(builder => builder.AddProvider(_logs));
        services.Configure<TraconRetentionOptions>(static options => options.BatchSize = 2);
        services.AddTracon().UseSqlServer(options =>
        {
            options.ConnectionString = schema.Context.Options.ConnectionString;
            options.SchemaName = schema.Context.SchemaName;
            options.AutoApplyMigrations = false;
        });

        return services.BuildServiceProvider();
    }

    private Task<AttachmentDescriptor> SaveExternalAsync() => SaveAsync(ExternalAttachments);

    private Task<AttachmentDescriptor> SaveInlineAsync() => SaveAsync(InlineAttachments);

    private static async Task<AttachmentDescriptor> SaveAsync(IAttachmentStore store)
        => await store.SaveAsync(new AttachmentContent
        {
            TenantId = Tenant,
            SessionId = null,
            FileName = "photo.png",
            MediaType = "image/png",
            Data = new byte[] { 1, 2, 3 },
        });

    private async Task SavePolicyAsync()
    {
        var now = DateTimeOffset.UtcNow;

        await _withoutStorage.GetRequiredService<IRetentionPolicyStore>().SavePolicyAsync(new RetentionPolicy
        {
            Id = Guid.NewGuid(),
            TenantId = Tenant,
            Target = RetentionTargets.Attachments,
            MaxAgeDays = 7,
            Enabled = true,
            CreatedAt = now,
            UpdatedAt = now,
        });
    }

    private async Task BackdateAllAsync(int days)
        => await schema.Context.ExecuteAsync($"UPDATE {Schema}.attachments SET created_at = DATEADD(day, -{days}, SYSDATETIMEOFFSET());");

    private async Task<long> AttachmentCountAsync()
        => await schema.Context.ScalarAsync<int>($"SELECT COUNT(*) FROM {Schema}.attachments;");

    /// <summary>Collects the log entries of the container that holds it.</summary>
    private sealed class ListLoggerProvider : ILoggerProvider
    {
        private readonly ConcurrentQueue<(LogLevel Level, string Message)> _entries = new();

        public IEnumerable<(LogLevel Level, string Message)> Warnings
            => _entries.Where(static entry => entry.Level == LogLevel.Warning);

        public ILogger CreateLogger(string categoryName) => new ListLogger(_entries);

        public void Dispose()
        {
            // Nothing to release.
        }

        private sealed class ListLogger(ConcurrentQueue<(LogLevel Level, string Message)> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull
                => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
                => entries.Enqueue((logLevel, formatter(state, exception)));
        }
    }
}
