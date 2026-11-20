namespace Tracon.Samples.CustomAgentSource;

/// <summary>Configuration for <see cref="JsonFileAgentDefinitionSource"/>.</summary>
/// <remarks>
/// <c>AddAgentDefinitionSource&lt;TSource&gt;()</c> constructs <c>TSource</c> through DI, so a
/// source that needs its own configuration reads it the same way every other
/// Tracon-registered service does — as an injected settings object, not a
/// constructor parameter supplied by hand. Register it once, before
/// <c>AddAgentDefinitionSource&lt;JsonFileAgentDefinitionSource&gt;()</c>:
/// <c>builder.Services.AddSingleton(new JsonFileAgentDefinitionSourceOptions { Directory = path });</c>
/// </remarks>
public sealed class JsonFileAgentDefinitionSourceOptions
{
    /// <summary>The directory to read <c>*.json</c> agent definitions from.</summary>
    public required string Directory { get; init; }
}
