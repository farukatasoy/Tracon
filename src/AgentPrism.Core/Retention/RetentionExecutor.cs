using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>
/// Onizleme ve fiili temizleme kosusu icin ortak mantik. <see cref="RetentionJobHandler"/>
/// ve <c>RetentionEndpoints</c>'in "simdi calistir"/"onizle" uclari bunu paylasir.
/// </summary>
public sealed class RetentionExecutor(
    IRetentionPolicyStore policyStore,
    IRetentionStore dataStore,
    RetentionPolicyResolver resolver,
    IOptionsMonitor<AgentPrismRetentionOptions> optionsMonitor,
    IArchiveSink? archiveSink = null,
    TimeProvider? timeProvider = null,
    ILogger<RetentionExecutor>? logger = null)
{
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;

    /// <summary>Bir veya tum hedefler icin "su an calistirilirsa ne olur" onizlemesi cikarir.</summary>
    /// <param name="tenantId">Kiraci kimligi.</param>
    /// <param name="target">Yalniz bu hedef; <see langword="null"/> ise tum taninan hedefler.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Hedef basina onizleme.</returns>
    public async ValueTask<IReadOnlyList<RetentionPreview>> PreviewAsync(
        string tenantId,
        string? target,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        var targets = ResolveTargetList(target);
        var results = new List<RetentionPreview>(targets.Count);
        var now = _clock.GetUtcNow();

        foreach (var candidate in targets)
        {
            var resolved = await resolver.ResolveAsync(tenantId, candidate, cancellationToken).ConfigureAwait(false);

            if (resolved is null)
            {
                results.Add(new RetentionPreview { Target = candidate, Enabled = false });

                continue;
            }

            var cutoff = now - TimeSpan.FromDays(resolved.MaxAgeDays);
            var count = await dataStore.CountOlderThanAsync(candidate, cutoff, cancellationToken).ConfigureAwait(false);

            results.Add(new RetentionPreview
            {
                Target = candidate,
                MaxAgeDays = resolved.MaxAgeDays,
                Enabled = true,
                Cutoff = cutoff,
                MatchingRows = count,
            });
        }

        return results;
    }

    /// <summary>Bir veya tum hedefler icin fiili temizlemeyi calistirir.</summary>
    /// <param name="tenantId">Kiraci kimligi.</param>
    /// <param name="target">Yalniz bu hedef; <see langword="null"/> ise tum taninan hedefler.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Tamamlanan (veya hicbir sey yapilmayan) kosu kayitlari.</returns>
    public async ValueTask<IReadOnlyList<RetentionRun>> RunAsync(
        string tenantId,
        string? target,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        var options = optionsMonitor.CurrentValue;
        var targets = ResolveTargetList(target);
        var completed = new List<RetentionRun>(targets.Count);

        foreach (var candidate in targets)
        {
            var resolved = await resolver.ResolveAsync(tenantId, candidate, cancellationToken).ConfigureAwait(false);

            if (resolved is null)
            {
                continue;
            }

            var run = await RunTargetAsync(tenantId, resolved, options, cancellationToken).ConfigureAwait(false);
            completed.Add(run);
        }

        return completed;
    }

    private async ValueTask<RetentionRun> RunTargetAsync(
        string tenantId,
        ResolvedRetentionPolicy policy,
        AgentPrismRetentionOptions options,
        CancellationToken cancellationToken)
    {
        var now = _clock.GetUtcNow();
        var cutoff = now - TimeSpan.FromDays(policy.MaxAgeDays);

        var run = await policyStore.CreateRunAsync(
            new RetentionRun
            {
                Id = AgentPrismId.NewId(),
                TenantId = tenantId,
                Target = policy.Target,
                StartedAt = now,
            },
            cancellationToken).ConfigureAwait(false);

        if (policy.Archive && archiveSink is null)
        {
            // 🚨 Arsivlenemeyen veri dusurulmez (Faz 25 karari). Kosu "basarili"
            // olarak kapatilir ama hicbir satir silinmez.
            if (logger is not null && logger.IsEnabled(LogLevel.Warning))
            {
                logger.LogWarning(
                    "'{Target}' hedefi icin arsivleme istendi ama IArchiveSink kayitli degil; hicbir satir silinmedi.",
                    policy.Target);
            }

            await policyStore.CompleteRunAsync(run.Id, _clock.GetUtcNow(), null, cancellationToken).ConfigureAwait(false);

            return run;
        }

        string? error = null;
        long deletedTotal = 0;
        long archivedTotal = 0;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                if (policy.Archive)
                {
                    var rows = await dataStore
                        .ReadForArchiveAsync(policy.Target, cutoff, options.BatchSize, cancellationToken)
                        .ConfigureAwait(false);

                    if (rows.Count > 0)
                    {
                        await archiveSink!
                            .WriteAsync(policy.Target, now, rows, cancellationToken)
                            .ConfigureAwait(false);

                        archivedTotal += rows.Count;

                        await policyStore
                            .AppendRunProgressAsync(run.Id, 0, rows.Count, cancellationToken)
                            .ConfigureAwait(false);
                    }
                }

                var deleted = await dataStore
                    .DeleteBatchAsync(policy.Target, cutoff, options.BatchSize, cancellationToken)
                    .ConfigureAwait(false);

                if (deleted == 0)
                {
                    break;
                }

                deletedTotal += deleted;

                await policyStore
                    .AppendRunProgressAsync(run.Id, deleted, 0, cancellationToken)
                    .ConfigureAwait(false);

                if (deleted < options.BatchSize)
                {
                    // Son partiden daha azi dondu: eslesen kalmadi, tekrar
                    // denemeye gerek yok.
                    break;
                }

                if (options.BatchDelay > TimeSpan.Zero)
                {
                    await Task.Delay(options.BatchDelay, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            error = exception.Message;

            throw;
        }
        finally
        {
            await policyStore
                .CompleteRunAsync(run.Id, _clock.GetUtcNow(), error, cancellationToken)
                .ConfigureAwait(false);
        }

        return run with { DeletedRows = deletedTotal, ArchivedRows = archivedTotal, CompletedAt = _clock.GetUtcNow() };
    }

    private static IReadOnlyList<string> ResolveTargetList(string? target)
    {
        if (target is not { Length: > 0 })
        {
            return RetentionTargets.All;
        }

        if (!RetentionTargets.IsKnown(target))
        {
            throw new ArgumentException($"Bilinmeyen saklama hedefi: '{target}'.", nameof(target));
        }

        return [target];
    }
}
