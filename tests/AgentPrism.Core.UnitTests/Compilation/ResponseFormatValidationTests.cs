using System.Text.Json;
using AgentPrism.Core.UnitTests.Fakes;

namespace AgentPrism.Core.UnitTests.Compilation;

public sealed class ResponseFormatValidationTests
{
    [Fact]
    public void JsonSchema_mode_without_a_schema_stops_compilation()
    {
        var compiler = CreateCompiler();

        var definition = TestData.Definition() with
        {
            Model = TestData.Binding() with
            {
                ResponseFormat = new AgentResponseFormat { Kind = AgentResponseFormatKind.JsonSchema },
            },
        };

        var exception = Should.Throw<AgentPrismCompilationException>(() => compiler.Compile(definition));

        exception.AgentName.ShouldBe("test-agent");
        exception.Message.ShouldContain(nameof(AgentResponseFormat.Schema));
    }

    [Fact]
    public void Text_mode_with_a_schema_stops_compilation()
    {
        var compiler = CreateCompiler();

        var definition = TestData.Definition() with
        {
            Model = TestData.Binding() with
            {
                ResponseFormat = new AgentResponseFormat
                {
                    Kind = AgentResponseFormatKind.Text,
                    Schema = ParseSchema("""{"type":"object"}"""),
                },
            },
        };

        var exception = Should.Throw<AgentPrismCompilationException>(() => compiler.Compile(definition));

        exception.Message.ShouldContain(nameof(AgentResponseFormat.Schema));
    }

    [Fact]
    public void Json_mode_with_a_schema_stops_compilation()
    {
        var compiler = CreateCompiler();

        var definition = TestData.Definition() with
        {
            Model = TestData.Binding() with
            {
                ResponseFormat = new AgentResponseFormat
                {
                    Kind = AgentResponseFormatKind.Json,
                    Schema = ParseSchema("""{"type":"object"}"""),
                },
            },
        };

        var exception = Should.Throw<AgentPrismCompilationException>(() => compiler.Compile(definition));

        exception.Message.ShouldContain(nameof(AgentResponseFormat.Schema));
    }

    [Fact]
    public void Schema_that_is_not_a_JSON_object_stops_compilation()
    {
        var compiler = CreateCompiler();

        var definition = TestData.Definition() with
        {
            Model = TestData.Binding() with
            {
                ResponseFormat = new AgentResponseFormat
                {
                    Kind = AgentResponseFormatKind.JsonSchema,
                    Schema = ParseSchema("""["not", "object"]"""),
                },
            },
        };

        var exception = Should.Throw<AgentPrismCompilationException>(() => compiler.Compile(definition));

        exception.Message.ShouldContain(nameof(AgentResponseFormat.Schema));
    }

    private static AgentDefinitionCompiler CreateCompiler()
        => new(TestData.Providers(new FakeModelProvider()), TestData.Registry());

    private static JsonElement ParseSchema(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
