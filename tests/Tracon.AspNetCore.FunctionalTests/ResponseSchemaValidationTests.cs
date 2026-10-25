using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// Phase 245 through the real host and the real OpenAI and Azure OpenAI providers: <c>POST /api/agents/validate</c>
/// reports the response schema each provider's client sends, with the strict setting, and warns about what differs from
/// the schema the author wrote.
/// </summary>
public sealed class ResponseSchemaValidationTests
{
    private const string TestKey = "test-key-response-schema";

    private static readonly Uri Validate = new("/tracon/api/agents/validate", UriKind.Relative);

    private static readonly object Schema = new
    {
        type = "object",
        properties = new
        {
            name = new { type = "string", maxLength = 20 },
            nickname = new { type = "string" },
            age = new { type = new[] { "integer", "null" } },
        },
        required = new[] { "name" },
    };

    [Fact]
    public async Task The_report_carries_the_effective_schema_with_the_strict_setting_and_both_warnings()
    {
        await using var host = await StartAsync();

        using var response = await host.Client.PostAsJsonAsync(Validate, Request(new
        {
            provider = OpenAIProviderNames.ChatCompletions,
            model = "gpt-test",
            responseFormat = new { kind = "JsonSchema", schema = Schema, schemaName = "person" },
            providerSettings = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                [OpenAIProviderNames.StrictJsonSchemaSetting] = true,
            },
        }));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await TraconTestHost.ReadJsonAsync(response);
        body.GetProperty("valid").GetBoolean().ShouldBeTrue("a warning never stops the definition");

        var entry = body.GetProperty("responseSchemas").EnumerateArray().ShouldHaveSingleItem();
        entry.GetProperty("provider").GetString().ShouldBe("openai");
        entry.GetProperty("model").GetString().ShouldBe("gpt-test");
        entry.GetProperty("strict").GetBoolean().ShouldBeTrue();

        var schema = entry.GetProperty("schema");
        schema.GetProperty("additionalProperties").GetBoolean().ShouldBeFalse();
        schema.GetProperty("required").EnumerateArray().Select(static item => item.GetString()).ShouldBe(["name", "nickname", "age"]);
        schema.GetProperty("properties").GetProperty("name").TryGetProperty("maxLength", out _).ShouldBeFalse();

        var messages = body.GetProperty("messages").EnumerateArray().ToList();

        messages.Select(static message => message.GetProperty("code").GetString()).ShouldBe(
            ["response_schema_optional_required", "response_schema_keyword_not_enforced"]);
        messages.ShouldAllBe(static message => string.Equals(message.GetProperty("severity").GetString(), "Warning", StringComparison.Ordinal));

        var optional = messages[0].GetProperty("message").GetString()!;
        optional.ShouldContain("#/properties/nickname");
        optional.ShouldNotContain("#/properties/age", Case.Sensitive);
        optional.ShouldNotContain("#/properties/name", Case.Sensitive);

        messages[1].GetProperty("message").GetString().ShouldNotBeNull().ShouldContain("#/properties/name: maxLength");
    }

    [Fact]
    public async Task Without_the_setting_the_entry_is_not_strict_and_the_schema_is_still_rewritten()
    {
        await using var host = await StartAsync();

        using var response = await host.Client.PostAsJsonAsync(Validate, Request(new
        {
            provider = OpenAIProviderNames.Responses,
            model = "gpt-test",
            responseFormat = new { kind = "JsonSchema", schema = Schema },
        }));

        var body = await TraconTestHost.ReadJsonAsync(response);
        var entry = body.GetProperty("responseSchemas").EnumerateArray().ShouldHaveSingleItem();

        entry.GetProperty("provider").GetString().ShouldBe("openai-responses");
        entry.GetProperty("strict").GetBoolean().ShouldBeFalse();
        entry.GetProperty("schema").GetProperty("additionalProperties").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task A_fallback_link_gets_its_own_entry_that_is_not_strict_whatever_the_primary_says()
    {
        await using var host = await StartAsync();

        using var response = await host.Client.PostAsJsonAsync(Validate, Request(new
        {
            provider = OpenAIProviderNames.ChatCompletions,
            model = "gpt-test",
            responseFormat = new { kind = "JsonSchema", schema = Schema },
            providerSettings = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                [OpenAIProviderNames.StrictJsonSchemaSetting] = true,
            },
            fallbacks = new[] { new { provider = AzureOpenAIProviderNames.AzureOpenAI, model = "production-gpt" } },
        }));

        var body = await TraconTestHost.ReadJsonAsync(response);
        var entries = body.GetProperty("responseSchemas").EnumerateArray().ToList();

        entries.Select(static entry => (entry.GetProperty("provider").GetString(), entry.GetProperty("strict").GetBoolean()))
            .ShouldBe([("openai", true), ("azure-openai", false)]);

        // One warning pair per provider, each at the path of its binding.
        body.GetProperty("messages").EnumerateArray()
            .Where(static message => string.Equals(message.GetProperty("code").GetString(), "response_schema_optional_required", StringComparison.Ordinal))
            .Select(static message => message.GetProperty("path").GetString())
            .ShouldBe(["model.responseFormat.schema", "model.fallbacks[0]"]);
    }

    private static readonly object OneOfSchema = new
    {
        type = "object",
        properties = new
        {
            answer = new { oneOf = new object[] { new { type = "string" }, new { type = "integer" } } },
        },
        required = new[] { "answer" },
    };

    [Theory]
    [InlineData(OpenAIProviderNames.ChatCompletions)]
    [InlineData(OpenAIProviderNames.Responses)]
    public async Task A_part_that_OpenAI_refuses_in_strict_mode_is_a_warning_and_the_definition_can_still_be_saved(string provider)
    {
        await using var host = await StartAsync();
        var request = Request(new
        {
            provider,
            model = "gpt-test",
            responseFormat = new { kind = "JsonSchema", schema = OneOfSchema },
            providerSettings = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                [OpenAIProviderNames.StrictJsonSchemaSetting] = true,
            },
        });

        using var validation = await host.Client.PostAsJsonAsync(Validate, request);
        var body = await TraconTestHost.ReadJsonAsync(validation);

        body.GetProperty("valid").GetBoolean().ShouldBeTrue("a warning never stops the definition");

        var warning = body.GetProperty("messages").EnumerateArray().ShouldHaveSingleItem();
        warning.GetProperty("severity").GetString().ShouldBe("Warning");
        warning.GetProperty("code").GetString().ShouldBe("response_schema_strict_unsupported");
        warning.GetProperty("path").GetString().ShouldBe("model.responseFormat.schema");
        warning.GetProperty("message").GetString().ShouldNotBeNull().ShouldContain("#/properties/answer: oneOf");

        // The report warns; saving is not blocked, because the answer is tenant-independent.
        using var save = await host.Client.PostAsJsonAsync(new Uri("/tracon/api/agents", UriKind.Relative), request);

        save.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Without_the_strict_setting_the_same_schema_gets_no_such_warning()
    {
        await using var host = await StartAsync();

        using var response = await host.Client.PostAsJsonAsync(Validate, Request(new
        {
            provider = OpenAIProviderNames.ChatCompletions,
            model = "gpt-test",
            responseFormat = new { kind = "JsonSchema", schema = OneOfSchema },
        }));

        var body = await TraconTestHost.ReadJsonAsync(response);

        body.GetProperty("messages").EnumerateArray()
            .Select(static message => message.GetProperty("code").GetString())
            .Contains("response_schema_strict_unsupported", StringComparer.Ordinal)
            .ShouldBeFalse();
    }

    [Fact]
    public async Task A_compatible_server_and_Azure_OpenAI_get_no_refusal_warning_because_none_was_measured_there()
    {
        await using var host = await TraconTestHost.StartAsync(configureTracon: static builder => builder
            .UseOpenAICompatible("local", static options =>
            {
                options.Endpoint = new Uri("http://127.0.0.1:9/v1");
                options.Models.Add(new ModelDescriptor { Name = "local-model", SupportsStructuredOutput = true });
            })
            .UseAzureOpenAI(new Uri("https://test-resource.openai.azure.com/"), TestKey, static options =>
            {
                options.DefaultDeployment = "production-gpt";
                options.Models.Add(new ModelDescriptor { Name = "production-gpt", SupportsStructuredOutput = true });
            }));

        foreach (var (provider, model, key) in new[]
        {
            ("local", "local-model", OpenAIProviderNames.StrictJsonSchemaSetting),
            (AzureOpenAIProviderNames.AzureOpenAI, "production-gpt", AzureOpenAIProviderNames.StrictJsonSchemaSetting),
        })
        {
            using var response = await host.Client.PostAsJsonAsync(Validate, Request(new
            {
                provider,
                model,
                responseFormat = new { kind = "JsonSchema", schema = OneOfSchema },
                providerSettings = new Dictionary<string, object>(StringComparer.Ordinal) { [key] = true },
            }));

            var body = await TraconTestHost.ReadJsonAsync(response);

            body.GetProperty("valid").GetBoolean().ShouldBeTrue(provider);
            body.GetProperty("responseSchemas").EnumerateArray().ShouldHaveSingleItem().GetProperty("strict").GetBoolean().ShouldBeTrue();
            body.GetProperty("messages").EnumerateArray()
                .Select(static message => message.GetProperty("code").GetString())
                .Contains("response_schema_strict_unsupported", StringComparer.Ordinal)
                .ShouldBeFalse(provider);
        }
    }

    [Fact]
    public async Task A_schema_the_client_cannot_rewrite_does_not_turn_the_report_into_a_server_error()
    {
        await using var host = await StartAsync();

        // The client reads a description as text when it moves the default into it, and throws for a number.
        using var response = await host.Client.PostAsJsonAsync(Validate, Request(new
        {
            provider = OpenAIProviderNames.ChatCompletions,
            model = "gpt-test",
            responseFormat = new
            {
                kind = "JsonSchema",
                schema = new
                {
                    type = "object",
                    properties = new { x = new { type = "string", @default = "a", description = 5 } },
                },
            },
        }));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await TraconTestHost.ReadJsonAsync(response);

        body.GetProperty("responseSchemas").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task A_strict_value_that_is_not_a_boolean_is_an_invalid_setting_that_names_the_key()
    {
        await using var host = await StartAsync();

        using var response = await host.Client.PostAsJsonAsync(Validate, Request(new
        {
            provider = OpenAIProviderNames.ChatCompletions,
            model = "gpt-test",
            responseFormat = new { kind = "JsonSchema", schema = Schema },
            providerSettings = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                [OpenAIProviderNames.StrictJsonSchemaSetting] = "sometimes",
            },
        }));

        var body = await TraconTestHost.ReadJsonAsync(response);
        body.GetProperty("valid").GetBoolean().ShouldBeFalse();
        body.GetProperty("responseSchemas").GetArrayLength().ShouldBe(0);

        var error = body.GetProperty("messages").EnumerateArray().ShouldHaveSingleItem();
        error.GetProperty("code").GetString().ShouldBe("invalid_setting");
        error.GetProperty("message").GetString().ShouldNotBeNull().ShouldContain(OpenAIProviderNames.StrictJsonSchemaSetting);
        error.GetProperty("message").GetString().ShouldNotBeNull().ShouldNotContain("sometimes", Case.Sensitive);
    }

    [Fact]
    public async Task A_definition_without_a_schema_returns_an_empty_list_not_a_missing_field()
    {
        await using var host = await StartAsync();

        using var response = await host.Client.PostAsJsonAsync(Validate, Request(new
        {
            provider = OpenAIProviderNames.ChatCompletions,
            model = "gpt-test",
        }));

        var body = await TraconTestHost.ReadJsonAsync(response);

        body.GetProperty("responseSchemas").ValueKind.ShouldBe(JsonValueKind.Array);
        body.GetProperty("responseSchemas").GetArrayLength().ShouldBe(0);
        body.GetProperty("messages").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task A_provider_that_does_not_state_its_schema_adds_nothing()
    {
        await using var host = await TraconTestHost.StartAsync(configureTracon: static builder => builder
            .UseAnthropic(TestKey, static options => options.Models.Add(new ModelDescriptor
            {
                Name = "claude-test",
                MaxOutputTokens = 4096,
                SupportsStructuredOutput = true,
            })));

        using var response = await host.Client.PostAsJsonAsync(Validate, Request(new
        {
            provider = AnthropicProviderNames.Anthropic,
            model = "claude-test",
            responseFormat = new { kind = "JsonSchema", schema = Schema },
        }));

        var body = await TraconTestHost.ReadJsonAsync(response);

        body.GetProperty("responseSchemas").GetArrayLength().ShouldBe(0);
        body.GetProperty("messages").GetArrayLength().ShouldBe(0);
    }

    private static object Request(object model) => new
    {
        name = "response-schema-agent",
        instructions = "You answer with a person.",
        model,
    };

    private static Task<TraconTestHost> StartAsync()
        => TraconTestHost.StartAsync(configureTracon: static builder => builder
            .UseOpenAI(TestKey, static options => options.Models.Add(new ModelDescriptor
            {
                Name = "gpt-test",
                SupportsStructuredOutput = true,
            }))
            .UseAzureOpenAI(new Uri("https://test-resource.openai.azure.com/"), TestKey, static options =>
            {
                options.DefaultDeployment = "production-gpt";
                options.Models.Add(new ModelDescriptor { Name = "production-gpt", SupportsStructuredOutput = true });
            }));
}
