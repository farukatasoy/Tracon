using System.Text.Json;
using Tracon.Azure.UnitTests.Infrastructure;

namespace Tracon.Azure.UnitTests;

/// <summary>
/// What the Azure OpenAI provider answers to <see cref="IResponseSchemaSupport"/>.
/// </summary>
/// <remarks>
/// The strict-mode refusals were measured against <c>api.openai.com</c> only, so the Azure provider reports none,
/// even for a schema that OpenAI refuses.
/// </remarks>
public sealed class AzureOpenAIResponseSchemaSupportTests
{
    private static readonly JsonElement OneOf = JsonDocument.Parse(
        """{"type":"object","properties":{"answer":{"oneOf":[{"type":"string"},{"type":"integer"}]}},"required":["answer"]}""")
        .RootElement.Clone();

    [Fact]
    public void The_strict_flag_comes_from_the_setting_and_no_refusal_is_listed()
    {
        var strict = Provider().DescribeResponseSchema(
            TestData.Binding(providerSettings: TestData.Settings((AzureOpenAIProviderNames.StrictJsonSchemaSetting, true))),
            OneOf);
        var plain = Provider().DescribeResponseSchema(TestData.Binding(), OneOf);

        strict.Strict.ShouldBeTrue();
        plain.Strict.ShouldBeFalse();
        strict.StrictUnsupported.ShouldBeEmpty("the Azure refusals are not measured");
        strict.EffectiveSchema.GetRawText().ShouldBe(plain.EffectiveSchema.GetRawText());
    }

    [Fact]
    public void A_setting_of_the_wrong_type_is_a_Tracon_exception_that_names_the_key()
    {
        var binding = TestData.Binding(providerSettings: TestData.Settings((AzureOpenAIProviderNames.StrictJsonSchemaSetting, "sometimes")));

        var exception = Should.Throw<TraconException>(() => Provider().DescribeResponseSchema(binding, OneOf));

        exception.Message.ShouldContain(AzureOpenAIProviderNames.StrictJsonSchemaSetting);
        exception.Message.ShouldNotContain("sometimes");
    }

    private static AzureOpenAIModelProvider Provider()
        => new(
            AzureOpenAIProviderNames.AzureOpenAI,
            new AzureOpenAIChatClientFactory(TestData.Options()),
            []);
}
