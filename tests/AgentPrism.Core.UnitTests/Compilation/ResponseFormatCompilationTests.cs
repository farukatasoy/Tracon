using System.Text.Json;
using AgentPrism.Core.UnitTests.Fakes;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace AgentPrism.Core.UnitTests.Compilation;

public sealed class ResponseFormatCompilationTests
{
    [Fact]
    public void ResponseFormat_verilmezse_secenek_bos_kalir()
    {
        var compiler = new AgentDefinitionCompiler(TestData.Providers(new FakeModelProvider()), TestData.Registry());

        var agent = compiler.Compile(TestData.Definition());
        var options = agent.GetService<ChatClientAgentOptions>();

        options!.ChatOptions!.ResponseFormat.ShouldBeNull();
    }

    [Fact]
    public void Text_kipi_ChatResponseFormatText_uretir()
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
    public void Json_kipi_sema_olmadan_ChatResponseFormatJson_uretir()
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
    public void JsonSchema_kipi_semayi_tasir()
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
                    SchemaDescription = "Bir fatura ozetinin semasi.",
                },
            },
        };

        var agent = compiler.Compile(definition);
        var options = agent.GetService<ChatClientAgentOptions>();

        var format = options!.ChatOptions!.ResponseFormat.ShouldBeOfType<ChatResponseFormatJson>();
        format.Schema.ShouldNotBeNull();
        format.SchemaName.ShouldBe("invoice");
        format.SchemaDescription.ShouldBe("Bir fatura ozetinin semasi.");
    }

    private static JsonElement ParseSchema(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
