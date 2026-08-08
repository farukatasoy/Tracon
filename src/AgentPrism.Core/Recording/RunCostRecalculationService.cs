namespace AgentPrism;

/// <summary>
/// Tum calistirmalarin maliyetini guncel fiyat kaynagina (katalog/yapilandirma)
/// gore yeniden hesaplayan bakim servisi.
/// </summary>
/// <remarks>
/// Yalniz <c>POST /api/stats/recalculate-costs</c> ucu tarafindan kullanilir.
/// Her cagri <strong>tam</strong> bir yeniden hesaplamadir — "yalniz tanimsiz
/// olanlar" gibi bir filtre yoktur, cunku ucun amaci guncel fiyati gecmise
/// aynen uygulamaktir. Saglayici gecmis satirlarda tutulmadigi icin cozumleme
/// yalniz model adiyla yapilir (bkz. <see cref="IRunPricingResolver.Resolve"/>
/// ve <c>docs/KARARLAR.md</c> K-154).
/// </remarks>
public sealed class RunCostRecalculationService
{
    private const int PageSize = 200;

    private readonly IRunStore _store;
    private readonly IRunPricingResolver _resolver;

    /// <summary>Yeni bir yeniden hesaplama servisi olusturur.</summary>
    /// <param name="store">Calistirma deposu.</param>
    /// <param name="resolver">Maliyet cozumleyici.</param>
    /// <exception cref="ArgumentNullException">Bagimliliklardan biri <see langword="null"/> ise.</exception>
    public RunCostRecalculationService(IRunStore store, IRunPricingResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(resolver);

        _store = store;
        _resolver = resolver;
    }

    /// <summary>Bir kiracinin tum calistirmalarinin maliyetini yeniden hesaplar.</summary>
    /// <param name="tenantId">Kiraci kimligi.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Islenen, guncellenen ve hala tanimsiz kalan calistirma sayilari.</returns>
    public async ValueTask<RunCostRecalculationResult> RecalculateAsync(
        string tenantId,
        CancellationToken cancellationToken = default)
    {
        long considered = 0;
        long updated = 0;
        long stillUnknown = 0;

        for (var skip = 0; ; skip += PageSize)
        {
            var page = await _store.QueryRunsAsync(
                new RunQuery
                {
                    TenantId = tenantId,
                    OnlyRootRuns = false,
                    Skip = skip,
                    Take = PageSize,
                },
                cancellationToken).ConfigureAwait(false);

            if (page.Count == 0)
            {
                break;
            }

            foreach (var run in page)
            {
                if (run.ModelId is not { Length: > 0 } || run.Usage is null)
                {
                    continue;
                }

                considered++;

                var cost = _resolver.Resolve(provider: null, run.ModelId, run.Usage);
                if (cost is null)
                {
                    continue;
                }

                // Kimlikler kiraciya gore SUZULMUS bir sorgudan geldi; ayni kiraciyi
                // yazmaya da tasiyoruz (K-355). Iki asamali yolda kayit arada baska
                // bir kiraciya gecemez.
                await _store.UpdateRunCostAsync(run.Id, cost, run.TenantId, cancellationToken)
                    .ConfigureAwait(false);

                if (cost.Source == PricingSource.Unknown)
                {
                    stillUnknown++;
                }
                else
                {
                    updated++;
                }
            }

            if (page.Count < PageSize)
            {
                break;
            }
        }

        return new RunCostRecalculationResult
        {
            RunsConsidered = considered,
            RunsUpdated = updated,
            RunsStillUnknown = stillUnknown,
        };
    }
}
