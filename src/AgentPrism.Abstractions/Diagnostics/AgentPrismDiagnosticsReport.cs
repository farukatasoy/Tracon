namespace AgentPrism;

/// <summary>Kurulumun kendi kendini denetleyen ozet raporu (Faz 33).</summary>
/// <remarks>
/// <para>
/// 🚨 Bu tip hicbir <c>secret</c> tasimaz: bağlanti dizesi, API anahtari veya kimlik
/// bilgisi alanlarindan hicbiri yoktur. Yalniz "cozuldu mu" bilgisi tasinir.
/// Gerekce: <c>docs/KARARLAR.md</c>, karar K-059.
/// </para>
/// <para>
/// <c>AgentPrismDiagnosticsCollector</c> (AgentPrism.Core) tarafindan uretilir;
/// hicbir model cagrisi veya migration uygulamasi yapmaz, yalniz mevcut durumu okur.
/// </para>
/// </remarks>
public sealed record AgentPrismDiagnosticsReport
{
    /// <summary>Etkin kalicilik saglayicisinin adi. Ornek: <c>PostgreSQL</c>, <c>InMemory</c>.</summary>
    public required string PersistenceProvider { get; init; }

    /// <summary>
    /// Kayitli SQL kalicilik saglayicisi sayisi. <c>1</c>'den fazlaysa K-183 durumu
    /// olusmustur: son cagri kazanir ve digerleri sessizce devre disi kalir.
    /// </summary>
    public required int RegisteredPersistenceProviders { get; init; }

    /// <summary>Etkin SQL saglayicisina baglanilabiliyor mu. SQL saglayicisi yoksa <see langword="true"/>.</summary>
    public required bool CanConnect { get; init; }

    /// <summary>Bekleyen migration yok mu. SQL saglayicisi yoksa <see langword="true"/>.</summary>
    public required bool MigrationsUpToDate { get; init; }

    /// <summary>Bekleyen migration adlari. Bostur: SQL saglayicisi yoksa veya hepsi uygulanmissa.</summary>
    public required IReadOnlyList<string> PendingMigrations { get; init; }

    /// <summary>Kayitli her model saglayicisinin son bilinen durumu.</summary>
    public required IReadOnlyList<ProviderDiagnostic> ModelProviders { get; init; }

    /// <summary>Kayitli saglayicilarin bekledigi yapilandirma anahtarlarinin cozulme durumu.</summary>
    public required IReadOnlyList<ConfigurationDiagnostic> Configuration { get; init; }

    /// <summary>Yonetim arayuzunun gomulu varliklari var mi.</summary>
    public required bool UiEmbedded { get; init; }

    /// <summary>Kayitli tool sayisi.</summary>
    public required int ToolCount { get; init; }

    /// <summary>Kayitli agent sayisi.</summary>
    public required int AgentCount { get; init; }
}
