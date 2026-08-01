namespace AgentPrism;

/// <summary>AgentPrism'in telemetri kaynak adlari.</summary>
/// <remarks>
/// Bu adlar <strong>kararlidir</strong>. Tuketiciler OpenTelemetry
/// yapilandirmalarinda bu adlari kullanir; degistirmek kirici degisikliktir.
/// </remarks>
public static class AgentPrismDiagnostics
{
    /// <summary>AgentPrism'in <c>ActivitySource</c> adi.</summary>
    public const string ActivitySourceName = "AgentPrism";

    /// <summary>AgentPrism'in <c>Meter</c> adi.</summary>
    public const string MeterName = "AgentPrism";
}
