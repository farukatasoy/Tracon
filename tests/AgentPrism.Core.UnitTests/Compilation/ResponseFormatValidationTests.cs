using System.Text.Json;
using AgentPrism.Core.UnitTests.Fakes;

namespace AgentPrism.Core.UnitTests.Compilation;

public sealed class ResponseFormatValidationTests
{
    [Fact]
    public void JsonSchema_kipi_semasiz_derlemeyi_durdurur()
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
    public void Text_kipi_semayla_verilirse_derlemeyi_durdurur()
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
    public void Json_kipi_semayla_verilirse_derlemeyi_durdurur()
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
    public void Sema_JSON_nesnesi_degilse_derlemeyi_durdurur()
    {
        var compiler = CreateCompiler();

        var definition = TestData.Definition() with
        {
            Model = TestData.Binding() with
            {
                ResponseFormat = new AgentResponseFormat
                {
                    Kind = AgentResponseFormatKind.JsonSchema,
                    Schema = ParseSchema("""["degil", "nesne"]"""),
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
