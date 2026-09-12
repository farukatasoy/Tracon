using System.Text.Json;
using Tracon.Core.UnitTests.Fakes;
using Microsoft.Extensions.AI;

namespace Tracon.Core.UnitTests.Compilation;

public sealed class ResponseFormatCapabilityTests
{
    [Fact]
    public void Compilation_is_rejected_for_a_model_with_SupportsStructuredOutput_false()
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

        var exception = Should.Throw<TraconCompilationException>(() => compiler.Compile(definition));

        exception.AgentName.ShouldBe("test-agent");
        exception.Message.ShouldContain("fake-model");
    }

    [Fact]
    public void Compilation_succeeds_for_a_model_with_SupportsStructuredOutput_true()
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
    public void Check_is_skipped_when_the_model_is_not_in_the_catalog()
    {
        // K-032: model names can come from configuration, and the catalog is not
        // a validation list. A model absent from the catalog entirely must not
        // be rejected.
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
