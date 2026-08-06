using System.Text.Json;

namespace AgentPrism.Core.UnitTests.Compilation;

/// <summary>
/// Eski kayitlar <c>responseFormat</c> anahtarini taşımaz (bu faz oncesi yazilmis
/// tanimlar). Seri halden cikarma <see langword="null"/> uretmeli, istisna atmamali.
/// </summary>
public sealed class ResponseFormatSerializationTests
{
    [Fact]
    public void ResponseFormat_anahtari_olmayan_eski_json_null_uretir()
    {
        const string json = """{"provider":"openai","model":"gpt-5"}""";

        var binding = JsonSerializer.Deserialize<ModelBinding>(json, JsonSerializerOptions.Web);

        binding.ShouldNotBeNull();
        binding!.ResponseFormat.ShouldBeNull();
    }

    [Fact]
    public void Eski_AgentDefinition_json_kaynak_uretilmis_baglamla_okunur()
    {
        // AgentPrismCoreJsonContext, AgentPrism.PostgreSql/SqlServer/Sqlite'in
        // jsonb sutununa yazilan gercek yoldur (karar K-006). Bu faz oncesi
        // yazilmis bir satirin JSON'unda "responseFormat" anahtari hic yoktur.
        const string json = """
            {
              "name": "eski-agent",
              "instructions": "test",
              "model": { "provider": "openai", "model": "gpt-5" },
              "toolNames": [],
              "skillNames": [],
              "callableAgentNames": []
            }
            """;

        var definition = JsonSerializer.Deserialize(json, AgentPrismCoreJsonContext.Default.AgentDefinition);

        definition.ShouldNotBeNull();
        definition!.Model.ResponseFormat.ShouldBeNull();
    }
}
