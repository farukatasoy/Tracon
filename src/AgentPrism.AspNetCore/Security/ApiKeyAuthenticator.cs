namespace AgentPrism;

/// <summary>Sunulan bir ham token degerini bir API anahtarina cozer.</summary>
/// <remarks>
/// Hash &gt; arama &gt; sure sonu/iptal denetimi zincirini tek yerde toplar; hem
/// <see cref="AgentPrismEndpointFilter"/> hem de ileride eklenebilecek ikinci
/// bir giris noktasi (ornegin WebSocket el sikismasi) ayni mantigi tekrar
/// yazmaz.
/// </remarks>
internal static class ApiKeyAuthenticator
{
    /// <summary>Ham bir token degerini dogrular ve kaydini doner.</summary>
    /// <param name="store">Anahtar deposu.</param>
    /// <param name="presentedToken">Sunulan ham deger.</param>
    /// <param name="timeProvider">Sure sonu denetimi icin zaman kaynagi.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Gecerli bir anahtarsa kaydi; degilse <see langword="null"/>.</returns>
    public static async ValueTask<ApiKeyRecord?> AuthenticateAsync(
        IApiKeyStore store,
        string presentedToken,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(timeProvider);

        if (string.IsNullOrEmpty(presentedToken))
        {
            return null;
        }

        var hash = ApiKeyGenerator.ComputeHash(presentedToken);
        var record = await store.FindByHashAsync(hash, cancellationToken).ConfigureAwait(false);

        if (record is null)
        {
            return null;
        }

        var now = timeProvider.GetUtcNow();

        if (record.RevokedAt is not null || record.ExpiresAt is { } expires && expires <= now)
        {
            return null;
        }

        return record;
    }
}
