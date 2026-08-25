namespace AgentPrism.Samples.CustomAgentSource;

/// <summary>Configuration for <see cref="JsonFileAgentSource"/>.</summary>
/// <remarks>
/// <c>AddAgentSource&lt;TSource&gt;()</c> constructs <c>TSource</c> through DI, so a
/// source that needs its own configuration reads it the same way every other
/// AgentPrism-registered service does — as an injected settings object, not a
/// constructor parameter supplied by hand. Register it once, before
/// <c>AddAgentSource&lt;JsonFileAgentSource&gt;()</c>:
/// <c>builder.Services.AddSingleton(new JsonFileAgentSourceOptions { Directory = path });</c>
/// </remarks>
public sealed class JsonFileAgentSourceOptions
{
    /// <summary>The directory to read <c>*.json</c> agent definitions from.</summary>
    public required string Directory { get; init; }
}
