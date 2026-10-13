using System.Text;
using System.Text.Json;

namespace Tracon;

/// <summary>
/// One batch item's input, read as either plain text or a structured item that
/// carries parameter values and documents beside its message.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The shape.</strong> An item whose text is a JSON object with a
/// <em>string</em> member named <c>message</c> is structured:
/// <c>{"message": "...", "parameters": {"tone": "formal"}, "documents": [{"name": "...", "content": "..."}], "sessionId": "..."}</c>.
/// Every other item is plain text, exactly as before.
/// </para>
/// <para>
/// <strong><c>sessionId</c> is for a workflow item only.</strong> It names the
/// execution session the item's workflow run uses, so the host can correlate the
/// run with its own record (a production id): the run, its
/// <c>run.completed</c> webhook and a run that resumes it carry the same id. It
/// is a correlation, not a de-duplication: two items with the same id are two
/// runs. The id follows the session id rule (at most 128 letters, digits,
/// <c>-</c> or <c>_</c>); an item that breaks it fails on its own. An agent batch
/// item that names one fails too, because an agent batch runs without a session.
/// </para>
/// <para>
/// <strong>The ambiguity is real and documented.</strong> A job item is stored
/// as text: <see cref="JobPayload.ExtractItems"/> turns a JSON string element and
/// a JSON object element into the same kind of text, and <c>job_items.input</c>
/// keeps no type. A plain-text item whose own content happens to be a JSON object
/// with a string <c>message</c> member is therefore read as structured. A real
/// collision would need a type marker on the stored item (a migration); that is
/// not added until a collision is measured.
/// </para>
/// <para>
/// <strong>Text from outside the operator's hands is wrapped.</strong> A webhook
/// body or a field of one is the sender's text, not the operator's. Read as an
/// item it could name parameter values, which reach the model's instructions.
/// <see cref="Envelope"/> turns such text into <c>{"message": "..."}</c>, so the
/// whole text stays the message whatever it contains.
/// </para>
/// <para>
/// A structured item with a malformed member is not silently run as plain text:
/// <see cref="Problem"/> says why, and the caller fails that item (and only
/// that item). A member the shape does not name is refused too, so a misspelled
/// <c>parameter</c> does not vanish without a trace.
/// </para>
/// </remarks>
internal sealed record BatchItemInput
{
    private static readonly string[] KnownMembers = ["message", "parameters", "documents", "sessionId"];

    /// <summary>Gets the text to send as the user message.</summary>
    public required string Message { get; init; }

    /// <summary>Gets the parameter values. <see langword="null"/> when the item named none.</summary>
    public IReadOnlyDictionary<string, string>? Parameters { get; init; }

    /// <summary>Gets the reference documents, in order. Empty when the item named none.</summary>
    public IReadOnlyList<AgentRunDocument> Documents { get; init; } = [];

    /// <summary>
    /// Gets the execution session the item names. <see langword="null"/> when the
    /// item named none.
    /// </summary>
    public string? SessionId { get; init; }

    /// <summary>Gets whether the item had the structured shape.</summary>
    public bool IsStructured { get; init; }

    /// <summary>
    /// Gets why a structured item is malformed, or <see langword="null"/> when it is sound.
    /// Never contains a value, only names and positions.
    /// </summary>
    public string? Problem { get; init; }

    /// <summary>Gets whether the item carries at least one parameter value.</summary>
    public bool HasParameterValues => Parameters is { Count: > 0 };

    /// <summary>
    /// Wraps text so it is always read as a message and never as a structured item.
    /// </summary>
    /// <param name="message">The text of an item that came from outside the operator's hands.</param>
    /// <returns>A JSON object whose only member is the string <c>message</c>.</returns>
    public static string Envelope(string message)
    {
        ArgumentNullException.ThrowIfNull(message);

        using var stream = new MemoryStream();

        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("message", message);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>Reads one item's input.</summary>
    /// <param name="input">The stored item text.</param>
    /// <returns>The parsed item; a plain-text item comes back as itself.</returns>
    public static BatchItemInput Parse(string input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var plain = new BatchItemInput { Message = input };

        // Most items are plain text; skip the parser for anything that cannot
        // start an object.
        var trimmed = input.AsSpan().TrimStart();

        if (trimmed.Length == 0 || trimmed[0] != '{')
        {
            return plain;
        }

        JsonDocument document;

        try
        {
            document = JsonDocument.Parse(input);
        }
        catch (JsonException)
        {
            return plain;
        }

        using (document)
        {
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("message", out var message) ||
                message.ValueKind != JsonValueKind.String)
            {
                return plain;
            }

            foreach (var member in root.EnumerateObject())
            {
                if (!KnownMembers.Contains(member.Name, StringComparer.Ordinal))
                {
                    return Malformed($"it has an unknown member '{member.Name}' (expected message, parameters, documents, sessionId)");
                }
            }

            IReadOnlyDictionary<string, string>? parameters = null;

            if (root.TryGetProperty("parameters", out var parameterElement) && parameterElement.ValueKind != JsonValueKind.Null)
            {
                if (parameterElement.ValueKind != JsonValueKind.Object)
                {
                    return Malformed("'parameters' must be an object of string values");
                }

                var values = new Dictionary<string, string>(StringComparer.Ordinal);

                foreach (var property in parameterElement.EnumerateObject())
                {
                    if (property.Value.ValueKind != JsonValueKind.String)
                    {
                        return Malformed($"the value of parameter '{property.Name}' is not a string");
                    }

                    values[property.Name] = property.Value.GetString()!;
                }

                parameters = values;
            }

            var documents = new List<AgentRunDocument>();

            if (root.TryGetProperty("documents", out var documentElement) && documentElement.ValueKind != JsonValueKind.Null)
            {
                if (documentElement.ValueKind != JsonValueKind.Array)
                {
                    return Malformed("'documents' must be an array of {name, content} objects");
                }

                var position = 0;

                foreach (var item in documentElement.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object ||
                        !item.TryGetProperty("name", out var name) || name.ValueKind != JsonValueKind.String ||
                        !item.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.String)
                    {
                        return Malformed($"document {position} must be an object with a string 'name' and a string 'content'");
                    }

                    documents.Add(new AgentRunDocument { Name = name.GetString()!, Content = content.GetString()! });
                    position++;
                }
            }

            string? sessionId = null;

            if (root.TryGetProperty("sessionId", out var sessionElement) && sessionElement.ValueKind != JsonValueKind.Null)
            {
                if (sessionElement.ValueKind != JsonValueKind.String || sessionElement.GetString() is not { Length: > 0 } text)
                {
                    return Malformed("'sessionId' must be a non-empty string");
                }

                // The reason names the rule, never the value: the item text is
                // written to the job's error, and an over-long id is still text
                // the sender chose.
                if (WorkflowSessionId.Problem(text) is { } sessionProblem)
                {
                    return Malformed($"'sessionId' is not valid. {sessionProblem}");
                }

                sessionId = text;
            }

            return new BatchItemInput
            {
                Message = message.GetString()!,
                Parameters = parameters,
                Documents = documents,
                SessionId = sessionId,
                IsStructured = true,
            };
        }

        BatchItemInput Malformed(string reason)
            => new() { Message = input, IsStructured = true, Problem = $"The batch item is malformed: {reason}." };
    }
}
