using System.Text.Json;
using AgentPrism.Core.UnitTests.Fakes;
using Microsoft.Extensions.AI;

namespace AgentPrism.Core.UnitTests.Compilation;

public sealed class ResponseFormatCapabilityTests
{
    [Fact]
    public void SupportsStructuredOutput_false_olan_modelde_derleme_reddedilir()
    {
        var provider = new FakeModelProvider(models:
        [
            new ModelDescriptor { Name = "fake-model", SupportsStructuredOutput = false },
        ]);
        var compiler = new AgentDefinitionCompiler(TestData.Providers(provider), TestData.Registry());

        var definition = TestData.Definition() with
        {
            Model = TestData.Binding() with
            {
                ResponseFormat = new AgentResponseFormat
                {
                    Kind = AgentResponseFormatKind.JsonSchema,
                    Schema = ParseSchema("""{"type":"object"}"""),
                },
            },
        };

        var exception = Should.Throw<AgentPrismCompilationException>(() => compiler.Compile(definition));

        exception.AgentName.ShouldBe("test-agent");
        exception.Message.ShouldContain("fake-model");
    }

    [Fact]
    public void SupportsStructuredOutput_true_olan_modelde_derleme_basarili_olur()
    {
        var provider = new FakeModelProvider(models:
        [
            new ModelDescriptor { Name = "fake-model", SupportsStructuredOutput = true },
        ]);
        var compiler = new AgentDefinitionCompiler(TestData.Providers(provider), TestData.Registry());

        var definition = TestData.Definition() with
        {
            Model = TestData.Binding() with
            {
                ResponseFormat = new AgentResponseFormat
                {
                    Kind = AgentResponseFormatKind.JsonSchema,
                    Schema = ParseSchema("""{"type":"object"}"""),
                },
            },
        };

        var agent = compiler.Compile(definition);

        agent.ShouldNotBeNull();
    }

    [Fact]
    public void Model_katalogda_yoksa_denetim_atlanir()
    {
        // K-032: model adlari yapilandirmadan gelebilir ve katalog bir dogrulama
        // listesi degildir. Katalogda hic olmayan bir model reddedilmemelidir.
        var provider = new FakeModelProvider(models: []);
        var compiler = new AgentDefinitionCompiler(TestData.Providers(provider), TestData.Registry());

        var definition = TestData.Definition() with
        {
            Model = TestData.Binding() with
            {
                ResponseFormat = new AgentResponseFormat
                {
                    Kind = AgentResponseFormatKind.JsonSchema,
                    Schema = ParseSchema("""{"type":"object"}"""),
                },
            },
        };

        var agent = compiler.Compile(definition);

        agent.ShouldNotBeNull();
    }

    private static JsonElement ParseSchema(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
