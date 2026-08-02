namespace AgentPrism.AspNetCore.FunctionalTests.Infrastructure;

/// <summary>Testlerde tekrar eden nesneleri uretir.</summary>
internal static class TestData
{
    /// <summary>Yankilayan saglayiciya bagli bir model.</summary>
    public static ModelBinding Model() => new() { Provider = "echo", Model = "echo-1" };

    /// <summary>Kodda tanimlanabilir bir agent tanimi.</summary>
    /// <param name="name">Agent adi.</param>
    /// <returns>Tanim.</returns>
    public static AgentDefinition Definition(string name = "kod-agent")
        => new()
        {
            Name = name,
            DisplayName = "Kod Agent'i",
            Description = "Testlerde kullanilan kod agent'i.",
            Instructions = "Kisa yanit ver.",
            Model = Model(),
            Origin = AgentDefinitionOrigin.Code,
        };

    /// <summary>Yonetim API'sine gonderilebilir bir tanim istegi govdesi.</summary>
    /// <param name="name">Agent adi.</param>
    /// <param name="instructions">Sistem talimati.</param>
    /// <returns>JSON'a cevrilebilir istek.</returns>
    public static AgentDefinitionRequest Request(string name = "db-agent", string instructions = "Kisa yanit ver.")
        => new()
        {
            Name = name,
            DisplayName = "Veritabani Agent'i",
            Instructions = instructions,
            Model = Model(),
        };
}
