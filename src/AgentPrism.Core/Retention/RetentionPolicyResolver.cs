using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>Bir hedef icin veritabani politikasi ile yapilandirma varsayilanini birlestirir.</summary>
/// <remarks>
/// Sira: once <see cref="IRetentionPolicyStore"/>'daki acik kayit (kiraci, sonra
/// <c>"*"</c>) aranir; bulunursa yapilandirma HIC bakilmaz. Kayit yoksa
/// <see cref="AgentPrismRetentionOptions"/> devreye girer, ama yalniz
/// <see cref="AgentPrismRetentionOptions.Enabled"/> acikken.
/// </remarks>
public sealed class RetentionPolicyResolver(
    IRetentionPolicyStore policyStore,
    IOptionsMonitor<AgentPrismRetentionOptions> optionsMonitor)
{
    /// <summary>Bir hedef icin etkin saklama kuralini cozer.</summary>
    /// <param name="tenantId">Kiraci kimligi.</param>
    /// <param name="target">Hedef adi.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Etkin kural; hicbir sey silinmeyecekse <see langword="null"/>.</returns>
    public async ValueTask<ResolvedRetentionPolicy?> ResolveAsync(
        string tenantId,
        string target,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(target);

        var dbPolicy = await policyStore.GetPolicyAsync(tenantId, target, cancellationToken).ConfigureAwait(false);

        if (dbPolicy is not null)
        {
            // Acik bir kayit varsa yapilandirmaya HIC bakilmaz — kayit "kapali"
            // olsa bile bu, tuketicinin bilincli tercihidir.
            return dbPolicy.Enabled && dbPolicy.MaxAgeDays is { } days
                ? new ResolvedRetentionPolicy(target, days, dbPolicy.Archive)
                : null;
        }

        var options = optionsMonitor.CurrentValue;

        if (!options.Enabled)
        {
            return null;
        }

        var fallback = options.ForTarget(target);

        return fallback?.MaxAgeDays is { } fallbackDays
            ? new ResolvedRetentionPolicy(target, fallbackDays, fallback.Archive)
            : null;
    }
}

/// <summary>Bir hedef icin cozulmus, uygulanabilir saklama kurali.</summary>
/// <param name="Target">Hedef adi.</param>
/// <param name="MaxAgeDays">Bu yastan eski satirlar silinir.</param>
/// <param name="Archive">Silmeden once arsivlensin mi.</param>
public sealed record ResolvedRetentionPolicy(string Target, int MaxAgeDays, bool Archive);
