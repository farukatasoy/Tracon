using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Tracon.Api;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// The document the host serves says what the server really accepts and returns: the shapes the generator writes
/// loosely are corrected, and a status the server answers for a missing capability is listed.
/// </summary>
/// <remarks>
/// <para>
/// 🚨 <c>OpenApiSnapshotTests</c> proves the committed file equals the document the host produces. It cannot say the
/// document is RIGHT: a snapshot refreshed over a regression is green. These tests compute their answer from the live
/// document, so a correction that stops being applied (or a status that is dropped) turns them red.
/// </para>
/// <para>
/// The second correction is the one with a cost on the other side. A tool that reads the document as a type system
/// (the TypeScript generator, the .NET client generator) reads <c>anyOf</c> and <c>oneOf</c> differently; the generated
/// client tests (<c>ClientSchemaFieldCoverageTests</c>, <c>GeneratedClientMinimalRequestTests</c>) are what prove the
/// rewritten shape still generates a nullable member.
/// </para>
/// </remarks>
public sealed class OpenApiDocumentShapeTests
{
    /// <summary>The operations that answer <c>501</c> when the capability behind them is not registered.</summary>
    public static TheoryData<string> OperationsThatAnswerNotImplemented { get; } = new()
    {
        "TraconVoiceHealth",
        "TraconRefreshMcpTools",
        "TraconListMcpPrompts",
        "TraconGetMcpPrompt",
        "TraconListMcpResources",
        "TraconReadMcpResource",
        "TraconStartMcpOAuth",
        "TraconGetWorkflowGraph",
        "TraconListWorkflowRequests",
    };

    [Fact]
    public async Task No_nullable_member_is_written_with_two_branches_that_both_accept_null()
    {
        var document = await FetchDocumentAsync();
        var schemas = document.GetProperty("components").GetProperty("schemas");
        var wrapped = 0;
        var offenders = new List<string>();

        foreach (var schema in schemas.EnumerateObject())
        {
            if (!schema.Value.TryGetProperty("properties", out var properties))
            {
                continue;
            }

            foreach (var property in properties.EnumerateObject())
            {
                if (!property.Value.TryGetProperty("oneOf", out var branches))
                {
                    continue;
                }

                var accepting = branches.EnumerateArray().Count(branch => AcceptsNull(branch, schemas));

                wrapped++;

                if (accepting > 1)
                {
                    offenders.Add($"{schema.Name}.{property.Name}");
                }
            }
        }

        // A scan that looked at nothing would pass for the wrong reason.
        wrapped.ShouldBeGreaterThan(30);
        offenders.ShouldBeEmpty("a null value matches more than one oneOf branch of: " + string.Join(", ", offenders));
    }

    [Fact]
    public async Task No_enum_component_lists_null_among_its_values()
    {
        var document = await FetchDocumentAsync();
        var carrying = new List<string>();
        var enums = 0;

        foreach (var schema in document.GetProperty("components").GetProperty("schemas").EnumerateObject())
        {
            if (!schema.Value.TryGetProperty("enum", out var values))
            {
                continue;
            }

            enums++;

            if (values.EnumerateArray().Any(static value => value.ValueKind == JsonValueKind.Null))
            {
                carrying.Add(schema.Name);
            }
        }

        enums.ShouldBeGreaterThan(40);
        carrying.ShouldBeEmpty("these enum components list null, so the property that wraps them is nullable twice: " + string.Join(", ", carrying));
    }

    [Fact]
    public async Task Every_nullable_json_element_member_is_written_as_any_of_and_the_free_form_request_members_are_optional()
    {
        var document = await FetchDocumentAsync();
        var schemas = document.GetProperty("components").GetProperty("schemas");

        foreach (var (schema, member) in new[]
                 {
                     ("EvalSuiteSaveRequest", "checks"),
                     ("JobScheduleSaveRequest", "payload"),
                     ("JobTriggerRequest", "payload"),
                 })
        {
            var property = schemas.GetProperty(schema).GetProperty("properties").GetProperty(member);

            property.TryGetProperty("oneOf", out _).ShouldBeFalse($"{schema}.{member} still uses oneOf");
            property.GetProperty("anyOf").GetArrayLength().ShouldBe(2, $"{schema}.{member}");
            RequiredNames(schemas.GetProperty(schema)).Contains(member, StringComparer.Ordinal).ShouldBeFalse($"{schema}.{member} must be optional");
        }

        foreach (var (schema, member) in new[]
                 {
                     ("ModelBinding", "providerSettings"),
                     ("AgentDefinitionRequest", "metadata"),
                     ("AgentSkillRequest", "metadata"),
                 })
        {
            var property = schemas.GetProperty(schema).GetProperty("properties").GetProperty(member);

            AcceptsNull(property, schemas).ShouldBeTrue($"{schema}.{member} must accept null");
            RequiredNames(schemas.GetProperty(schema)).Contains(member, StringComparer.Ordinal).ShouldBeFalse($"{schema}.{member} must be optional");
        }
    }

    [Fact]
    public async Task Every_string_keyed_json_element_dictionary_declares_what_its_values_are()
    {
        var document = await FetchDocumentAsync();
        var schemas = document.GetProperty("components").GetProperty("schemas");
        var types = ServerTypes();
        var examined = 0;
        var bare = new List<string>();

        foreach (var schema in schemas.EnumerateObject())
        {
            if (!types.TryGetValue(schema.Name, out var type) || !schema.Value.TryGetProperty("properties", out var properties))
            {
                continue;
            }

            foreach (var member in type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(static member => PublishedDocumentCorrections.IsJsonElementDictionary(member.PropertyType)))
            {
                var name = member.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? JsonNamingPolicy.CamelCase.ConvertName(member.Name);

                if (!properties.TryGetProperty(name, out var property))
                {
                    continue;
                }

                examined++;

                if (!property.TryGetProperty("additionalProperties", out _))
                {
                    bare.Add($"{schema.Name}.{name}");
                }
            }
        }

        // The generator turns a bare `type: object` into Record<string, never>, which no key may carry a value.
        examined.ShouldBeGreaterThanOrEqualTo(5);
        bare.ShouldBeEmpty("a dictionary with no value schema is Record<string, never> in TypeScript: " + string.Join(", ", bare));
    }

    [Fact]
    public async Task The_attachment_download_and_the_oauth_callback_list_what_they_answer()
    {
        var document = await FetchDocumentAsync();

        var download = OpenApiTestHelpers.FindByOperationId(document, "TraconDownloadAttachment").GetProperty("responses").GetProperty("200").GetProperty("content");
        var callback = OpenApiTestHelpers.FindByOperationId(document, "TraconMcpOAuthCallback").GetProperty("responses").GetProperty("200").GetProperty("content");

        // The stored media type is the file's own, so the document lists any type next to the octet stream.
        download.TryGetProperty("*/*", out _).ShouldBeTrue("the download lists no wildcard media type");
        download.TryGetProperty("application/octet-stream", out _).ShouldBeTrue();

        // The callback answers a page, which the document used to call "no content".
        callback.TryGetProperty("text/html", out _).ShouldBeTrue("the callback lists no HTML answer");
    }

    [Fact]
    public async Task The_a2a_operations_list_their_json_answer()
    {
        await using var host = await OpenApiSnapshotTests.StartFullSurfaceHostAsync(
            configureMoreTracon: static builder => builder
                .AddAgent(TestData.Definition("probe"))
                .UseA2A(static options => options.ExposedAgents.Add("probe")),
            configureAfterMap: static app => app.MapTraconA2A());

        using var response = await host.Client.GetAsync(new Uri("/openapi/v1.json", UriKind.Relative), TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();

        var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).RootElement;

        foreach (var operationId in new[] { "TraconA2A_probe", "TraconA2AAgentCard_probe" })
        {
            var content = OpenApiTestHelpers.FindByOperationId(document, operationId).GetProperty("responses").GetProperty("200").GetProperty("content");

            content.TryGetProperty("application/json", out _).ShouldBeTrue($"{operationId} lists no JSON answer");
        }
    }

    [Theory]
    [MemberData(nameof(OperationsThatAnswerNotImplemented))]
    public async Task An_operation_that_answers_501_for_a_missing_capability_lists_it(string operationId)
    {
        var document = await FetchDocumentAsync();
        var responses = OpenApiTestHelpers.FindByOperationId(document, operationId).GetProperty("responses");

        responses.TryGetProperty("501", out var notImplemented).ShouldBeTrue($"{operationId} does not list 501");
        notImplemented.GetProperty("content").TryGetProperty("application/problem+json", out _).ShouldBeTrue();
    }

    private static IEnumerable<string> RequiredNames(JsonElement schema)
        => schema.TryGetProperty("required", out var required)
            ? required.EnumerateArray().Select(static name => name.GetString()!)
            : [];

    private static bool AcceptsNull(JsonElement branch, JsonElement schemas)
    {
        if (branch.TryGetProperty("$ref", out var reference))
        {
            var name = reference.GetString()![(reference.GetString()!.LastIndexOf('/') + 1)..];

            return AcceptsNull(schemas.GetProperty(name), schemas);
        }

        if (branch.TryGetProperty("type", out var type))
        {
            return type.ValueKind == JsonValueKind.Array
                ? type.EnumerateArray().Any(static item => string.Equals(item.GetString(), "null", StringComparison.Ordinal))
                : string.Equals(type.GetString(), "null", StringComparison.Ordinal);
        }

        if (branch.TryGetProperty("enum", out var values))
        {
            return values.EnumerateArray().Any(static value => value.ValueKind == JsonValueKind.Null);
        }

        // `{}` and a schema that only describes itself accept anything, null included.
        return !branch.EnumerateObject().Any(static entry => entry.Name is not ("description" or "title"));
    }

    private static Dictionary<string, Type> ServerTypes()
        => new[]
            {
                typeof(TraconEndpointOptions).Assembly,
                typeof(ModelBinding).Assembly,
                typeof(TraconOptions).Assembly,
            }
            .SelectMany(static assembly => assembly.GetTypes())
            .GroupBy(static type => type.Name, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.Ordinal);

    private static async Task<JsonElement> FetchDocumentAsync()
    {
        await using var host = await OpenApiSnapshotTests.StartFullSurfaceHostAsync();

        using var response = await host.Client.GetAsync(new Uri("/openapi/v1.json", UriKind.Relative));
        response.EnsureSuccessStatusCode();

        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    }
}
