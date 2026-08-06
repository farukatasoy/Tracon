namespace AgentPrism;

/// <summary>
/// Etkin SQL kalicilik saglayicisinin isteğe bagli teshis sozlesmesi.
/// </summary>
/// <remarks>
/// <para>
/// Her <c>Use*()</c> uzantisi (<c>UsePostgreSql</c>, <c>UseSqlServer</c>,
/// <c>UseSqlite</c>) bu arayuzu <c>Replace</c> ile kaydeder — kazanan hangisiyse
/// (K-025) teshis de onu yansitir. Kayitli degilse kalicilik bellek icidir.
/// </para>
/// <para>
/// Denetim <strong>hafif bir baglanti sinamasidir</strong> (<c>SELECT 1</c>
/// benzeri); migration uygulamaz, veri degistirmez.
/// </para>
/// </remarks>
public interface ISqlPersistenceDiagnostics
{
    /// <summary>Saglayici adi. Ornek: <c>PostgreSQL</c>.</summary>
    string ProviderName { get; }

    /// <summary>Baglanti ve migration durumunu okur.</summary>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Anlik durum.</returns>
    ValueTask<SqlPersistenceDiagnosticsSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default);
}

/// <summary>Bir SQL kalicilik saglayicisinin anlik baglanti ve migration durumu.</summary>
public sealed record SqlPersistenceDiagnosticsSnapshot
{
    /// <summary>Veritabanina baglanilabildi mi.</summary>
    public required bool CanConnect { get; init; }

    /// <summary>Bekleyen migration adlari. Baglanti kurulamadiysa bilinmez ve bostur.</summary>
    public required IReadOnlyList<string> PendingMigrations { get; init; }
}

/// <summary>
/// Kayitli bir SQL kalicilik saglayicisinin isareti.
/// </summary>
/// <param name="ProviderName">Saglayici adi. Ornek: <c>PostgreSQL</c>.</param>
/// <remarks>
/// Her <c>Use*</c> uzantisi bir isaret ekler. Isaretler <em>birikir</em>
/// (<c>AddSingleton</c>, <c>TryAdd</c> degil); birden fazlaysa acilista uyari
/// loglanir ve <see cref="AgentPrismDiagnosticsReport.RegisteredPersistenceProviders"/>
/// birden buyuk doner (K-183).
/// </remarks>
public sealed record SqlPersistenceRegistrationMarker(string ProviderName);
