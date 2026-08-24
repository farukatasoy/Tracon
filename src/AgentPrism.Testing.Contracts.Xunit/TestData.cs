using System.Text.Json;

namespace AgentPrism.Testing.Contracts.Storage;

/// <summary>Sample data used by the tests.</summary>
public static class TestData
{
    /// <summary>Produces a sample agent definition.</summary>
    /// <param name="name">The agent name.</param>
    /// <returns>The definition.</returns>
    public static AgentDefinition Definition(string name)
        => new()
        {
            Name = name,
            DisplayName = $"{name} agent",
            Description = "Test definition.",
            Instructions = "Give a short answer.",
            Model = new ModelBinding { Provider = "echo", Model = "echo-1", Temperature = 0.5f },
            ToolNames = ["alpha", "beta"],
        };

    /// <summary>Produces a sample run start.</summary>
    /// <param name="runId">The run ID.</param>
    /// <param name="agentName">The agent name.</param>
    /// <returns>The start info.</returns>
    public static RunStartInfo Run(Guid runId, string agentName = "test-agent")
        => new()
        {
            RunId = runId,
            AgentName = agentName,
            StartedAt = DateTimeOffset.UtcNow,
        };

    /// <summary>Produces a sample run event.</summary>
    /// <param name="runId">The run ID.</param>
    /// <param name="sequence">The sequence number.</param>
    /// <returns>The event.</returns>
    public static RunEvent Event(Guid runId, long sequence)
        => new()
        {
            RunId = runId,
            Sequence = sequence,
            Type = RunEventType.MessageDelta,
            Timestamp = DateTimeOffset.UtcNow,
            Text = $"chunk {sequence}",
        };

    /// <summary>Produces a sample session record.</summary>
    /// <param name="sessionId">The session ID.</param>
    /// <param name="state">The serialized state. A sample object is used when omitted.</param>
    /// <returns>The session record.</returns>
    public static SessionRecord Session(string sessionId, JsonElement? state = null)
    {
        var now = DateTimeOffset.UtcNow;

        return new SessionRecord
        {
            Id = sessionId,
            AgentName = "test-agent",
            State = state ?? State("""{"messages":[{"role":"user","text":"hello"}],"turn":3}"""),
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    /// <summary>Converts raw JSON text to a <see cref="JsonElement"/>.</summary>
    /// <param name="json">The JSON text.</param>
    /// <returns>A standalone JSON element.</returns>
    public static JsonElement State(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    /// <summary>Produces a sample queued job. Ready to pass to <c>EnqueueAsync</c>.</summary>
    /// <param name="tenantId">The tenant ID.</param>
    /// <param name="scheduledFor">The time the job is due to run. Defaults to now.</param>
    /// <returns>The job record.</returns>
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
            Payload = State("""["input"]"""),
            ScheduledFor = scheduledFor ?? now,
            CreatedAt = now,
        };
    }

    /// <summary>Produces a sample schedule.</summary>
    /// <param name="tenantId">The tenant ID.</param>
    /// <param name="name">The schedule name.</param>
    /// <returns>The schedule.</returns>
    public static JobSchedule Schedule(string tenantId = "default", string name = "night-report")
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
            Payload = State("""["input"]"""),
            Enabled = true,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    /// <summary>Produces a sample eval suite.</summary>
    /// <param name="tenantId">The tenant ID.</param>
    /// <param name="name">The suite name.</param>
    /// <returns>The suite.</returns>
    public static EvalSuite EvalSuite(string tenantId = "default", string name = "customer-support-team")
        => new()
        {
            TenantId = tenantId,
            Name = name,
            AgentName = "test-agent",
            Checks = State("""[{"kind":"nonEmpty","minLength":1}]"""),
        };

    /// <summary>Produces a sample eval case.</summary>
    /// <param name="suiteId">The ID of the suite it belongs to.</param>
    /// <param name="query">The query text.</param>
    /// <returns>The case.</returns>
    public static EvalCase EvalCase(Guid suiteId, string query = "question")
        => new() { SuiteId = suiteId, Seq = 0, Query = query };

    /// <summary>Produces a sample eval case draft, as if promoted from a production run.</summary>
    /// <param name="sourceRunId">The ID of the promoted run.</param>
    /// <param name="query">The query text.</param>
    /// <returns>The draft.</returns>
    public static EvalCaseDraft EvalCaseDraft(Guid? sourceRunId = null, string query = "question")
        => new()
        {
            Query = query,
            SourceRunId = sourceRunId,
            SourceKind = sourceRunId is null ? null : EvalCaseSource.FailedRun,
        };

    /// <summary>Produces a sample eval run.</summary>
    /// <param name="tenantId">The tenant ID.</param>
    /// <param name="suiteId">The ID of the suite being measured.</param>
    /// <returns>The run.</returns>
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
