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
/// <c>{"message": "...", "parameters": {"tone": "formal"}, "documents": [{"name": "...", "content": "..."}]}</c>.
/// Every other item is plain text, exactly as before.
/// </para>
/// <para>
/// 🚨 <strong>The ambiguity is real and documented.</strong> A job item is stored
/// as text: <see cref="JobPayload.ExtractItems"/> turns a JSON string element and
/// a JSON object element into the same kind of text, and <c>job_items.input</c>
/// keeps no type. A plain-text item whose own content happens to be a JSON object
/// with a string <c>message</c> member is therefore read as structured. A real
/// collision would need a type marker on the stored item (a migration); that is
/// not added until a collision is measured.
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
    private static readonly string[] KnownMembers = ["message", "parameters", "documents"];

    /// <summary>Gets the text to send as the user message.</summary>
    public required string Message { get; init; }

    /// <summary>Gets the parameter values. <see langword="null"/> when the item named none.</summary>
    public IReadOnlyDictionary<string, string>? Parameters { get; init; }

    /// <summary>Gets the reference documents, in order. Empty when the item named none.</summary>
    public IReadOnlyList<AgentRunDocument> Documents { get; init; } = [];

    /// <summary>Gets whether the item had the structured shape.</summary>
    public bool IsStructured { get; init; }

    /// <summary>
    /// Gets why a structured item is malformed, or <see langword="null"/> when it is sound.
    /// Never contains a value, only names and positions.
    /// </summary>
    public string? Problem { get; init; }

    /// <summary>Gets whether the item carries at least one parameter value.</summary>
    public bool HasParameterValues => Parameters is { Count: > 0 };

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
                    return Malformed($"it has an unknown member '{member.Name}' (expected message, parameters, documents)");
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

            return new BatchItemInput
            {
                Message = message.GetString()!,
                Parameters = parameters,
                Documents = documents,
                IsStructured = true,
            };
        }

        BatchItemInput Malformed(string reason)
            => new() { Message = input, IsStructured = true, Problem = $"The batch item is malformed: {reason}." };
    }
}
