using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Tracon;

/// <summary>
/// The payload of a <see cref="JobHandlerKeys.WorkflowRun"/> job: one workflow
/// operation, queued with <c>Prefer: respond-async</c>.
/// </summary>
/// <remarks>
/// <para>
/// The HTTP layer writes it and <see cref="WorkflowRunJobHandler"/> reads it, so
/// the field names live in one place. The worker does not see the request that
/// queued the job: everything the run needs travels here, including the
/// attribution of whoever queued it (<c>userId</c>, <c>labels</c>).
/// </para>
/// <para>
/// <c>jobs.payload</c> is not a protected column: a run's message and
/// parameter values, and a response's text and data, are stored here as plain
/// text until the job's retention removes them.
/// </para>
/// <para>
/// A payload is durable data, so the worker reads it again as untrusted input: a
/// field that is malformed fails the job, except the attribution, which is dropped
/// with a warning like a queued agent run's.
/// </para>
/// </remarks>
internal static class WorkflowRunJobPayload
{
    /// <summary>Starts a new run of the workflow.</summary>
    public const string RunOperation = "run";

    /// <summary>Resumes a run from a checkpoint.</summary>
    public const string ResumeOperation = "resume";

    /// <summary>Answers a run's pending request.</summary>
    public const string RespondOperation = "respond";

    /// <summary>Writes the payload.</summary>
    /// <param name="request">The operation to queue.</param>
    /// <returns>The payload.</returns>
    /// <remarks>Written by hand with <see cref="Utf8JsonWriter"/>, so it stays AOT-compatible.</remarks>
    public static JsonElement Write(WorkflowRunJobRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var buffer = new MemoryStream();

        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("operation", request.Operation);
            writer.WriteString("runId", request.RunId);
            WriteOptional(writer, "sessionId", request.SessionId);
            WriteOptional(writer, "userId", request.UserId);
            WriteMap(writer, "labels", request.Labels);
            WriteOptional(writer, "message", request.Message);
            WriteMap(writer, "parameters", request.Parameters);

            if (request.SourceRunId is { } sourceRunId)
            {
                writer.WriteString("sourceRunId", sourceRunId);
            }

            WriteOptional(writer, "checkpointId", request.CheckpointId);

            if (request.DefinitionVersion is { } definitionVersion)
            {
                writer.WriteNumber("definitionVersion", definitionVersion);
            }

            WriteOptional(writer, "requestId", request.RequestId);

            if (request.Approved is { } approved)
            {
                writer.WriteBoolean("approved", approved);
            }

            WriteOptional(writer, "text", request.Text);

            if (request.Json is { } json)
            {
                writer.WritePropertyName("data");
                writer.WriteRawValue(json);
            }

            writer.WriteEndObject();
        }

        using var document = JsonDocument.Parse(buffer.ToArray());

        return document.RootElement.Clone();
    }

    /// <summary>Reads the payload as untrusted input.</summary>
    /// <param name="payload">The job's payload.</param>
    /// <param name="logger">Where a dropped attribution is reported.</param>
    /// <returns>
    /// The operation, or the reason the payload cannot be run. The run id is
    /// returned whenever it could be read, so the caller can close its row.
    /// </returns>
    public static (WorkflowRunJobRequest? Request, Guid? RunId, string? Problem) Read(JsonElement payload, ILogger? logger)
    {
        if (payload.ValueKind != JsonValueKind.Object)
        {
            return Refused(null, "it must be a JSON object");
        }

        if (!TryGetGuid(payload, "runId", out var runId))
        {
            return Refused(null, "'runId' must be a valid identifier");
        }

        var operation = payload.TryGetProperty("operation", out var operationElement) &&
                        operationElement.ValueKind == JsonValueKind.String
            ? operationElement.GetString()
            : null;

        if (operation is not (RunOperation or ResumeOperation or RespondOperation))
        {
            return Refused(runId, "'operation' must be 'run', 'resume' or 'respond'");
        }

        if (!TryGetOptionalString(payload, "sessionId", out var sessionId) ||
            (sessionId is not null && WorkflowSessionId.Problem(sessionId) is not null))
        {
            return Refused(runId, "'sessionId' is not a valid execution session id");
        }

        if (!TryGetOptionalString(payload, "message", out var message) ||
            !TryGetOptionalString(payload, "checkpointId", out var checkpointId) ||
            !TryGetOptionalString(payload, "requestId", out var requestId) ||
            !TryGetOptionalString(payload, "text", out var text))
        {
            return Refused(runId, "a text field ('message', 'checkpointId', 'requestId' or 'text') is not a string");
        }

        if (!TryGetMap(payload, "parameters", out var parameters))
        {
            return Refused(runId, "'parameters' must be an object of string values");
        }

        int? definitionVersion = null;

        if (payload.TryGetProperty("definitionVersion", out var versionElement) && versionElement.ValueKind != JsonValueKind.Null)
        {
            if (versionElement.ValueKind != JsonValueKind.Number ||
                !versionElement.TryGetInt32(out var version) ||
                version < 1)
            {
                return Refused(runId, "'definitionVersion' must be a whole number of 1 or more");
            }

            definitionVersion = version;
        }

        bool? approved = null;

        if (payload.TryGetProperty("approved", out var approvedElement) && approvedElement.ValueKind != JsonValueKind.Null)
        {
            if (approvedElement.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                return Refused(runId, "'approved' must be a boolean");
            }

            approved = approvedElement.GetBoolean();
        }

        var json = payload.TryGetProperty("data", out var dataElement) && dataElement.ValueKind != JsonValueKind.Null
            ? dataElement.GetRawText()
            : null;

        Guid? sourceRunId = null;

        if (operation is not RunOperation)
        {
            if (!TryGetGuid(payload, "sourceRunId", out var source))
            {
                return Refused(runId, $"a '{operation}' operation needs 'sourceRunId', the run it continues");
            }

            sourceRunId = source;
        }

        if (operation is RespondOperation && string.IsNullOrWhiteSpace(requestId))
        {
            return Refused(runId, "a 'respond' operation needs 'requestId'");
        }

        // Dropped rather than refused, like a queued agent run's: AmbientRunAttributionScope
        // rejects an over-long identity, and a refused payload would fail on every lease.
        var userId = payload.TryGetProperty("userId", out var userElement) &&
                     userElement.ValueKind == JsonValueKind.String &&
                     userElement.GetString() is { } user &&
                     RunLabels.ValidateUserId(user) is null
            ? user
            : null;

        var labels = QueuedRunAttribution.ReadLabels(payload, out var labelProblem);

        if (labelProblem is not null && logger is not null && logger.IsEnabled(LogLevel.Warning))
        {
            logger.LogWarning("Queued workflow run {RunId} runs without labels: {Problem}", runId, labelProblem);
        }

        return (
            new WorkflowRunJobRequest
            {
                Operation = operation,
                RunId = runId,
                SessionId = sessionId,
                UserId = userId,
                Labels = labels,
                Message = message,
                Parameters = parameters,
                SourceRunId = sourceRunId,
                CheckpointId = checkpointId,
                DefinitionVersion = definitionVersion,
                RequestId = requestId,
                Approved = approved,
                Text = text,
                Json = json,
            },
            runId,
            null);

        static (WorkflowRunJobRequest?, Guid?, string?) Refused(Guid? runId, string reason)
            => (null, runId, $"The {JobHandlerKeys.WorkflowRun} job payload is not valid: {reason}.");
    }

    private static void WriteOptional(Utf8JsonWriter writer, string name, string? value)
    {
        if (value is not null)
        {
            writer.WriteString(name, value);
        }
    }

    private static void WriteMap(Utf8JsonWriter writer, string name, IReadOnlyDictionary<string, string>? values)
    {
        if (values is null)
        {
            return;
        }

        writer.WriteStartObject(name);

        foreach (var pair in values)
        {
            writer.WriteString(pair.Key, pair.Value);
        }

        writer.WriteEndObject();
    }

    private static bool TryGetGuid(JsonElement payload, string name, out Guid value)
    {
        value = default;

        return payload.TryGetProperty(name, out var element) &&
               element.ValueKind == JsonValueKind.String &&
               Guid.TryParse(element.GetString(), out value);
    }

    private static bool TryGetOptionalString(JsonElement payload, string name, out string? value)
    {
        value = null;

        if (!payload.TryGetProperty(name, out var element) || element.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (element.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = element.GetString();

        return true;
    }

    private static bool TryGetMap(JsonElement payload, string name, out IReadOnlyDictionary<string, string>? value)
    {
        value = null;

        if (!payload.TryGetProperty(name, out var element) || element.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var map = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var property in element.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            map[property.Name] = property.Value.GetString()!;
        }

        value = map;

        return true;
    }
}

/// <summary>One queued workflow operation, as <see cref="WorkflowRunJobPayload"/> carries it.</summary>
internal sealed record WorkflowRunJobRequest
{
    /// <summary>Gets the operation: <c>run</c>, <c>resume</c> or <c>respond</c>.</summary>
    public required string Operation { get; init; }

    /// <summary>Gets the id of the run the operation opens; the job's id too.</summary>
    public required Guid RunId { get; init; }

    /// <summary>Gets the execution session of a <c>run</c>; resume and respond use their source run's.</summary>
    public string? SessionId { get; init; }

    /// <summary>Gets the user the job was queued by.</summary>
    public string? UserId { get; init; }

    /// <summary>Gets the labels the job was queued with.</summary>
    public IReadOnlyDictionary<string, string>? Labels { get; init; }

    /// <summary>Gets the message of a <c>run</c>.</summary>
    public string? Message { get; init; }

    /// <summary>Gets the parameter values of a <c>run</c>.</summary>
    public IReadOnlyDictionary<string, string>? Parameters { get; init; }

    /// <summary>Gets the run a <c>resume</c> or <c>respond</c> continues.</summary>
    public Guid? SourceRunId { get; init; }

    /// <summary>Gets the checkpoint a <c>resume</c> or <c>respond</c> names; the latest when <see langword="null"/>.</summary>
    public string? CheckpointId { get; init; }

    /// <summary>
    /// Gets the workflow definition version a <c>resume</c> or <c>respond</c> names;
    /// the version the source run recorded when <see langword="null"/>.
    /// </summary>
    public int? DefinitionVersion { get; init; }

    /// <summary>Gets the request a <c>respond</c> answers.</summary>
    public string? RequestId { get; init; }

    /// <summary>Gets the yes/no answer of a <c>respond</c>.</summary>
    public bool? Approved { get; init; }

    /// <summary>Gets the text answer of a <c>respond</c>.</summary>
    public string? Text { get; init; }

    /// <summary>Gets the JSON answer of a <c>respond</c>, as raw JSON text.</summary>
    public string? Json { get; init; }
}
