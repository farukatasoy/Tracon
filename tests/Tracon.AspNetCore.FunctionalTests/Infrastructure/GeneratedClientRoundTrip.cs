using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Tracon.Client;
using Tracon.Client.Generated;

namespace Tracon.AspNetCore.FunctionalTests.Infrastructure;

/// <summary>What happened when the generated client called one operation of the live OpenAPI document.</summary>
/// <param name="OperationId">The operation id.</param>
/// <param name="Method">The HTTP method.</param>
/// <param name="Path">The path template.</param>
/// <param name="Kind">The outcome class, one of the <see cref="RoundTripKind"/> constants.</param>
/// <param name="Status">The HTTP status when the call reached the server.</param>
/// <param name="Detail">The exception type and message when the call did not return normally.</param>
internal sealed record RoundTripOutcome(string OperationId, string Method, string Path, string Kind, int? Status, string? Detail);

/// <summary>The outcome classes of <see cref="RoundTripOutcome"/>.</summary>
internal static class RoundTripKind
{
    /// <summary>The client returned a value: a 2xx answer the client could read.</summary>
    public const string Returned = "returned";

    /// <summary>The server answered with a status the document lists; the client raised its API exception.</summary>
    public const string DocumentedStatus = "documented-status";

    /// <summary>The server answered with a status the document does not list for the operation.</summary>
    public const string UndocumentedStatus = "undocumented-status";

    /// <summary>The call raised something other than the API exception: a deserialization or client fault.</summary>
    public const string ClientFault = "client-fault";

    /// <summary>The call did not finish in the allowed time.</summary>
    public const string Timeout = "timeout";

    /// <summary>The document lists an operation the generated client has no method for.</summary>
    public const string NoClientMethod = "no-client-method";

    /// <summary>A sample argument could not be built for the operation.</summary>
    public const string NotConstructible = "not-constructible";
}

/// <summary>
/// Calls every operation of the live OpenAPI document through the generated .NET client and records how each call ended.
/// </summary>
/// <remarks>
/// <para>
/// The operation list is read from the document the running host serves, never typed in. For each operation the client
/// method is found by <c>operationId</c>, the arguments are built from the document (path and query values from the
/// parameter schemas and a small set of fixtures, the body from the request schema with its required properties only),
/// and the call is made with a deadline. A call can end in four ways that matter: the client returned a value (the
/// answer was a 2xx the generated types could read), it raised its API exception with a status the document lists, it
/// raised its API exception with a status the document does not list, or it raised anything else (a
/// <see cref="JsonException"/> from a response the types cannot read is the case this exists for).
/// </para>
/// <para>
/// Sample data reaches deep into only some operations: a path value that names nothing answers 404. The outcome counts
/// say how many operations were read back as a typed value, which is the number that grows when fixtures improve.
/// </para>
/// </remarks>
internal static class GeneratedClientRoundTrip
{
    private const string FixtureAgent = "kod-agent";
    private const string FixtureName = "sample";

    private static readonly string[] MethodNames = ["get", "post", "put", "delete", "patch"];

    private static readonly JsonSerializerOptions SampleOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Calls every operation and returns the outcomes in call order.</summary>
    /// <param name="http">The client to send with; its base address is the API prefix (<c>http://localhost/tracon/</c>).</param>
    /// <param name="document">The OpenAPI document the host serves.</param>
    /// <param name="fixtures">Values that make a path value name something real.</param>
    /// <param name="perCallTimeout">How long one call may take.</param>
    /// <param name="cancellationToken">Cancels the whole sweep.</param>
    /// <returns>One outcome per operation.</returns>
    public static async Task<IReadOnlyList<RoundTripOutcome>> RunAsync(
        HttpClient http,
        JsonElement document,
        RoundTripFixtures fixtures,
        TimeSpan perCallTimeout,
        CancellationToken cancellationToken)
    {
        var client = new TraconApiClient(http);
        var schemas = document.GetProperty("components").GetProperty("schemas");
        var outcomes = new List<RoundTripOutcome>();

        foreach (var operation in ReadOperations(document).OrderBy(Rank))
        {
            var method = typeof(TraconApiClient).GetMethod(operation.OperationId + "Async", BindingFlags.Public | BindingFlags.Instance);

            if (method is null)
            {
                outcomes.Add(Outcome(operation, RoundTripKind.NoClientMethod, null, "no method " + operation.OperationId + "Async"));

                continue;
            }

            object?[] arguments;

            try
            {
                arguments = BuildArguments(method, operation, schemas, fixtures, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                outcomes.Add(Outcome(operation, RoundTripKind.NotConstructible, null, exception.GetType().Name + ": " + exception.Message));

                continue;
            }

            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(perCallTimeout); // cancel: bound
            arguments[^1] = deadline.Token;

            outcomes.Add(await InvokeAsync(client, method, arguments, operation, deadline, cancellationToken));
        }

        return outcomes;
    }

    /// <summary>The operations of a document: method, path, id, parameters, body schema and the statuses it lists.</summary>
    /// <param name="document">The OpenAPI document.</param>
    /// <returns>The operations.</returns>
    public static IEnumerable<RoundTripOperation> ReadOperations(JsonElement document)
    {
        foreach (var path in document.GetProperty("paths").EnumerateObject())
        {
            foreach (var entry in path.Value.EnumerateObject().Where(static entry => MethodNames.Contains(entry.Name, StringComparer.Ordinal)))
            {
                var body = entry.Value;
                var parameters = body.TryGetProperty("parameters", out var listed)
                    ? listed.EnumerateArray().Select(static parameter => parameter.Clone()).ToArray()
                    : [];

                string? bodySchemaJson = null;
                string? bodyContentType = null;

                if (body.TryGetProperty("requestBody", out var requestBody) && requestBody.TryGetProperty("content", out var content))
                {
                    foreach (var media in content.EnumerateObject())
                    {
                        bodyContentType = media.Name;
                        bodySchemaJson = media.Value.TryGetProperty("schema", out var schema) ? schema.GetRawText() : "{}";

                        break;
                    }
                }

                yield return new RoundTripOperation(
                    body.GetProperty("operationId").GetString()!,
                    entry.Name.ToUpperInvariant(),
                    path.Name,
                    parameters,
                    bodySchemaJson,
                    bodyContentType,
                    [.. body.GetProperty("responses").EnumerateObject().Select(static response => response.Name)]);
            }
        }
    }

    private static int Rank(RoundTripOperation operation)
    {
        if (operation.OperationId.Contains("RunAgent", StringComparison.Ordinal))
        {
            return 0;
        }

        return operation.Method switch
        {
            "PUT" => 1,
            "POST" => 2,
            "GET" => 3,
            "PATCH" => 4,
            _ => 5,
        };
    }

    private static async Task<RoundTripOutcome> InvokeAsync(
        TraconApiClient client,
        MethodInfo method,
        object?[] arguments,
        RoundTripOperation operation,
        CancellationTokenSource deadline,
        CancellationToken cancellationToken)
    {
        try
        {
            var task = (Task)method.Invoke(client, arguments)!;
            await task;

            return Outcome(operation, RoundTripKind.Returned, null, null);
        }
        catch (TargetInvocationException wrapper) when (wrapper.InnerException is not null)
        {
            return Classify(operation, wrapper.InnerException, deadline, cancellationToken);
        }
        catch (Exception exception)
        {
            return Classify(operation, exception, deadline, cancellationToken);
        }
    }

    private static RoundTripOutcome Classify(
        RoundTripOperation operation,
        Exception exception,
        CancellationTokenSource deadline,
        CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException && deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            return Outcome(operation, RoundTripKind.Timeout, null, "the call did not finish in time");
        }

        // The generated client does not let a JsonException escape: a success answer it cannot read comes back as its API
        // exception carrying the 2xx status ("Could not deserialize the response body"). A 2xx in an exception is
        // therefore a response the generated types cannot read, never an error status.
        if (exception is TraconApiException { StatusCode: >= 200 and < 300 } unreadable)
        {
            return Outcome(operation, RoundTripKind.ClientFault, unreadable.StatusCode, "TraconApiException: " + Trim(unreadable.Message));
        }

        if (exception is TraconApiException api)
        {
            var documented = operation.Statuses.Contains(api.StatusCode.ToString(CultureInfo.InvariantCulture), StringComparer.Ordinal);
            var text = Trim(api.Message + " " + (string.IsNullOrEmpty(api.Response) ? TypedResult(api) : api.Response));

            // The server refuses a null the generated client wrote for a property its type promises is never null: the
            // generator leaves such a property at null and the serializer sends it. A caller that sets only what it cares
            // about gets a 400 for a request the types called valid.
            if (api.StatusCode == 400 && text.Contains("allow setting null values", StringComparison.Ordinal))
            {
                return Outcome(operation, RoundTripKind.ClientFault, api.StatusCode, "client sent a null the server's contract refuses: " + text);
            }

            return Outcome(operation, documented ? RoundTripKind.DocumentedStatus : RoundTripKind.UndocumentedStatus, api.StatusCode, text);
        }

        return Outcome(operation, RoundTripKind.ClientFault, null, exception.GetType().Name + ": " + Trim(exception.Message));
    }

    // The typed exception (TraconApiException<ProblemDetails>) keeps the parsed body, not its text.
    private static string? TypedResult(TraconApiException exception)
        => exception.GetType().GetProperty("Result")?.GetValue(exception) is { } result
            ? JsonSerializer.Serialize(result, result.GetType(), SampleOptions)
            : null;

    private static RoundTripOutcome Outcome(RoundTripOperation operation, string kind, int? status, string? detail)
        => new(operation.OperationId, operation.Method, operation.Path, kind, status, detail);

    private static string Trim(string text) => text.Length <= 700 ? text : text[..700];

    private static object?[] BuildArguments(
        MethodInfo method,
        RoundTripOperation operation,
        JsonElement schemas,
        RoundTripFixtures fixtures,
        CancellationToken cancellationToken)
    {
        var parameters = method.GetParameters();
        var arguments = new object?[parameters.Length];

        for (var index = 0; index < parameters.Length; index++)
        {
            var parameter = parameters[index];

            if (parameter.ParameterType == typeof(CancellationToken))
            {
                arguments[index] = cancellationToken;

                continue;
            }

            if ((Nullable.GetUnderlyingType(parameter.ParameterType) ?? parameter.ParameterType) == typeof(FileParameter))
            {
                arguments[index] = new FileParameter(new MemoryStream("sample"u8.ToArray()), "sample.txt", "text/plain");

                continue;
            }

            if (string.Equals(parameter.Name, "body", StringComparison.Ordinal))
            {
                arguments[index] = BuildBody(parameter, operation, schemas, fixtures);

                continue;
            }

            var listed = operation.Parameters.FirstOrDefault(candidate =>
                string.Equals(Normalize(candidate.GetProperty("name").GetString()!), Normalize(parameter.Name!), StringComparison.Ordinal));

            var isPath = listed.ValueKind == JsonValueKind.Object
                && string.Equals(listed.GetProperty("in").GetString(), "path", StringComparison.Ordinal);

            if (!isPath && parameter.HasDefaultValue)
            {
                arguments[index] = parameter.DefaultValue;

                continue;
            }

            arguments[index] = ParameterValue(parameter.ParameterType, parameter.Name!, operation, listed, schemas, fixtures);
        }

        return arguments;
    }

    private static object? BuildBody(ParameterInfo parameter, RoundTripOperation operation, JsonElement schemas, RoundTripFixtures fixtures)
    {
        var type = Nullable.GetUnderlyingType(parameter.ParameterType) ?? parameter.ParameterType;

        if (type == typeof(FileParameter))
        {
            return new FileParameter(new MemoryStream("sample"u8.ToArray()), "sample.txt", "text/plain");
        }

        if (operation.BodySchemaJson is null)
        {
            return parameter.HasDefaultValue ? parameter.DefaultValue : null;
        }

        using var schema = JsonDocument.Parse(operation.BodySchemaJson);
        var sample = SampleFor(schema.RootElement, schemas, depth: 0) ?? new JsonObject();

        if (fixtures.BodyOverrides.TryGetValue(operation.OperationId, out var overridden))
        {
            sample = JsonNode.Parse(overridden)!;
        }

        if (type == typeof(JsonElement))
        {
            return JsonSerializer.SerializeToElement(sample);
        }

        return sample.Deserialize(type, SampleOptions);
    }

    private static object? ParameterValue(
        Type type,
        string name,
        RoundTripOperation operation,
        JsonElement listed,
        JsonElement schemas,
        RoundTripFixtures fixtures)
    {
        var target = Nullable.GetUnderlyingType(type) ?? type;
        var normalized = Normalize(name);

        if (target == typeof(Guid))
        {
            return RoundTripFixtures.IsRunKey(normalized) ? fixtures.RunId : Guid.Parse("11111111-1111-1111-1111-111111111111");
        }

        if (target == typeof(string))
        {
            return normalized switch
            {
                "sessionid" => fixtures.SessionId,
                "tenantid" or "slug" => "default",
                "name" or "skillname" => operation.Path.Contains("/agents", StringComparison.Ordinal) ? FixtureAgent : FixtureName,
                "id" => fixtures.RunId.ToString(),
                _ => EnumOrDefault(listed, schemas) ?? FixtureName,
            };
        }

        if (target == typeof(int) || target == typeof(long) || target == typeof(short))
        {
            return Convert.ChangeType(1, target, CultureInfo.InvariantCulture);
        }

        if (target == typeof(double) || target == typeof(float) || target == typeof(decimal))
        {
            return Convert.ChangeType(1, target, CultureInfo.InvariantCulture);
        }

        if (target == typeof(bool))
        {
            return false;
        }

        if (target == typeof(DateTimeOffset))
        {
            return new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        }

        if (target.IsEnum)
        {
            return Enum.GetValues(target).GetValue(0);
        }

        if (target.IsGenericType && target.GetGenericArguments() is [var element]
            && (target.GetGenericTypeDefinition() == typeof(ICollection<>) || target.GetGenericTypeDefinition() == typeof(IEnumerable<>)))
        {
            return Activator.CreateInstance(typeof(List<>).MakeGenericType(element));
        }

        throw new NotSupportedException($"No sample for parameter '{name}' of type {type}.");
    }

    private static string? EnumOrDefault(JsonElement listed, JsonElement schemas)
    {
        if (listed.ValueKind != JsonValueKind.Object || !listed.TryGetProperty("schema", out var schema))
        {
            return null;
        }

        schema = Resolve(schema, schemas);

        return schema.TryGetProperty("enum", out var values) && values.GetArrayLength() > 0 ? values[0].ToString() : null;
    }

    private static string Normalize(string name) => new([.. name.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant)]);

    private static JsonElement Resolve(JsonElement schema, JsonElement schemas)
    {
        while (schema.ValueKind == JsonValueKind.Object && schema.TryGetProperty("$ref", out var reference))
        {
            var name = reference.GetString()![(reference.GetString()!.LastIndexOf('/') + 1)..];
            schema = schemas.GetProperty(name);
        }

        return schema;
    }

    private static JsonNode? SampleFor(JsonElement schema, JsonElement schemas, int depth)
    {
        if (depth > 6)
        {
            return null;
        }

        schema = Resolve(schema, schemas);

        if (schema.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (schema.TryGetProperty("enum", out var values) && values.GetArrayLength() > 0)
        {
            return JsonNode.Parse(values[0].GetRawText());
        }

        foreach (var key in new[] { "oneOf", "anyOf" })
        {
            if (schema.TryGetProperty(key, out var branches))
            {
                foreach (var branch in branches.EnumerateArray())
                {
                    if (branch.TryGetProperty("type", out var branchType) && string.Equals(branchType.ToString(), "null", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    return SampleFor(branch, schemas, depth + 1);
                }

                return null;
            }
        }

        if (schema.TryGetProperty("allOf", out var parts))
        {
            var merged = new JsonObject();

            foreach (var part in parts.EnumerateArray())
            {
                if (SampleFor(part, schemas, depth + 1) is JsonObject piece)
                {
                    foreach (var property in piece.ToArray())
                    {
                        piece.Remove(property.Key);
                        merged[property.Key] = property.Value;
                    }
                }
            }

            return merged;
        }

        var kind = TypeOf(schema);

        switch (kind)
        {
            case "string":
                return JsonValue.Create(StringFor(schema));
            case "integer":
                return JsonValue.Create(schema.TryGetProperty("minimum", out var minimum) && minimum.TryGetInt64(out var low) ? Math.Max(low, 1) : 1);
            case "number":
                return JsonValue.Create(1);
            case "boolean":
                return JsonValue.Create(false);
            case "array":
                return new JsonArray();
            default:
                var result = new JsonObject();

                if (schema.TryGetProperty("properties", out var properties))
                {
                    var required = schema.TryGetProperty("required", out var list)
                        ? list.EnumerateArray().Select(static item => item.GetString()!).ToHashSet(StringComparer.Ordinal)
                        : [];

                    foreach (var property in properties.EnumerateObject().Where(property => required.Contains(property.Name)))
                    {
                        if (SampleFor(property.Value, schemas, depth + 1) is { } value)
                        {
                            result[property.Name] = value;
                        }
                    }
                }

                return result;
        }
    }

    private static string TypeOf(JsonElement schema)
    {
        if (!schema.TryGetProperty("type", out var type))
        {
            return "object";
        }

        return type.ValueKind == JsonValueKind.Array
            ? type.EnumerateArray().Select(static item => item.GetString()!).FirstOrDefault(static item => !string.Equals(item, "null", StringComparison.Ordinal)) ?? "object"
            : type.GetString()!;
    }

    private static string StringFor(JsonElement schema)
    {
        var format = schema.TryGetProperty("format", out var value) ? value.GetString() : null;

        // A time span is a string with a pattern; "sample" is not one.
        if (schema.TryGetProperty("pattern", out var pattern) && pattern.GetString()!.Contains(@"\d{2}:\d{2}:\d{2}", StringComparison.Ordinal))
        {
            return "00:00:30";
        }

        return format switch
        {
            "uuid" => "11111111-1111-1111-1111-111111111111",
            "date-time" => "2026-01-01T00:00:00+00:00",
            "date" => "2026-01-01",
            "uri" => "https://example.test/",
            "byte" => "c2FtcGxl",
            _ => FixtureName,
        };
    }
}

/// <summary>One operation of an OpenAPI document, as <see cref="GeneratedClientRoundTrip"/> reads it.</summary>
/// <param name="OperationId">The operation id.</param>
/// <param name="Method">The upper-case HTTP method.</param>
/// <param name="Path">The path template.</param>
/// <param name="Parameters">The parameter objects.</param>
/// <param name="BodySchemaJson">The request body schema, or <see langword="null"/>.</param>
/// <param name="BodyContentType">The request body media type, or <see langword="null"/>.</param>
/// <param name="Statuses">The status codes the document lists.</param>
internal sealed record RoundTripOperation(
    string OperationId,
    string Method,
    string Path,
    JsonElement[] Parameters,
    string? BodySchemaJson,
    string? BodyContentType,
    string[] Statuses);

/// <summary>Values that make a sample path value name something real on the host.</summary>
/// <param name="RunId">A run that exists.</param>
/// <param name="SessionId">A session that exists.</param>
internal sealed record RoundTripFixtures(Guid RunId, string SessionId)
{
    /// <summary>Bodies that replace the one built from the schema, by operation id.</summary>
    public Dictionary<string, string> BodyOverrides { get; } = new(StringComparer.Ordinal);

    /// <summary>Whether a normalized parameter name stands for a run id.</summary>
    /// <param name="normalized">The lower-case alphanumeric parameter name.</param>
    /// <returns><see langword="true"/> when the value should be the fixture run.</returns>
    public static bool IsRunKey(string normalized) => normalized is "runid" or "id" or "a" or "b" or "scoreid" or "ruleid";
}

/// <summary>Sends every request through another client, so a sweep can use a base address of its own.</summary>
/// <param name="inner">The client that reaches the host.</param>
internal class ForwardingHandler(HttpClient inner) : HttpMessageHandler
{
    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using var forwarded = new HttpRequestMessage(request.Method, request.RequestUri) { Content = request.Content };

        foreach (var header in request.Headers)
        {
            forwarded.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        return await inner.SendAsync(forwarded, HttpCompletionOption.ResponseContentRead, cancellationToken);
    }
}
