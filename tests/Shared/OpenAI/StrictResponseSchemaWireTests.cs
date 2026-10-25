using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using Tracon.ProviderCore.Tests;

namespace Tracon.OpenAIShared.Tests;

/// <summary>The OpenAI API surface a wire test drives.</summary>
public enum WireSurface
{
    /// <summary>The Chat Completions API.</summary>
    ChatCompletions,

    /// <summary>The Responses API.</summary>
    Responses,
}

/// <summary>
/// What the strict setting and the schema rewrite put in the request body, read off a real request over a
/// loopback socket (Phase 245).
/// </summary>
/// <remarks>
/// <para>
/// Linked into <c>Tracon.OpenAI.UnitTests</c> and <c>Tracon.Azure.UnitTests</c>; each project supplies the
/// <see cref="StrictWireHarness"/> that builds its package's client through the factory a registered provider
/// uses, so the decorator is the one the product runs.
/// </para>
/// <para>
/// 🚨 This is the drift gate for <see cref="OpenAIResponseSchema"/>: the effective schema it computes has to
/// equal the schema in the request body the real Microsoft.Extensions.AI client writes, strict or not. When
/// an upgrade changes the library's rewrite of anything the probe schemas contain (the 22 moved keywords, the four
/// options, and the keywords it keeps), a test below turns red. A keyword that no probe contains is not covered.
/// </para>
/// </remarks>
public sealed class StrictResponseSchemaWireTests
{
    private const string AuthorSchema = """
        {
          "type": "object",
          "properties": {
            "name": { "type": "string", "maxLength": 20, "description": "Full name" },
            "nickname": { "type": "string" },
            "age": { "type": ["integer", "null"], "minimum": 0 },
            "role": { "type": "string", "default": "user" },
            "address": {
              "type": "object",
              "properties": { "city": { "type": "string" }, "zip": { "type": "string", "pattern": "^[0-9]{5}$" } },
              "required": ["city"]
            },
            "answer": { "oneOf": [ { "type": "string" }, { "type": "integer", "minimum": 1 } ] }
          },
          "required": ["name"]
        }
        """;

    // Every keyword the library moves into the description, on one schema node, plus a default and a boolean
    // schema: a keyword that the copy forgets (or the library adds) shows up as a difference on the wire.
    private const string EveryKeywordSchema = """
        {
          "type": "object",
          "properties": {
            "everything": {
              "type": "string",
              "contentEncoding": "base64", "contentMediaType": "text/plain", "not": { "const": "x" },
              "minLength": 1, "maxLength": 9, "pattern": "^a", "format": "email",
              "minimum": 0, "maximum": 9, "multipleOf": 1, "patternProperties": { "^x": {} },
              "minItems": 1, "maxItems": 2, "unevaluatedProperties": false, "propertyNames": { "minLength": 1 },
              "minProperties": 1, "maxProperties": 2, "unevaluatedItems": false, "contains": { "type": "string" },
              "minContains": 1, "maxContains": 2, "uniqueItems": true, "default": "a"
            },
            "anything": true
          }
        }
        """;

    // The keywords the library keeps in the schema today. A library that starts to move one of them (the list of
    // 22 grows) makes the wire differ from the copy, and the test below goes red. Each is a keyword with a
    // plausible reason to be moved next: a bound, a tuple, a dependency.
    private const string KeptKeywordSchema = """
        {
          "type": "object",
          "properties": {
            "kept": {
              "type": "number", "title": "T", "examples": [1], "exclusiveMinimum": 0, "exclusiveMaximum": 9
            },
            "choice": { "enum": ["a", "b"] },
            "constant": { "const": "x", "type": "string" },
            "alternatives": { "anyOf": [ { "type": "string" }, { "type": "integer", "exclusiveMinimum": 1 } ] },
            "tuple": { "type": "array", "prefixItems": [ { "type": "string" } ], "items": { "type": "string" } },
            "linked": { "$ref": "#/$defs/Linked" },
            "map": { "type": "object", "additionalProperties": { "type": "string" } }
          },
          "dependentRequired": { "kept": ["choice"] },
          "required": ["kept"],
          "$defs": { "Linked": { "type": "string", "readOnly": true } }
        }
        """;

    private static readonly string[] MovedKeywords =
    [
        "contentEncoding", "contentMediaType", "not", "minLength", "maxLength", "pattern", "format", "minimum",
        "maximum", "multipleOf", "patternProperties", "minItems", "maxItems", "unevaluatedProperties",
        "propertyNames", "minProperties", "maxProperties", "unevaluatedItems", "contains", "minContains",
        "maxContains", "uniqueItems",
    ];

    private const string ChatCompletion = """
        {"id":"chatcmpl-1","object":"chat.completion","created":1,"model":"m",
         "choices":[{"index":0,"message":{"role":"assistant","content":"{}"},"finish_reason":"stop"}],
         "usage":{"prompt_tokens":3,"completion_tokens":1,"total_tokens":4}}
        """;

    private const string ResponsesReply = """
        {"id":"resp_1","object":"response","created_at":1,"status":"completed","model":"m",
         "output":[{"type":"message","id":"msg_1","status":"completed","role":"assistant",
                    "content":[{"type":"output_text","text":"{}","annotations":[]}]}],
         "usage":{"input_tokens":3,"output_tokens":1,"total_tokens":4},
         "parallel_tool_calls":true,"tools":[],"tool_choice":"auto"}
        """;

    public static TheoryData<WireSurface, bool> SurfacesAndModes()
    {
        var data = new TheoryData<WireSurface, bool>();

        foreach (var surface in StrictWireHarness.Surfaces)
        {
            data.Add(surface, true);
            data.Add(surface, false);
        }

        return data;
    }

    /// <param name="surface">The API surface.</param>
    /// <param name="strict">Whether the strict setting is on.</param>
    [Theory]
    [MemberData(nameof(SurfacesAndModes))]
    public async Task The_schema_on_the_wire_is_the_schema_the_copy_computes(WireSurface surface, bool strict)
    {
        var (format, _) = await SendAsync(surface, strict ? true : null, withTool: false);

        var expected = OpenAIResponseSchema.Describe(Schema(), strict).EffectiveSchema;

        // Byte for byte: both sides are the compact serialization of the same transformed document.
        format["schema"]!.ToJsonString().ShouldBe(expected.GetRawText());
    }

    /// <param name="surface">The API surface.</param>
    [Theory]
    [MemberData(nameof(Surfaces))]
    public async Task Every_keyword_the_library_moves_is_moved_by_the_copy_too(WireSurface surface)
    {
        var (format, _) = await SendAsync(surface, strictSetting: null, withTool: false, schemaText: EveryKeywordSchema);

        using var document = JsonDocument.Parse(EveryKeywordSchema);
        var expected = OpenAIResponseSchema.Describe(document.RootElement, strict: false).EffectiveSchema;

        format["schema"]!.ToJsonString().ShouldBe(expected.GetRawText());

        // The probe is only worth something while it exercises all 22 keywords.
        var everything = format["schema"]!["properties"]!["everything"]!.AsObject();
        var description = everything["description"]!.GetValue<string>();

        everything.Select(static pair => pair.Key).ShouldBe(["type", "description"], ignoreOrder: true);

        foreach (var keyword in MovedKeywords)
        {
            description.ShouldContain($"{keyword}: ", Case.Sensitive);
        }

        description.ShouldContain("Default value: \"a\"", Case.Sensitive);
    }

    /// <param name="surface">The API surface.</param>
    [Theory]
    [MemberData(nameof(Surfaces))]
    public async Task Keywords_the_library_keeps_stay_in_the_copy_as_well(WireSurface surface)
    {
        var (format, _) = await SendAsync(surface, strictSetting: null, withTool: false, schemaText: KeptKeywordSchema);

        using var document = JsonDocument.Parse(KeptKeywordSchema);
        var expected = OpenAIResponseSchema.Describe(document.RootElement, strict: false).EffectiveSchema;

        format["schema"]!.ToJsonString().ShouldBe(expected.GetRawText());

        // The probe is only worth something while the library keeps them: none of these may have moved.
        var properties = format["schema"]!["properties"]!;
        properties["kept"]!["exclusiveMinimum"].ShouldNotBeNull();
        properties["choice"]!["enum"].ShouldNotBeNull();
        properties["tuple"]!["prefixItems"].ShouldNotBeNull();
        properties["map"]!["additionalProperties"]!["type"].ShouldNotBeNull();
        format["schema"]!["dependentRequired"].ShouldNotBeNull();
        format["schema"]!["$defs"].ShouldNotBeNull();
    }

    /// <param name="surface">The API surface.</param>
    [Theory]
    [MemberData(nameof(Surfaces))]
    public async Task The_strict_setting_puts_strict_true_on_the_response_format_and_false_on_every_tool(WireSurface surface)
    {
        var (format, tool) = await SendAsync(surface, strictSetting: true, withTool: true);

        format["strict"]!.GetValue<bool>().ShouldBeTrue();
        format["name"]!.GetValue<string>().ShouldBe("answer");
        tool!["strict"]!.GetValue<bool>().ShouldBeFalse("a tool is not made strict by the response schema's setting");
    }

    /// <param name="surface">The API surface.</param>
    [Theory]
    [MemberData(nameof(Surfaces))]
    public async Task Without_the_setting_the_request_carries_no_strict_flag_at_all(WireSurface surface)
    {
        var (format, tool) = await SendAsync(surface, strictSetting: null, withTool: true);

        // Absent and JSON null both leave the flag unset for the service.
        format["strict"].ShouldBeNull();
        tool!["strict"].ShouldBeNull();
    }

    /// <param name="surface">The API surface.</param>
    [Theory]
    [MemberData(nameof(Surfaces))]
    public async Task The_setting_turned_off_is_the_same_as_no_setting(WireSurface surface)
    {
        var (format, tool) = await SendAsync(surface, strictSetting: false, withTool: true);

        format["strict"].ShouldBeNull();
        tool!["strict"].ShouldBeNull();
    }

    /// <param name="surface">The API surface.</param>
    [Theory]
    [MemberData(nameof(Surfaces))]
    public async Task A_tool_parameter_schema_is_not_rewritten_when_the_response_format_is_strict(WireSurface surface)
    {
        var (_, strictTool) = await SendAsync(surface, strictSetting: true, withTool: true);
        var (_, plainTool) = await SendAsync(surface, strictSetting: null, withTool: true);

        Parameters(strictTool!).ToJsonString().ShouldBe(Parameters(plainTool!).ToJsonString());
    }

    /// <param name="surface">The API surface.</param>
    [Theory]
    [MemberData(nameof(Surfaces))]
    public async Task A_json_object_format_with_the_setting_on_carries_no_strict_flag(WireSurface surface)
    {
        await using var server = StubHttpServer.Respond(200, "OK", Reply(surface));
        using var client = StrictWireHarness.CreateClient(server.BaseAddress, surface, strictSetting: true);

        await client.GetResponseAsync(
            "ping",
            new ChatOptions { ResponseFormat = ChatResponseFormat.Json },
            TestContext.Current.CancellationToken);

        var body = JsonNode.Parse(await server.RequestBody)!;

        // A format without a schema has no strict mode to ask for.
        (body.ToJsonString().Contains("\"strict\"", StringComparison.Ordinal)).ShouldBeFalse();
    }

    public static TheoryData<WireSurface> Surfaces()
    {
        var data = new TheoryData<WireSurface>();

        foreach (var surface in StrictWireHarness.Surfaces)
        {
            data.Add(surface);
        }

        return data;
    }

    private static JsonElement Schema(string text = AuthorSchema)
    {
        using var document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }

    private static string Reply(WireSurface surface) => surface == WireSurface.Responses ? ResponsesReply : ChatCompletion;

    private static JsonNode Parameters(JsonObject tool)
        => (tool["parameters"] ?? tool["function"]?["parameters"])!;

    /// <summary>Sends one structured request and returns the response format and the tool as they went out.</summary>
    private static async Task<(JsonObject Format, JsonObject? Tool)> SendAsync(
        WireSurface surface,
        bool? strictSetting,
        bool withTool,
        string schemaText = AuthorSchema)
    {
        await using var server = StubHttpServer.Respond(200, "OK", Reply(surface));
        using var client = StrictWireHarness.CreateClient(server.BaseAddress, surface, strictSetting);

        var options = new ChatOptions
        {
            ResponseFormat = ChatResponseFormat.ForJsonSchema(Schema(schemaText), "answer", "The answer."),
        };

        if (withTool)
        {
            options.Tools = [AIFunctionFactory.Create(static (string city) => city, "lookup", "Looks a city up.")];
        }

        await client.GetResponseAsync("ping", options, TestContext.Current.CancellationToken);

        var body = JsonNode.Parse(await server.RequestBody)!.AsObject();

        var format = surface == WireSurface.Responses
            ? body["text"]!["format"]!.AsObject()
            : body["response_format"]!["json_schema"]!.AsObject();

        var tools = body["tools"] as JsonArray;
        var wireTool = tools is { Count: > 0 }
            ? surface == WireSurface.Responses ? tools[0]!.AsObject() : tools[0]!["function"]!.AsObject()
            : null;

        return (format, wireTool);
    }
}
