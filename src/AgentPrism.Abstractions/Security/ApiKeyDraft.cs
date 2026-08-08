namespace AgentPrism;

/// <summary>Bir API anahtari olusturmak icin taslak.</summary>
/// <remarks>
/// Ham deger, ozet ve onek <see cref="IApiKeyStore.CreateAsync"/> tarafindan
/// uretilir; taslak yalniz operatorun verdigi bilgileri tasir.
/// </remarks>
public sealed record ApiKeyDraft
{
    /// <summary>Anahtarin baglanacagi kiraci.</summary>
    public required string TenantId { get; init; }

    /// <summary>Operatorun anahtari tanimasi icin ad.</summary>
    public required string Name { get; init; }

    /// <summary>Kapsam kumesi. Bos olamaz.</summary>
    public required IReadOnlyList<ApiKeyScope> Scopes { get; init; }

    /// <summary>Sure sonu. <see langword="null"/> ise suresizdir.</summary>
    public DateTimeOffset? ExpiresAt { get; init; }
}
