namespace AgentPrism;

/// <summary>
/// Bir model saglayicisinin isteğe bagli yapilandirma teshis sozlesmesi.
/// </summary>
/// <remarks>
/// <see cref="IModelProvider"/> arayuzune uye <strong>eklenmez</strong> — bu, tuketicinin
/// kendi <see cref="IModelProvider"/> uygulamasini kirar (karar K4). Bir saglayici
/// bunu uygulamiyorsa veya <see langword="null"/> donerse teshis raporunda o saglayici
/// icin hicbir <see cref="ConfigurationDiagnostic"/> yer almaz.
/// </remarks>
public interface IModelProviderConfigurationDiagnostics
{
    /// <summary>
    /// Bu saglayicinin bekledigi yapilandirma anahtarinin cozulme durumunu dondurur.
    /// </summary>
    /// <returns>Saglayici bir ayar nesnesiyle kurulmadiysa <see langword="null"/>.</returns>
    ConfigurationDiagnostic? GetConfigurationDiagnostic();
}
