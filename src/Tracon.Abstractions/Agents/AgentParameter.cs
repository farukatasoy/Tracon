namespace Tracon;

/// <summary>
/// A single named placeholder an <see cref="AgentDefinition"/>'s instructions can
/// reference as <c>{{name}}</c>.
/// </summary>
/// <remarks>
/// <para>
/// The definition's compiler validates every definition's schema at compile
/// time: every <c>{{name}}</c> that appears in the instructions text must be
/// declared here, every declared name must be a valid identifier, and no name
/// may repeat. Every culture variant in
/// <see cref="AgentDefinition.InstructionsByCulture"/> is checked against this
/// same schema — a variant that references an undeclared name fails compilation
/// even if another variant does not use it.
/// </para>
/// <para>
/// A schema entry that never appears in the instructions text is not an error and
/// the validator reports nothing for it; the reverse direction (used but undeclared)
/// is what compilation rejects.
/// </para>
/// </remarks>
public sealed record AgentParameter
{
    /// <summary>Gets the parameter name, as referenced by <c>{{name}}</c> in the instructions text.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the parameter's data kind.</summary>
    public required AgentParameterKind Kind { get; init; }

    /// <summary>
    /// Gets whether a run must supply this parameter. A run missing a required
    /// parameter with no <c>DefaultValue</c> does not start. An empty string counts as a
    /// supplied value, so a required parameter given one is accepted.
    /// </summary>
    public bool Required { get; init; }

    /// <summary>
    /// Gets the value used when a run does not supply this parameter. Applies
    /// whether or not <c>Required</c> is set. A run that supplies an empty string has
    /// supplied a value, so the placeholder is replaced with empty text and not with this default.
    /// </summary>
    public string? DefaultValue { get; init; }

    /// <summary>Gets a short description shown to the person authoring a run.</summary>
    public string? Description { get; init; }
}
