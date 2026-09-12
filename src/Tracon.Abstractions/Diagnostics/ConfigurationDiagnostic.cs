namespace Tracon;

/// <summary>Reports whether a configuration key resolves. It does not carry the value.</summary>
public sealed record ConfigurationDiagnostic
{
    /// <summary>Gets the full configuration key path, for example <c>Tracon:Providers:OpenAI:ApiKey</c>.</summary>
    public required string Key { get; init; }

    /// <summary>Gets whether the key resolves to a non-empty value.</summary>
    public required bool Resolved { get; init; }

    /// <summary>
    /// Gets guidance for configuring an unresolved key. The server provides it and the
    /// UI does not translate it.
    /// </summary>
    public string? Hint { get; init; }
}
