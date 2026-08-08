using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>
/// Sirasi gelen zamanlamalari kuyruga isler dusurur ve kiralanabilir isleri
/// alip kayitli <see cref="IJobHandler"/> uygulamalarina dagitir.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="AgentPrismSchedulingOptions.RunWorker"/> <see langword="false"/>
/// ise <see cref="ExecuteAsync"/> hemen doner ve hicbir zamanlayici kurmaz —
/// <c>ModelProviderHealthBackgroundService</c> ile ayni "bosta duran kurulum
/// arka plan cagrisi yapmaz" kurali. Kuyruk ve zamanlama depolari bu durumda
/// da calismaya devam eder; yalnizca bu surecte is kiralanmaz.
/// </para>
/// <para>
/// Bir tur (tick) basarisiz olursa hata loglanir ve bir sonraki tur normal
/// sekilde calisir — gozlemlenebilirlik/arka plan hatasi islevselligi
/// bozmamalidir kurali burada da gecerlidir.
/// </para>
/// </remarks>
internal sealed class JobWorkerBackgroundService(
    IJobStore jobStore,
    IJobScheduleStore scheduleStore,
    IEnumerable<IJobHandler> handlers,
    IOptionsMonitor<AgentPrismSchedulingOptions> optionsMonitor,
    SchemaReadyGate schemaReadyGate,
    TimeProvider? timeProvider = null,
    ILogger<JobWorkerBackgroundService>? logger = null) : BackgroundService
{
    private readonly string _ownerId = $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var options = optionsMonitor.CurrentValue;

        if (!options.Enabled || !options.RunWorker || options.PollInterval <= TimeSpan.Zero)
        {
            return;
        }

        // 🚨 Ilk SQL denemesinden ONCE semanin hazir olmasini bekle. PeriodicTimer
        // bir tur gecikme verir ama GARANTI degildir: kisa bir PollInterval ile
        // migration henuz bitmemis olabilir. Gerekce: K-354.
        try
        {
            await schemaReadyGate.WaitAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        using var timer = new PeriodicTimer(options.PollInterval);
        using var slots = new SemaphoreSlim(Math.Max(1, options.MaxConcurrentJobs));

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                await TickAsync(slots, stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal kapanma.
        }
    }

    private async Task TickAsync(SemaphoreSlim slots, CancellationToken stoppingToken)
    {
        try
        {
            await DispatchDueSchedulesAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            if (logger is not null && logger.IsEnabled(LogLevel.Warning))
            {
                logger.LogWarning(exception, "Zamanlama tetikleme turu basarisiz oldu.");
            }
        }

        var options = optionsMonitor.CurrentValue;

        while (await slots.WaitAsync(0, stoppingToken).ConfigureAwait(false))
        {
            JobRecord? job;

            try
            {
                job = await jobStore.LeaseAsync(_ownerId, options.LeaseDuration, stoppingToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                slots.Release();

                if (logger is not null && logger.IsEnabled(LogLevel.Warning))
                {
                    logger.LogWarning(exception, "Is kiralama basarisiz oldu.");
                }

                break;
            }

            if (job is null)
            {
                slots.Release();
                break;
            }

            _ = RunJobAsync(job, slots, stoppingToken);
        }
    }

    private async Task RunJobAsync(JobRecord job, SemaphoreSlim slots, CancellationToken stoppingToken)
    {
        try
        {
            await ExecuteJobAsync(job, stoppingToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            if (logger is not null && logger.IsEnabled(LogLevel.Warning))
            {
                logger.LogWarning(exception, "Is yurutmesi beklenmedik sekilde basarisiz oldu: {JobId}.", job.Id);
            }
        }
        finally
        {
            slots.Release();
        }
    }

    private async Task ExecuteJobAsync(JobRecord job, CancellationToken stoppingToken)
    {
        var options = optionsMonitor.CurrentValue;
        var handler = handlers.FirstOrDefault(candidate => candidate.Kind == job.Kind);
        var now = _clock.GetUtcNow();

        if (handler is null)
        {
            await jobStore.CompleteAsync(
                new JobCompletion
                {
                    JobId = job.Id,
                    Status = JobStatus.Failed,
                    CompletedAt = now,
                    ErrorMessage = $"'{job.Kind}' turu icin kayitli bir IJobHandler yok.",
                },
                stoppingToken).ConfigureAwait(false);

            return;
        }

        await jobStore.MarkRunningAsync(job.Id, _ownerId, stoppingToken).ConfigureAwait(false);

        var items = await jobStore.ListItemsAsync(job.Id, stoppingToken).ConfigureAwait(false);

        var context = new JobContext
        {
            Job = job,
            Items = items,
            ReportItemAsync = (result, ct) => jobStore.ReportItemAsync(result, ct),
            IsCancelledAsync = async ct =>
            {
                var current = await jobStore.GetAsync(job.TenantId, job.Id, ct).ConfigureAwait(false);
                return current is null || current.Status == JobStatus.Cancelled;
            },
        };

        using var tenantScope = AmbientTenantScope.Begin(job.TenantId);
        using var leaseRenewal = StartLeaseRenewal(job.Id, options.LeaseDuration, stoppingToken);

        try
        {
            await handler.ExecuteAsync(context, stoppingToken).ConfigureAwait(false);

            var current = await jobStore.GetAsync(job.TenantId, job.Id, stoppingToken).ConfigureAwait(false);

            if (current?.Status == JobStatus.Cancelled)
            {
                // Isleyici ogeler arasinda IsCancelledAsync uzerinden bunu zaten
                // gormustur ve erken cikmistir; CompleteAsync ile uzerine yazma.
                return;
            }

            var finalItems = await jobStore.ListItemsAsync(job.Id, stoppingToken).ConfigureAwait(false);
            var allFailed = finalItems.Count > 0 && finalItems.All(item => item.Status == JobItemStatus.Failed);

            await jobStore.CompleteAsync(
                new JobCompletion
                {
                    JobId = job.Id,
                    Status = allFailed ? JobStatus.Failed : JobStatus.Completed,
                    CompletedAt = _clock.GetUtcNow(),
                    ErrorMessage = allFailed ? "Isin tum ogeleri basarisiz oldu." : null,
                },
                stoppingToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Is kendi deneme sinirini tasiyabilir (webhook teslimi genel
            // ayardan farkli bir merdiven kullanir); tasimiyorsa genel ayar.
            var maxAttempts = job.MaxAttempts ?? options.MaxAttempts;

            if (job.Attempt >= maxAttempts)
            {
                await jobStore.CompleteAsync(
                    new JobCompletion
                    {
                        JobId = job.Id,
                        Status = JobStatus.Failed,
                        CompletedAt = _clock.GetUtcNow(),
                        ErrorMessage = exception.Message,
                    },
                    stoppingToken).ConfigureAwait(false);
            }
            else
            {
                // Isleyici bir bekleme talep edebilir; etmezse eski davranis
                // (hemen yeniden kiralanabilir) korunur.
                var retryAfter = (exception as JobRetryException)?.RetryAfter;

                await jobStore
                    .ReleaseForRetryAsync(job.Id, exception.Message, retryAfter, stoppingToken)
                    .ConfigureAwait(false);
            }
        }
    }

    private async Task DispatchDueSchedulesAsync(CancellationToken stoppingToken)
    {
        var now = _clock.GetUtcNow();
        var due = await scheduleStore.ListDueAsync(now, stoppingToken).ConfigureAwait(false);

        foreach (var schedule in due)
        {
            if (schedule.NextRunAt is not { } nextRunAt || schedule.Cron is not { Length: > 0 } cron)
            {
                continue;
            }

            DateTimeOffset newNextRunAt;

            try
            {
                var timeZone = TimeZoneInfo.FindSystemTimeZoneById(schedule.TimeZone);
                newNextRunAt = CronExpression.Parse(cron).GetNextOccurrence(now, timeZone);
            }
            catch (Exception exception) when (
                exception is FormatException or TimeZoneNotFoundException or InvalidTimeZoneException)
            {
                if (logger is not null && logger.IsEnabled(LogLevel.Warning))
                {
                    logger.LogWarning(exception, "Zamanlama '{Name}' cozumlenemedi, bu turda atlaniyor.", schedule.Name);
                }

                continue;
            }

            // Baska bir ornek ayni anda ayni zamanlamayi iddia etmis olabilir;
            // yalnizca bu ornegin iddiasi kazanirsa is olusturulur.
            var claimed = await scheduleStore
                .TryClaimNextRunAsync(schedule.Id, nextRunAt, newNextRunAt, now, stoppingToken)
                .ConfigureAwait(false);

            if (!claimed)
            {
                continue;
            }

            await jobStore.EnqueueAsync(
                new JobRecord
                {
                    Id = AgentPrismId.NewId(),
                    TenantId = schedule.TenantId,
                    ScheduleId = schedule.Id,
                    Kind = schedule.Kind,
                    TargetName = schedule.TargetName,
                    Status = JobStatus.Pending,
                    Payload = schedule.Payload,
                    ScheduledFor = nextRunAt,
                    CreatedAt = now,
                },
                JobPayload.ExtractItems(schedule.Payload),
                stoppingToken).ConfigureAwait(false);
        }
    }

    private CancelOnDispose StartLeaseRenewal(Guid jobId, TimeSpan leaseDuration, CancellationToken stoppingToken)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var interval = leaseDuration / 2;

        if (interval <= TimeSpan.Zero)
        {
            interval = TimeSpan.FromSeconds(30);
        }

        _ = RenewLoopAsync(jobId, leaseDuration, interval, cts.Token);

        return new CancelOnDispose(cts);
    }

    private async Task RenewLoopAsync(Guid jobId, TimeSpan leaseDuration, TimeSpan interval, CancellationToken token)
    {
        try
        {
            using var timer = new PeriodicTimer(interval);

            while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
            {
                await jobStore.RenewLeaseAsync(jobId, _ownerId, leaseDuration, token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Is bitti veya isci kapaniyor.
        }
        catch (Exception exception)
        {
            if (logger is not null && logger.IsEnabled(LogLevel.Warning))
            {
                logger.LogWarning(exception, "Kira yenileme basarisiz oldu: {JobId}.", jobId);
            }
        }
    }

    /// <summary>Bir <see cref="CancellationTokenSource"/>'u once iptal edip sonra bertaraf eden yardimci.</summary>
    private sealed class CancelOnDispose(CancellationTokenSource cts) : IDisposable
    {
        public void Dispose()
        {
            cts.Cancel();
            cts.Dispose();
        }
    }
}
