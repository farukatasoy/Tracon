using System.Security.Cryptography;
using System.Text;

namespace AgentPrism;

/// <summary>
/// Bir agent icin calisan bir deney varsa, calistirma istegini deterministik
/// olarak bir kola atar.
/// </summary>
/// <remarks>
/// Atama <strong>deterministiktir</strong>: ayni anahtar her zaman ayni kolu uretir.
/// Rastgele atama bir konusmanin ortasinda talimati degistirirdi. Gerekce:
/// docs/19-SURUM-KARSILASTIRMA-VE-AB.md, bolum 19.3.
/// </remarks>
public sealed class ExperimentAssignmentResolver
{
    private readonly IExperimentStore _store;

    /// <summary>Yeni bir atama cozumleyici olusturur.</summary>
    /// <param name="store">Deney deposu.</param>
    /// <exception cref="ArgumentNullException"><paramref name="store"/> <see langword="null"/> ise.</exception>
    public ExperimentAssignmentResolver(IExperimentStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    /// <summary>
    /// Bu agent icin calisan bir deney varsa, verilen anahtar icin bir atama uretir.
    /// </summary>
    /// <param name="tenantId">Kiraci kimligi.</param>
    /// <param name="agentName">Agent adi.</param>
    /// <param name="assignmentKey">Atama anahtari (oturum kimligi veya calistirma kimligi).</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Atama; calisan bir deney yoksa <see langword="null"/>.</returns>
    public async ValueTask<ExperimentAssignment?> ResolveAsync(
        string tenantId,
        string agentName,
        string assignmentKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tenantId);
        ArgumentNullException.ThrowIfNull(agentName);
        ArgumentNullException.ThrowIfNull(assignmentKey);

        var experiment = await _store.GetRunningAsync(tenantId, agentName, cancellationToken).ConfigureAwait(false);

        if (experiment is null)
        {
            return null;
        }

        var variant = SelectVariant(experiment, assignmentKey);

        return new ExperimentAssignment
        {
            ExperimentId = experiment.Id,
            Variant = variant.Name,
            Version = variant.Version,
        };
    }

    /// <summary>
    /// SHA-256(deneyId + ":" + anahtar) ilk 4 baytindan 0-99 araliginda bir kova
    /// uretir ve agirlik araligina dusen varyanti dondurur.
    /// </summary>
    /// <remarks>Birim testlerin dogrudan cagirabilmesi icin <c>internal</c>.</remarks>
    internal static ExperimentVariant SelectVariant(Experiment experiment, string assignmentKey)
    {
        var input = $"{experiment.Id:D}:{assignmentKey}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));

        // Ilk 4 bayt, buyuk-endian olarak yorumlanip 0-99 araligina indirilir.
        var bucket = (uint)((hash[0] << 24) | (hash[1] << 16) | (hash[2] << 8) | hash[3]) % 100;

        var cumulative = 0;

        foreach (var variant in experiment.Variants)
        {
            cumulative += variant.Weight;

            if (bucket < cumulative)
            {
                return variant;
            }
        }

        // Agirlik toplami 100'e ulasmazsa (kayit aninda dogrulandigi icin normalde
        // olmaz) son varyant geri donus olarak kullanilir.
        return experiment.Variants[^1];
    }
}
