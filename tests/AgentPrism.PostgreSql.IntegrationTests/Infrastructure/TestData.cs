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
}
