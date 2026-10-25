using System.Text.Json;
using Tracon.OpenAI.UnitTests.Infrastructure;

namespace Tracon.OpenAI.UnitTests;

/// <summary>
/// What the OpenAI provider answers to <see cref="IResponseSchemaSupport"/> (Phase 245): the strict flag from the
/// binding's setting, and the strict-mode refusals only for the service they were measured against.
/// </summary>
/// <remarks>
/// The refusals (<c>oneOf</c>, <c>allOf</c>, <c>if</c>, <c>then</c>, <c>dependentRequired</c>,
/// <c>dependentSchemas</c>, an object without <c>additionalProperties: false</c>) were measured against
/// <c>api.openai.com</c> on both API surfaces (2026-10-25). A gateway or a compatible server can accept them, so
/// a provider with another address reports none.
/// </remarks>
public sealed class OpenAIResponseSchemaSupportTests
{
    private static readonly JsonElement OneOf = JsonDocument.Parse(
        """{"type":"object","properties":{"answer":{"oneOf":[{"type":"string"},{"type":"integer"}]}},"required":["answer"]}""")
        .RootElement.Clone();

    [Theory]
    [InlineData(null)]
    [InlineData("https://api.openai.com/v1/")]
    [InlineData("https://API.OPENAI.COM/v1")]
    public void The_refusals_are_listed_for_OpenAI_itself(string? endpoint)
    {
        var provider = Provider(endpoint is null ? null : new Uri(endpoint));

        var shape = provider.DescribeResponseSchema(Strict(), OneOf);

        shape.Strict.ShouldBeTrue();
        shape.StrictUnsupported.ShouldBe(["#/properties/answer: oneOf"]);
    }

    [Fact]
    public void A_provider_built_without_options_is_the_default_OpenAI_service()
    {
        var provider = new OpenAIModelProvider(
            OpenAIProviderNames.ChatCompletions,
            OpenAIApiSurface.ChatCompletions,
            new OpenAIChatClientFactory(TestData.Options()),
            []);

        provider.DescribeResponseSchema(Strict(), OneOf).StrictUnsupported.ShouldNotBeEmpty();
    }

    [Theory]
    [InlineData("http://127.0.0.1:11434/v1")]
    [InlineData("https://openrouter.ai/api/v1")]
    [InlineData("https://my-gateway.example.com/openai/v1")]
    [InlineData("https://notapi.openai.com.example.net/v1")]
    public void A_server_at_another_address_reports_no_refusal_because_none_was_measured_there(string endpoint)
    {
        var provider = Provider(new Uri(endpoint));

        var shape = provider.DescribeResponseSchema(Strict(), OneOf);

        shape.Strict.ShouldBeTrue();
        shape.StrictUnsupported.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Both_API_surfaces_answer_the_same(bool responses)
    {
        var provider = Provider(endpoint: null, responses);

        provider.DescribeResponseSchema(Strict(), OneOf).StrictUnsupported.ShouldBe(["#/properties/answer: oneOf"]);
    }

    [Fact]
    public void Without_the_setting_the_shape_is_not_strict()
    {
        var shape = Provider(endpoint: null).DescribeResponseSchema(TestData.Binding(), OneOf);

        shape.Strict.ShouldBeFalse();
        shape.EffectiveSchema.GetProperty("additionalProperties").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public void A_setting_of_the_wrong_type_is_a_Tracon_exception_that_names_the_key()
    {
        var binding = TestData.Binding(providerSettings: TestData.Settings((OpenAIProviderNames.StrictJsonSchemaSetting, "sometimes")));

        var exception = Should.Throw<TraconException>(() => Provider(endpoint: null).DescribeResponseSchema(binding, OneOf));

        exception.Message.ShouldContain(OpenAIProviderNames.StrictJsonSchemaSetting);
        exception.Message.ShouldNotContain("sometimes");
    }

    private static ModelBinding Strict()
        => TestData.Binding(providerSettings: TestData.Settings((OpenAIProviderNames.StrictJsonSchemaSetting, true)));

    private static OpenAIModelProvider Provider(Uri? endpoint, bool responses = false)
    {
        var options = TestData.Options(o => o.Endpoint = endpoint);

        return new OpenAIModelProvider(
            responses ? OpenAIProviderNames.Responses : OpenAIProviderNames.ChatCompletions,
            responses ? OpenAIApiSurface.Responses : OpenAIApiSurface.ChatCompletions,
            new OpenAIChatClientFactory(options),
            [],
            healthCheckOptions: options);
    }
}
