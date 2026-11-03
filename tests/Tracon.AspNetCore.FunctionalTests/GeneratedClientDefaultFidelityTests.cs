using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using Generated = Tracon.Client.Generated;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A request member the server fills in when it is left out starts with the same value in three places: the server's
/// own type, the published document, and the generated client's type.
/// </summary>
/// <remarks>
/// <para>
/// The generated client writes every property it has, so a caller that sets only the required ones sends the value the
/// client type starts with. If the server's default is not the CLR default (<c>Enabled = true</c>, a time zone of
/// <c>"UTC"</c>, a tool mode that is not the first enum member) and the client starts with the CLR default, the call
/// succeeds and records something else: a <c>200</c> for a request that did not say what it was taken to say. For a
/// member that guards something, the wrong value is the unsafe one (an MCP server registered with its approval gate
/// off).
/// </para>
/// <para>
/// This is the class gate, not a list of the cases found: it walks every member of every request schema the document
/// reaches and requires, for each one whose server default is not the type default, that the document declares it. The
/// walk is behavioural (it constructs the server type and the client type and reads the property), so a default added
/// to a request type without the matching declaration is red here, and so is one the generator did not carry into the
/// client.
/// </para>
/// </remarks>
public sealed class GeneratedClientDefaultFidelityTests
{
    private static readonly JsonSerializerOptions WireOptions = new() { Converters = { new JsonStringEnumConverter() } };

    [Fact]
    public async Task A_request_member_with_a_server_default_declares_it_and_the_generated_client_starts_with_it()
    {
        var document = await FetchDocumentAsync();
        var members = RequestMembers(document).ToList();
        var serverTypes = TypesByName(typeof(TraconEndpointOptions).Assembly, typeof(ModelBinding).Assembly, typeof(TraconOptions).Assembly);
        var clientTypes = TypesByName(typeof(Generated.TraconApiClient).Assembly);

        var undeclared = new List<string>();
        var serverMismatch = new List<string>();
        var clientMismatch = new List<string>();
        var examined = 0;
        var declared = 0;

        foreach (var member in members)
        {
            if (!serverTypes.TryGetValue(member.Schema, out var serverType) || ValueOf(serverType, member.Wire) is not { } server)
            {
                continue;
            }

            examined++;

            var serverJson = JsonSerializer.SerializeToElement(server.Value, server.Property.PropertyType, WireOptions);
            var typeDefault = TypeDefault(server.Property.PropertyType);

            if (member.Default is not { } documented)
            {
                if (!Equals(server.Value, typeDefault))
                {
                    undeclared.Add($"{member.Schema}.{member.Wire} (server starts with {serverJson.GetRawText()})");
                }

                continue;
            }

            declared++;

            if (!JsonElement.DeepEquals(documented, serverJson))
            {
                serverMismatch.Add($"{member.Schema}.{member.Wire}: document {documented.GetRawText()}, server {serverJson.GetRawText()}");
            }

            if (!clientTypes.TryGetValue(member.Schema, out var clientType) || ValueOf(clientType, member.Wire) is not { } client)
            {
                clientMismatch.Add($"{member.Schema}.{member.Wire}: no such property on the generated client");

                continue;
            }

            var clientJson = JsonSerializer.SerializeToElement(client.Value, client.Property.PropertyType, WireOptions);

            if (!JsonElement.DeepEquals(documented, clientJson))
            {
                clientMismatch.Add($"{member.Schema}.{member.Wire}: document {documented.GetRawText()}, generated client {clientJson.GetRawText()}");
            }
        }

        // A walk that found nothing would pass for the wrong reason.
        examined.ShouldBeGreaterThan(40);
        declared.ShouldBeGreaterThanOrEqualTo(14);
        undeclared.ShouldBeEmpty("the server fills these in when they are left out, but the document does not say so: " + string.Join("; ", undeclared));
        serverMismatch.ShouldBeEmpty("the document default and the server default differ: " + string.Join("; ", serverMismatch));
        clientMismatch.ShouldBeEmpty("a caller of the generated client sends something else when it leaves these out: " + string.Join("; ", clientMismatch));
    }

    [Fact]
    public async Task An_mcp_server_registered_with_only_an_endpoint_has_its_approval_gate_on_and_is_enabled()
    {
        await using var host = await OpenApiSnapshotTests.StartFullSurfaceHostAsync();
        var client = Client(host);

        var saved = await client.TraconSaveMcpServerAsync(
            "minimal-server",
            new Generated.McpServerRequest { Endpoint = "https://mcp.example.com/" });

        saved.RequiresApproval.ShouldBeTrue("a server registered without saying otherwise asks for approval");
        saved.Enabled.ShouldBeTrue();

        var read = (await client.TraconListMcpServersAsync()).Single(static server => string.Equals(server.Name, "minimal-server", StringComparison.Ordinal));

        read.RequiresApproval.ShouldBeTrue();
        read.Enabled.ShouldBeTrue();
    }

    [Fact]
    public async Task A_schedule_saved_with_only_a_handler_and_a_target_is_enabled_and_runs_in_utc()
    {
        await using var host = await OpenApiSnapshotTests.StartFullSurfaceHostAsync();
        var client = Client(host);

        var saved = await client.TraconSaveScheduleAsync(
            "minimal-schedule",
            new Generated.JobScheduleSaveRequest { HandlerKey = "tracon.agent-batch", TargetName = "kod-agent" });

        saved.Enabled.ShouldBeTrue();
        saved.TimeZone.ShouldBe("UTC");
    }

    private static Generated.TraconApiClient Client(TraconTestHost host)
    {
        host.Client.BaseAddress = new Uri("http://localhost/tracon/");

        return new Generated.TraconApiClient(host.Client);
    }

    private static object? TypeDefault(Type type) => type.IsValueType ? Activator.CreateInstance(type) : null;

    private static (PropertyInfo Property, object? Value)? ValueOf(Type type, string wireName)
    {
        var property = type.GetProperties(BindingFlags.Public | BindingFlags.Instance).FirstOrDefault(member =>
            string.Equals(
                member.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? JsonNamingPolicy.CamelCase.ConvertName(member.Name),
                wireName,
                StringComparison.Ordinal));

        if (property is null)
        {
            return null;
        }

        try
        {
            return Activator.CreateInstance(type, nonPublic: true) is { } instance ? (property, property.GetValue(instance)) : null;
        }
        catch (MissingMethodException)
        {
            // A positional type has no parameterless constructor; its members are not defaults of a request.
            return null;
        }
    }

    private static Dictionary<string, Type> TypesByName(params Assembly[] assemblies)
        => assemblies
            .SelectMany(static assembly => assembly.GetTypes())
            .Where(static type => type.IsClass)
            .GroupBy(static type => type.Name, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.Ordinal);

    /// <summary>
    /// The members of every schema reachable from a request body that the caller may leave out and that cannot be null:
    /// the ones whose omission the server answers with a default.
    /// </summary>
    private static IEnumerable<RequestMember> RequestMembers(JsonElement document)
    {
        var schemas = document.GetProperty("components").GetProperty("schemas");
        var reachable = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (_, _, operation) in OpenApiTestHelpers.EnumerateOperations(document))
        {
            if (!operation.TryGetProperty("requestBody", out var body) || !body.TryGetProperty("content", out var content))
            {
                continue;
            }

            foreach (var media in content.EnumerateObject().Where(static media => media.Value.TryGetProperty("schema", out _)))
            {
                Collect(media.Value.GetProperty("schema"), schemas, reachable);
            }
        }

        foreach (var name in reachable.OrderBy(static name => name, StringComparer.Ordinal))
        {
            var schema = schemas.GetProperty(name);

            if (!schema.TryGetProperty("properties", out var properties))
            {
                continue;
            }

            var required = schema.TryGetProperty("required", out var list)
                ? list.EnumerateArray().Select(static item => item.GetString()!).ToHashSet(StringComparer.Ordinal)
                : [];

            foreach (var property in properties.EnumerateObject().Where(property => !required.Contains(property.Name)))
            {
                if (IsScalarThatCannotBeNull(property.Value, schemas))
                {
                    yield return new RequestMember(
                        name,
                        property.Name,
                        property.Value.TryGetProperty("default", out var documented) ? documented.Clone() : null);
                }
            }
        }
    }

    private static void Collect(JsonElement schema, JsonElement schemas, HashSet<string> reachable)
    {
        if (schema.TryGetProperty("$ref", out var reference))
        {
            var name = reference.GetString()![(reference.GetString()!.LastIndexOf('/') + 1)..];

            if (reachable.Add(name))
            {
                Collect(schemas.GetProperty(name), schemas, reachable);
            }

            return;
        }

        foreach (var key in new[] { "oneOf", "anyOf", "allOf" })
        {
            if (schema.TryGetProperty(key, out var branches))
            {
                foreach (var branch in branches.EnumerateArray())
                {
                    Collect(branch, schemas, reachable);
                }
            }
        }

        if (schema.TryGetProperty("items", out var items))
        {
            Collect(items, schemas, reachable);
        }

        if (schema.TryGetProperty("additionalProperties", out var values) && values.ValueKind == JsonValueKind.Object)
        {
            Collect(values, schemas, reachable);
        }

        if (schema.TryGetProperty("properties", out var properties))
        {
            foreach (var property in properties.EnumerateObject())
            {
                Collect(property.Value, schemas, reachable);
            }
        }
    }

    private static bool IsScalarThatCannotBeNull(JsonElement property, JsonElement schemas)
    {
        if (property.TryGetProperty("$ref", out var reference))
        {
            var name = reference.GetString()![(reference.GetString()!.LastIndexOf('/') + 1)..];

            return schemas.GetProperty(name).TryGetProperty("enum", out _);
        }

        if (property.TryGetProperty("oneOf", out _) || property.TryGetProperty("anyOf", out _) || !property.TryGetProperty("type", out var type))
        {
            return false;
        }

        var kinds = type.ValueKind == JsonValueKind.Array
            ? type.EnumerateArray().Select(static item => item.GetString()!).ToList()
            : [type.GetString()!];

        return !kinds.Contains("null", StringComparer.Ordinal)
            && kinds.Exists(static kind => kind is "boolean" or "string" or "integer" or "number");
    }

    private static async Task<JsonElement> FetchDocumentAsync()
    {
        await using var host = await OpenApiSnapshotTests.StartFullSurfaceHostAsync();

        using var response = await host.Client.GetAsync(new Uri("/openapi/v1.json", UriKind.Relative));
        response.EnsureSuccessStatusCode();

        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    private sealed record RequestMember(string Schema, string Wire, JsonElement? Default);
}
