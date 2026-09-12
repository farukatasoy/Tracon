namespace Tracon;

/// <summary>Describes a function node registered in code.</summary>
public sealed record WorkflowFunctionDescriptor
{
    /// <summary>Gets the function's unique name. Serves as the key a workflow node points to.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the short description of what the function does.</summary>
    public string? Description { get; init; }

    /// <summary>Gets the CLR type the function accepts as input.</summary>
    public required Type InputType { get; init; }

    /// <summary>Gets the CLR type the function returns.</summary>
    public required Type OutputType { get; init; }
}
