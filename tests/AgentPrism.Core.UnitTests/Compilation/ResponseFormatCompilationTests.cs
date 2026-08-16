using System.Text.Json;
using AgentPrism.Core.UnitTests.Fakes;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace AgentPrism.Core.UnitTests.Compilation;

public sealed class ResponseFormatCompilationTests
{
    [Fact]
    public void Option_stays_empty_when_ResponseFormat_is_not_given()
    {
        var compiler = new AgentDefinitionCompiler(TestData.Providers(new FakeModelProvider()), TestData.Registry());

        var agent = compiler.Compile(TestData.Definition());
        var options = agent.GetService<ChatClientAgentOptions>();

        options!.ChatOptions!.ResponseFormat.ShouldBeNull();
    }

    [Fact]
    public void Text_mode_produces_ChatResponseFormatText()
    {
        var compiler = new AgentDefinitionCompiler(TestData.Providers(new FakeModelProvider()), TestData.Registry());

        var definition = TestData.Definition() with
        {
            Model = TestData.Binding() with
            {
                ResponseFormat = new AgentResponseFormat { Kind = AgentResponseFormatKind.Text },
            },
        };

        var agent = compiler.Compile(definition);
        var options = agent.GetService<ChatClientAgentOptions>();

        options!.ChatOptions!.ResponseFormat.ShouldBeOfType<ChatResponseFormatText>();
    }

    [Fact]
    public void Json_mode_produces_ChatResponseFormatJson_without_a_schema()
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
                ResponseFormat = new AgentResponseFormat { Kind = AgentResponseFormatKind.Json },
            },
        };

        var agent = compiler.Compile(definition);
        var options = agent.GetService<ChatClientAgentOptions>();

        var format = options!.ChatOptions!.ResponseFormat.ShouldBeOfType<ChatResponseFormatJson>();
        format.Schema.ShouldBeNull();
    }

    [Fact]
    public void JsonSchema_mode_carries_the_schema()
    {
        var provider = new FakeModelProvider(models:
        [
            new ModelDescriptor { Name = "fake-model", SupportsStructuredOutput = true },
        ]);
        var compiler = new AgentDefinitionCompiler(TestData.Providers(provider), TestData.Registry());

        var schema = ParseSchema("""{"type":"object","properties":{"total":{"type":"number"}}}""");
        var definition = TestData.Definition() with
        {
            Model = TestData.Binding() with
            {
                ResponseFormat = new AgentResponseFormat
                {
                    Kind = AgentResponseFormatKind.JsonSchema,
                    Schema = schema,
                    SchemaName = "invoice",
                    SchemaDescription = "Schema for an invoice summary.",
                },
            },
        };

        var agent = compiler.Compile(definition);
        var options = agent.GetService<ChatClientAgentOptions>();

        var format = options!.ChatOptions!.ResponseFormat.ShouldBeOfType<ChatResponseFormatJson>();
        format.Schema.ShouldNotBeNull();
        format.SchemaName.ShouldBe("invoice");
        format.SchemaDescription.ShouldBe("Schema for an invoice summary.");
    }

    private static JsonElement ParseSchema(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
