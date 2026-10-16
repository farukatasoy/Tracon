using System.Text.Json;

namespace Tracon;

/// <summary>Describes a function node registered in code.</summary>
internal sealed record WorkflowFunctionDescriptor
{
    /// <summary>Gets the function's unique name. Serves as the key a workflow node points to.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the short description of what the function does.</summary>
    public string? Description { get; init; }

    /// <summary>Gets the CLR type the function accepts as input.</summary>
    public required Type InputType { get; init; }

    /// <summary>Gets the CLR type the function returns.</summary>
    public required Type OutputType { get; init; }

    /// <summary>
    /// Gets the settings type the function declares, or <see langword="null"/> when
    /// the function takes no settings. A node that points to a function with no
    /// settings type must not carry <see cref="WorkflowNodeReference.Settings"/>.
    /// </summary>
    public Type? SettingsType { get; init; }

    /// <summary>
    /// Gets the JSON schema of <see cref="SettingsType"/>, or <see langword="null"/>
    /// when the function takes no settings.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>For display only.</strong> The schema helps an operator write a
    /// valid object; the check itself is the strict deserialization into
    /// <see cref="SettingsType"/>, so what the function receives is exactly what was
    /// checked.
    /// </para>
    /// <para>
    /// A settings type the schema exporter cannot describe is reported as the bare
    /// object schema <c>{"type":"object"}</c> with no fields listed, never as
    /// <see langword="null"/>: a missing schema means "no settings", and a function
    /// that takes settings must not look like one that does not.
    /// </para>
    /// </remarks>
    public JsonElement? SettingsSchema { get; init; }

    /// <summary>
    /// Gets the longest time one call of the function may take when its node sets no
    /// timeout of its own (<see cref="WorkflowNodeReference.Timeout"/>).
    /// <see langword="null"/> means the function has no time limit of its own; only
    /// the run's deadline stops it.
    /// </summary>
    public TimeSpan? Timeout { get; init; }
}
