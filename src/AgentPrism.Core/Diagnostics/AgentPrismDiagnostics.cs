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

    /// <summary>Bir skill script calistirmasini temsil eden span'in adi.</summary>
    public const string SkillScriptActivityName = "execute_skill_script";

    /// <summary>Skill script cagrilarinin metrikteki tool adi.</summary>
    public const string SkillScriptToolName = "skill_script";

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

        /// <summary>Bu calistirmayi baslatan calistirmanin kimligi. Yalnizca alt calistirmalarda yazilir.</summary>
        public const string ParentRunId = "agentprism.run.parent_id";

        /// <summary>Cagri agacindaki derinlik. Yalnizca alt calistirmalarda yazilir.</summary>
        public const string Depth = "agentprism.run.depth";

        /// <summary>Akisli calistirma mi.</summary>
        public const string Streaming = "agentprism.run.streaming";

        /// <summary>Model adi.</summary>
        public const string ModelId = "agentprism.model.id";

        /// <summary>Tool adi.</summary>
        public const string ToolName = "agentprism.tool.name";

        /// <summary>Token yonu: <c>input</c> veya <c>output</c>.</summary>
        public const string Direction = "agentprism.token.direction";

        /// <summary>Skill adi.</summary>
        public const string SkillName = "agentprism.skill.name";

        /// <summary>Script adi.</summary>
        public const string ScriptName = "agentprism.script.name";

        /// <summary>Script surecinin cikis kodu.</summary>
        public const string ExitCode = "agentprism.script.exit_code";

        /// <summary>Script calistirma suresi (milisaniye).</summary>
        public const string DurationMs = "agentprism.script.duration_ms";
    }
}
