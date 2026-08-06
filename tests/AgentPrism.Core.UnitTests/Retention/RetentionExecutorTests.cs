using AgentPrism.Core.UnitTests.Fakes;

namespace AgentPrism.Core.UnitTests.Retention;

/// <summary>
/// <see cref="RetentionExecutor"/>'in onizleme/kosu orkestrasyonunun testleri.
/// </summary>
public sealed class RetentionExecutorTests
{
    private const string Tenant = "acme";

    [Fact]
    public async Task Bilinmeyen_hedef_ArgumentException_firlatir()
    {
        var (executor, _, _) = Build();

        await Should.ThrowAsync<ArgumentException>(async ()
            => await executor.PreviewAsync(Tenant, "not_a_real_table"));
    }

    [Fact]
    public async Task Onizleme_hicbir_satir_silmez()
    {
        var (executor, store, dataStore) = Build();

        await SavePolicyAsync(store, RetentionTargets.RunEvents, maxAgeDays: 30);
        dataStore.CountToReturn = 42;

        var preview = await executor.PreviewAsync(Tenant, RetentionTargets.RunEvents);

        preview.Count.ShouldBe(1);
        preview[0].Enabled.ShouldBeTrue();
        preview[0].MatchingRows.ShouldBe(42);
        dataStore.DeleteCalls.ShouldBe(0);
    }

    [Fact]
    public async Task Politikasi_olmayan_hedef_onizlemede_kapali_gorunur()
    {
        var (executor, _, _) = Build();

        var preview = await executor.PreviewAsync(Tenant, RetentionTargets.RunEvents);

        preview[0].Enabled.ShouldBeFalse();
        preview[0].MaxAgeDays.ShouldBeNull();
        preview[0].MatchingRows.ShouldBe(0);
    }

    [Fact]
    public async Task Arsiv_istenip_sink_kayitli_degilse_hicbir_satir_silinmez()
    {
        var (executor, store, dataStore) = Build(archiveSink: null);

        await SavePolicyAsync(store, RetentionTargets.RunEvents, maxAgeDays: 30, archive: true);
        dataStore.DeleteBatchSizes.Enqueue(100);

        var runs = await executor.RunAsync(Tenant, RetentionTargets.RunEvents);

        runs.Count.ShouldBe(1);
        dataStore.DeleteCalls.ShouldBe(0);
        dataStore.ArchiveReadCalls.ShouldBe(0);

        var history = await store.ListRunsAsync(Tenant, RetentionTargets.RunEvents, 0, 10);
        history[0].CompletedAt.ShouldNotBeNull();
        history[0].Error.ShouldBeNull();
        history[0].DeletedRows.ShouldBe(0);
    }

    [Fact]
    public async Task Arsiv_sink_kayitliysa_silmeden_once_yazilir()
    {
        var sink = new RecordingArchiveSink();
        var (executor, store, dataStore) = Build(archiveSink: sink);

        await SavePolicyAsync(store, RetentionTargets.RunEvents, maxAgeDays: 30, archive: true);
        dataStore.ArchiveRowsToReturn = [new ArchiveRow { Json = "{}" }];
        dataStore.DeleteBatchSizes.Enqueue(1);
        dataStore.DeleteBatchSizes.Enqueue(0);

        var runs = await executor.RunAsync(Tenant, RetentionTargets.RunEvents);

        sink.WriteCalls.ShouldBe(1);
        runs[0].ArchivedRows.ShouldBe(1);
        runs[0].DeletedRows.ShouldBe(1);
    }

    [Fact]
    public async Task Parti_boyutundan_kucuk_donunce_dongu_durur()
    {
        var (executor, store, dataStore) = Build(batchSize: 2);

        await SavePolicyAsync(store, RetentionTargets.RunEvents, maxAgeDays: 30);
        dataStore.DeleteBatchSizes.Enqueue(2);
        dataStore.DeleteBatchSizes.Enqueue(2);
        dataStore.DeleteBatchSizes.Enqueue(1);

        var runs = await executor.RunAsync(Tenant, RetentionTargets.RunEvents);

        dataStore.DeleteCalls.ShouldBe(3);
        runs[0].DeletedRows.ShouldBe(5);
    }

    [Fact]
    public async Task Politikasi_olmayan_hedef_hicbir_kosu_uretmez()
    {
        var (executor, _, dataStore) = Build();

        var runs = await executor.RunAsync(Tenant, RetentionTargets.RunEvents);

        runs.ShouldBeEmpty();
        dataStore.DeleteCalls.ShouldBe(0);
    }

    /// <summary>
    /// 🚨 Faz 36'nin kapattigi bosluk: MaxAgeDays BOS, yalniz MaxRows dolu bir
    /// politika bugune kadar sifir satir siliyordu.
    /// </summary>
    [Fact]
    public async Task Yalniz_MaxRows_dolu_politika_siler()
    {
        var (executor, store, dataStore) = Build();

        await SavePolicyAsync(store, RetentionTargets.RunEvents, maxRows: 100);
        dataStore.RowLimitCutoffToReturn = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);
        dataStore.DeleteBatchSizes.Enqueue(50);
        dataStore.DeleteBatchSizes.Enqueue(0);

        var runs = await executor.RunAsync(Tenant, RetentionTargets.RunEvents);

        runs[0].DeletedRows.ShouldBe(50);
        dataStore.RowLimitCalls.ShouldBe(1);
        dataStore.LastRowLimitMaxRows.ShouldBe(100);
    }

    [Fact]
    public async Task MaxRows_tablo_sinirin_altindaysa_hicbir_silme_sorgusu_calismaz()
    {
        var (executor, store, dataStore) = Build();

        await SavePolicyAsync(store, RetentionTargets.RunEvents, maxRows: 100);
        dataStore.RowLimitCutoffToReturn = null;

        var runs = await executor.RunAsync(Tenant, RetentionTargets.RunEvents);

        runs.Count.ShouldBe(1);
        runs[0].DeletedRows.ShouldBe(0);
        dataStore.DeleteCalls.ShouldBe(0);
    }

    [Fact]
    public async Task Iki_esik_doluyken_daha_yeni_olan_kazanir()
    {
        var (executor, store, dataStore) = Build(
            now: new DateTimeOffset(2026, 8, 6, 0, 0, 0, TimeSpan.Zero));

        // MaxAgeDays=30 -> esik 2026-07-07. MaxRows esigi (satirdan gelen) daha
        // YENI (2026-08-01) — daha cok siler ve KAZANMALIDIR.
        await SavePolicyAsync(store, RetentionTargets.RunEvents, maxAgeDays: 30, maxRows: 100);
        dataStore.RowLimitCutoffToReturn = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);
        dataStore.DeleteBatchSizes.Enqueue(10);

        await executor.RunAsync(Tenant, RetentionTargets.RunEvents);

        dataStore.LastCutoffUsed.ShouldBe(new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task Onizleme_MaxRows_esigini_gercek_kosuyla_ayni_hesaplar()
    {
        var (executor, store, dataStore) = Build();

        await SavePolicyAsync(store, RetentionTargets.RunEvents, maxRows: 100);
        dataStore.RowLimitCutoffToReturn = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);
        dataStore.CountToReturn = 50;

        var preview = await executor.PreviewAsync(Tenant, RetentionTargets.RunEvents);

        preview[0].Enabled.ShouldBeTrue();
        preview[0].Cutoff.ShouldBe(new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero));
        preview[0].MatchingRows.ShouldBe(50);
    }

    private static (RetentionExecutor Executor, IRetentionPolicyStore Store, RecordingRetentionStore DataStore) Build(
        IArchiveSink? archiveSink = null,
        int batchSize = 5000,
        DateTimeOffset? now = null)
    {
        var policyStore = new InMemoryRetentionPolicyStore();
        var dataStore = new RecordingRetentionStore();
        var options = new AgentPrismRetentionOptions { BatchSize = batchSize, BatchDelay = TimeSpan.Zero };
        var resolver = new RetentionPolicyResolver(policyStore, new StaticOptionsMonitor<AgentPrismRetentionOptions>(options));

        var executor = new RetentionExecutor(
            policyStore,
            dataStore,
            resolver,
            new StaticOptionsMonitor<AgentPrismRetentionOptions>(options),
            archiveSink,
            new ManualTimeProvider(now ?? new DateTimeOffset(2026, 8, 5, 3, 0, 0, TimeSpan.Zero)));

        return (executor, policyStore, dataStore);
    }

    private static async Task SavePolicyAsync(
        IRetentionPolicyStore store,
        string target,
        int? maxAgeDays = null,
        long? maxRows = null,
        bool archive = false)
    {
        var now = DateTimeOffset.UtcNow;

        await store.SavePolicyAsync(new RetentionPolicy
        {
            Id = Guid.NewGuid(),
            TenantId = Tenant,
            Target = target,
            MaxAgeDays = maxAgeDays,
            MaxRows = maxRows,
            Archive = archive,
            Enabled = true,
            CreatedAt = now,
            UpdatedAt = now,
        });
    }

    private sealed class RecordingRetentionStore : IRetentionStore
    {
        public int DeleteCalls { get; private set; }

        public int ArchiveReadCalls { get; private set; }

        public int RowLimitCalls { get; private set; }

        public long CountToReturn { get; set; }

        public DateTimeOffset? RowLimitCutoffToReturn { get; set; }

        public long? LastRowLimitMaxRows { get; private set; }

        public DateTimeOffset? LastCutoffUsed { get; private set; }

        public Queue<int> DeleteBatchSizes { get; } = new();

        public IReadOnlyList<ArchiveRow> ArchiveRowsToReturn { get; set; } = [];

        public ValueTask<long> CountOlderThanAsync(string target, string? tenantId, DateTimeOffset cutoff, CancellationToken cancellationToken = default)
        {
            LastCutoffUsed = cutoff;

            return new ValueTask<long>(CountToReturn);
        }

        public ValueTask<IReadOnlyList<ArchiveRow>> ReadForArchiveAsync(
            string target,
            string? tenantId,
            DateTimeOffset cutoff,
            int batchSize,
            CancellationToken cancellationToken = default)
        {
            ArchiveReadCalls++;

            var rows = ArchiveRowsToReturn;
            ArchiveRowsToReturn = [];

            return new ValueTask<IReadOnlyList<ArchiveRow>>(rows);
        }

        public ValueTask<DateTimeOffset?> FindRowLimitCutoffAsync(
            string target,
            string? tenantId,
            long maxRows,
            CancellationToken cancellationToken = default)
        {
            RowLimitCalls++;
            LastRowLimitMaxRows = maxRows;

            return new ValueTask<DateTimeOffset?>(RowLimitCutoffToReturn);
        }

        public ValueTask<int> DeleteBatchAsync(string target, string? tenantId, DateTimeOffset cutoff, int batchSize, CancellationToken cancellationToken = default)
        {
            LastCutoffUsed = cutoff;

            DeleteCalls++;

            return new ValueTask<int>(DeleteBatchSizes.Count > 0 ? DeleteBatchSizes.Dequeue() : 0);
        }
    }

    private sealed class RecordingArchiveSink : IArchiveSink
    {
        public int WriteCalls { get; private set; }

        public ValueTask WriteAsync(
            string target,
            DateTimeOffset partitionDate,
            IReadOnlyList<ArchiveRow> rows,
            CancellationToken cancellationToken = default)
        {
            WriteCalls++;

            return ValueTask.CompletedTask;
        }
    }
}
