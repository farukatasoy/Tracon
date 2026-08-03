using System.Text.Json;

namespace AgentPrism.PostgreSql.IntegrationTests.Infrastructure;

/// <summary>Testlerin kullandigi ornek veriler.</summary>
public static class TestData
{
    /// <summary>Ornek bir agent tanimi uretir.</summary>
    /// <param name="name">Agent adi.</param>
    /// <returns>Tanim.</returns>
    public static AgentDefinition Definition(string name)
        => new()
        {
            Name = name,
            DisplayName = $"{name} agent'i",
            Description = "Test tanimi.",
            Instructions = "Kisa yanit ver.",
            Model = new ModelBinding { Provider = "echo", Model = "echo-1", Temperature = 0.5f },
            ToolNames = ["alpha", "beta"],
        };

    /// <summary>Ornek bir calistirma baslangici uretir.</summary>
    /// <param name="runId">Calistirma kimligi.</param>
    /// <param name="agentName">Agent adi.</param>
    /// <returns>Baslangic bilgileri.</returns>
    public static RunStartInfo Run(Guid runId, string agentName = "test-agent")
        => new()
        {
            RunId = runId,
            AgentName = agentName,
            StartedAt = DateTimeOffset.UtcNow,
        };

    /// <summary>Ornek bir calistirma olayi uretir.</summary>
    /// <param name="runId">Calistirma kimligi.</param>
    /// <param name="sequence">Sira numarasi.</param>
    /// <returns>Olay.</returns>
    public static RunEvent Event(Guid runId, long sequence)
        => new()
        {
            RunId = runId,
            Sequence = sequence,
            Type = RunEventType.MessageDelta,
            Timestamp = DateTimeOffset.UtcNow,
            Text = $"parca {sequence}",
        };

    /// <summary>Ornek bir oturum kaydi uretir.</summary>
    /// <param name="sessionId">Oturum kimligi.</param>
    /// <param name="state">Serilestirilmis durum. Verilmezse ornek bir nesne kullanilir.</param>
    /// <returns>Oturum kaydi.</returns>
    public static SessionRecord Session(string sessionId, JsonElement? state = null)
    {
        var now = DateTimeOffset.UtcNow;

        return new SessionRecord
        {
            Id = sessionId,
            AgentName = "test-agent",
            State = state ?? State("""{"messages":[{"role":"user","text":"merhaba"}],"turn":3}"""),
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    /// <summary>Ham JSON metnini <see cref="JsonElement"/> nesnesine cevirir.</summary>
    /// <param name="json">JSON metni.</param>
    /// <returns>Bagimsiz bir JSON ogesi.</returns>
    public static JsonElement State(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    /// <summary>Ornek bir kuyruk isi uretir. <c>EnqueueAsync</c>'e verilmeye hazirdir.</summary>
    /// <param name="tenantId">Kiraci kimligi.</param>
    /// <param name="scheduledFor">Calismaya uygun zaman. Verilmezse su an.</param>
    /// <returns>Is kaydi.</returns>
    public static JobRecord Job(string tenantId = "default", DateTimeOffset? scheduledFor = null)
    {
        var now = DateTimeOffset.UtcNow;

        return new JobRecord
        {
            Id = AgentPrismId.NewId(),
            TenantId = tenantId,
            Kind = JobKind.AgentBatch,
            TargetName = "test-agent",
            Status = JobStatus.Pending,
            Payload = State("""["girdi"]"""),
            ScheduledFor = scheduledFor ?? now,
            CreatedAt = now,
        };
    }

    /// <summary>Ornek bir zamanlama uretir.</summary>
    /// <param name="tenantId">Kiraci kimligi.</param>
    /// <param name="name">Zamanlama adi.</param>
    /// <returns>Zamanlama.</returns>
    public static JobSchedule Schedule(string tenantId = "default", string name = "gece-raporu")
    {
        var now = DateTimeOffset.UtcNow;

        return new JobSchedule
        {
            TenantId = tenantId,
            Name = name,
            Kind = JobKind.AgentBatch,
            TargetName = "test-agent",
            Cron = "0 3 * * *",
            TimeZone = "UTC",
            Payload = State("""["girdi"]"""),
            Enabled = true,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    /// <summary>Ornek bir eval takimi uretir.</summary>
    /// <param name="tenantId">Kiraci kimligi.</param>
    /// <param name="name">Takim adi.</param>
    /// <returns>Takim.</returns>
    public static EvalSuite EvalSuite(string tenantId = "default", string name = "musteri-destek-takimi")
        => new()
        {
            TenantId = tenantId,
            Name = name,
            AgentName = "test-agent",
            Checks = State("""[{"kind":"nonEmpty","minLength":1}]"""),
        };

    /// <summary>Ornek bir eval vakasi uretir.</summary>
    /// <param name="suiteId">Ait oldugu takimin kimligi.</param>
    /// <param name="query">Sorgu metni.</param>
    /// <returns>Vaka.</returns>
    public static EvalCase EvalCase(Guid suiteId, string query = "soru")
        => new() { SuiteId = suiteId, Seq = 0, Query = query };

    /// <summary>Ornek bir eval kosusu uretir.</summary>
    /// <param name="tenantId">Kiraci kimligi.</param>
    /// <param name="suiteId">Olculen takimin kimligi.</param>
    /// <returns>Kosu.</returns>
    public static EvalRun EvalRun(string tenantId, Guid suiteId)
        => new()
        {
            Id = AgentPrismId.NewId(),
            TenantId = tenantId,
            SuiteId = suiteId,
            Status = EvalRunStatus.Pending,
            Total = 1,
            StartedAt = DateTimeOffset.UtcNow,
        };
}
