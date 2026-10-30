using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// End to end, through the real host and the real Anthropic provider:
/// <c>POST /api/agents/validate</c> warns about the Anthropic default output
/// limit, a catalog model that rejects sampling cannot be saved with
/// <c>temperature</c>, and <c>GET /api/models</c> carries the new flag.
/// </summary>
public sealed class AgentValidationWarningTests
{
    private const string TestKey = "test-anthropic-validation-warning";

    private static readonly Uri Validate = new("/tracon/api/agents/validate", UriKind.Relative);

    [Fact]
    public async Task A_structured_agent_on_the_Anthropic_default_limit_gets_a_warning_that_names_the_limit()
    {
        await using var host = await StartAsync();

        using var response = await host.Client.PostAsJsonAsync(Validate, Request(new
        {
            provider = AnthropicProviderNames.Anthropic,
            model = "claude-long",
            responseFormat = new { kind = "JsonSchema", schema = new { type = "object" } },
        }));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await TraconTestHost.ReadJsonAsync(response);
        body.GetProperty("valid").GetBoolean().ShouldBeTrue("a warning never stops the definition");

        var warning = body.GetProperty("messages").EnumerateArray().ShouldHaveSingleItem();
        warning.GetProperty("severity").GetString().ShouldBe("Warning");
        warning.GetProperty("code").GetString().ShouldBe("output_truncation_risk");
        warning.GetProperty("path").GetString().ShouldBe("model.maxOutputTokens");
        warning.GetProperty("message").GetString().ShouldNotBeNull().ShouldContain("4096");
    }

    [Fact]
    public async Task High_effort_without_a_limit_gets_the_same_warning_for_plain_text()
    {
        await using var host = await StartAsync();

        using var response = await host.Client.PostAsJsonAsync(Validate, Request(new
        {
            provider = AnthropicProviderNames.Anthropic,
            model = "claude-long",
            providerSettings = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                [AnthropicProviderNames.EffortSetting] = "high",
            },
        }));

        var body = await TraconTestHost.ReadJsonAsync(response);

        body.GetProperty("messages").EnumerateArray().ShouldHaveSingleItem()
            .GetProperty("code").GetString().ShouldBe("output_truncation_risk");
    }

    [Fact]
    public async Task An_explicit_limit_removes_the_warning()
    {
        await using var host = await StartAsync();

        using var response = await host.Client.PostAsJsonAsync(Validate, Request(new
        {
            provider = AnthropicProviderNames.Anthropic,
            model = "claude-long",
            maxOutputTokens = 64000,
            responseFormat = new { kind = "JsonSchema", schema = new { type = "object" } },
        }));

        var body = await TraconTestHost.ReadJsonAsync(response);

        body.GetProperty("valid").GetBoolean().ShouldBeTrue();
        body.GetProperty("messages").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task A_wrong_effort_value_is_an_invalid_setting_that_names_the_key()
    {
        await using var host = await StartAsync();

        using var response = await host.Client.PostAsJsonAsync(Validate, Request(new
        {
            provider = AnthropicProviderNames.Anthropic,
            model = "claude-long",
            providerSettings = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                [AnthropicProviderNames.EffortSetting] = "extreme",
            },
        }));

        var body = await TraconTestHost.ReadJsonAsync(response);
        body.GetProperty("valid").GetBoolean().ShouldBeFalse();

        var error = body.GetProperty("messages").EnumerateArray().ShouldHaveSingleItem();
        error.GetProperty("code").GetString().ShouldBe("invalid_setting");
        error.GetProperty("message").GetString().ShouldNotBeNull().ShouldContain(AnthropicProviderNames.EffortSetting);
    }

    [Fact]
    public async Task Temperature_on_a_model_that_rejects_sampling_is_reported_and_cannot_be_saved()
    {
        await using var host = await StartAsync();

        var model = new
        {
            provider = AnthropicProviderNames.Anthropic,
            model = "claude-no-sampling",
            maxOutputTokens = 1000,
            temperature = 0.2,
        };

        using var validation = await host.Client.PostAsJsonAsync(Validate, Request(model));
        var report = await TraconTestHost.ReadJsonAsync(validation);

        report.GetProperty("valid").GetBoolean().ShouldBeFalse();

        // The catalog says the model rejects sampling, and since Anthropic SDK 13 the provider does not
        // send the field either. Both findings are true and both name the path; the catalog one is the one this
        // test is about, so it is looked up by its code.
        var errors = report.GetProperty("messages").EnumerateArray().ToList();
        errors.Select(static message => message.GetProperty("code").GetString()).Order(StringComparer.Ordinal)
            .ShouldBe(["sampling_field_unsupported", "sampling_unsupported"]);
        errors.ShouldAllBe(static message => string.Equals(message.GetProperty("path").GetString(), "model.temperature", StringComparison.Ordinal));

        using var save = await host.Client.PutAsJsonAsync(
            new Uri("/tracon/api/agents/sampling-agent", UriKind.Relative),
            Request(model, name: "sampling-agent"));

        save.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await save.Content.ReadAsStringAsync()).ShouldContain("anthropic/claude-no-sampling");
    }

    [Fact]
    public async Task The_model_catalog_carries_supportsSampling()
    {
        await using var host = await StartAsync();

        var providers = await host.Client.GetFromJsonAsync<JsonElement>(new Uri("/tracon/api/models", UriKind.Relative));

        var models = providers.EnumerateArray()
            .Single(static provider => string.Equals(provider.GetProperty("name").GetString(), AnthropicProviderNames.Anthropic, StringComparison.Ordinal))
            .GetProperty("models")
            .EnumerateArray()
            .ToDictionary(static model => model.GetProperty("name").GetString()!, StringComparer.Ordinal);

        models["claude-no-sampling"].GetProperty("supportsSampling").GetBoolean().ShouldBeFalse();
        // Unknown stays unknown on the wire: never a guessed true or false.
        (models["claude-long"].TryGetProperty("supportsSampling", out var unknown) && unknown.ValueKind != JsonValueKind.Null)
            .ShouldBeFalse();
    }

    private static object Request(object model, string name = "validation-warning-agent") => new
    {
        name,
        instructions = "You write long scripts.",
        model,
    };

    private static Task<TraconTestHost> StartAsync()
        => TraconTestHost.StartAsync(configureTracon: static builder => builder
            .UseAnthropic(TestKey, static options =>
            {
                options.Models.Add(new ModelDescriptor
                {
                    Name = "claude-long",
                    MaxOutputTokens = 128_000,
                    SupportsStructuredOutput = true,
                });
                options.Models.Add(new ModelDescriptor
                {
                    Name = "claude-no-sampling",
                    MaxOutputTokens = 128_000,
                    SupportsSampling = false,
                });
            }));
}
