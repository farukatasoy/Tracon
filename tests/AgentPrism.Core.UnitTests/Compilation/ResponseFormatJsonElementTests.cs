using System.Text.Json;

namespace AgentPrism.Core.UnitTests.Compilation;

/// <summary>
/// <see cref="AgentResponseFormat.Schema"/> icin MEMORY.md'nin struct tuzagini
/// dogrular: atanmamis bir <see cref="JsonElement"/> alani <c>default</c> kalirsa
/// <c>ValueKind = Undefined</c> olur ve seri hale getirme onu iceren liste ucunun
/// TAMAMINI cokertir. Sozlesme <c>Schema</c>'yi <c>JsonElement?</c> yaptigi icin
/// atanmamis alan <see langword="null"/>'dur, <c>Undefined</c> degil.
/// </summary>
public sealed class ResponseFormatJsonElementTests
{
    [Fact]
    public void Sema_atanmamis_AgentResponseFormat_liste_ucunu_cokertmez()
    {
        var bindings = new List<ModelBinding>
        {
            new() { Provider = "openai", Model = "gpt-5" },
            new()
            {
                Provider = "openai",
                Model = "gpt-5",
                ResponseFormat = new AgentResponseFormat { Kind = AgentResponseFormatKind.Text },
            },
            new() { Provider = "anthropic", Model = "claude-sonnet-5" },
        };

        var json = JsonSerializer.Serialize(bindings);
        var roundTripped = JsonSerializer.Deserialize<List<ModelBinding>>(json);

        roundTripped.ShouldNotBeNull();
        roundTripped!.Count.ShouldBe(3);
        roundTripped[1].ResponseFormat.ShouldNotBeNull();
        roundTripped[1].ResponseFormat!.Schema.ShouldBeNull();
    }

    [Fact]
    public void Bos_AgentResponseFormat_serilestirmede_null_sema_uretir()
    {
        var format = new AgentResponseFormat { Kind = AgentResponseFormatKind.Json };

        var json = JsonSerializer.Serialize(format);
        var roundTripped = JsonSerializer.Deserialize<AgentResponseFormat>(json);

        roundTripped.ShouldNotBeNull();
        roundTripped!.Schema.ShouldBeNull();
    }
}
