namespace AgentPrism;

/// <summary>AgentPrism'in telemetri kaynak ve olcum adlari.</summary>
/// <remarks>
/// <para>
/// Bu adlar <strong>kararlidir</strong>. Tuketiciler OpenTelemetry
/// yapilandirmalarinda bu adlari kullanir; degistirmek kirici degisikliktir.
/// </para>
/// <para>
/// Tuketici kendi OTLP exporter'ini kurmaya devam eder; AgentPrism akisi ele
/// gecirmez. Ornek kurulum:
/// </para>
/// <code>
/// builder.Services.AddOpenTelemetry()
///     .WithTracing(t => t.AddSource(AgentPrismDiagnostics.ActivitySourceName))
///     .WithMetrics(m => m.AddMeter(AgentPrismDiagnostics.MeterName));
/// </code>
/// </remarks>
public static class AgentPrismDiagnostics
{
    /// <summary>AgentPrism'in <c>ActivitySource</c> adi.</summary>
    public const string ActivitySourceName = "AgentPrism";

    /// <summary>AgentPrism'in <c>Meter</c> adi.</summary>
    public const string MeterName = "AgentPrism";

    /// <summary>Bir calistirmayi temsil eden kok span'in adi.</summary>
    public const string RunActivityName = "agentprism.run";

    /// <summary>Tamamlanan calistirma sayaci.</summary>
    public const string RunCounterName = "agentprism.runs";

    /// <summary>Calistirma suresi histogrami (saniye).</summary>
    public const string RunDurationName = "agentprism.run.duration";

    /// <summary>Token sayaci.</summary>
    public const string TokenCounterName = "agentprism.tokens";

    /// <summary>Tool cagrisi sayaci.</summary>
    public const string ToolCounterName = "agentprism.tool.invocations";

    /// <summary>Tool cagri suresi histogrami (saniye).</summary>
    public const string ToolDurationName = "agentprism.tool.duration";

    /// <summary>Span ve metrik etiket adlari. Degistirmek gosterge panolarini kirar.</summary>
    public static class Tags
    {
        /// <summary>Calistirma kimligi.</summary>
        public const string RunId = "agentprism.run.id";

        /// <summary>Agent adi.</summary>
        public const string AgentName = "agentprism.agent.name";

        /// <summary>Kiraci kimligi.</summary>
        public const string TenantId = "agentprism.tenant.id";

        /// <summary>Oturum kimligi.</summary>
        public const string SessionId = "agentprism.session.id";

        /// <summary>Calistirma durumu.</summary>
        public const string Status = "agentprism.run.status";

        /// <summary>Akisli calistirma mi.</summary>
        public const string Streaming = "agentprism.run.streaming";

        /// <summary>Model adi.</summary>
        public const string ModelId = "agentprism.model.id";

        /// <summary>Tool adi.</summary>
        public const string ToolName = "agentprism.tool.name";

        /// <summary>Token yonu: <c>input</c> veya <c>output</c>.</summary>
        public const string Direction = "agentprism.token.direction";
    }
}
