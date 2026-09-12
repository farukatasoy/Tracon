namespace Tracon.AspNetCore.FunctionalTests.Infrastructure;

/// <summary>Produces objects reused across tests.</summary>
internal static class TestData
{
    /// <summary>A model bound to the echo provider.</summary>
    public static ModelBinding Model() => new() { Provider = "echo", Model = "echo-1" };

    /// <summary>An agent definition that can be defined in code.</summary>
    /// <param name="name">The agent name.</param>
    /// <returns>The definition.</returns>
    public static AgentDefinition Definition(string name = "kod-agent")
        => new()
        {
            Name = name,
            DisplayName = "Code Agent",
            Description = "The code agent used in tests.",
            Instructions = "Reply briefly.",
            Model = Model(),
            Origin = AgentDefinitionOrigin.Code,
        };

    /// <summary>A definition request body that can be sent to the management API.</summary>
    /// <param name="name">The agent name.</param>
    /// <param name="instructions">The system instructions.</param>
    /// <returns>The request, ready to serialize to JSON.</returns>
    public static AgentDefinitionRequest Request(string name = "db-agent", string instructions = "Reply briefly.")
        => new()
        {
            Name = name,
            DisplayName = "Database Agent",
            Instructions = instructions,
            Model = Model(),
        };
}
