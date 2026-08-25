namespace AgentPrism;

/// <summary>Configures metadata for a tool registration.</summary>
public sealed class ToolRegistrationOptions
{
    /// <summary>Gets or sets whether a call needs explicit approval.</summary>
    public bool RequiresApproval { get; set; }

    /// <summary>Gets or sets the tool effect. The default is <see cref="ToolEffect.Read"/>.</summary>
    public ToolEffect Effect { get; set; } = ToolEffect.Read;

    /// <summary>Gets or sets the permission required to invoke the tool.</summary>
    public string? RequiredPermission { get; set; }

    /// <summary>Gets or sets the tool timeout, or <see langword="null"/> to use the installation default.</summary>
    public TimeSpan? Timeout { get; set; }

    /// <summary>Gets or sets whether an interrupted external or destructive call may be repeated.</summary>
    public bool SafeToRepeat { get; set; }

    /// <summary>Gets or sets the maximum UTF-8 result size, or <see langword="null"/> to use the installation default.</summary>
    public int? MaxOutputBytes { get; set; }

    /// <summary>Gets or sets the tool source. Code-defined tools use <see langword="null"/>.</summary>
    public string? Source { get; set; }
}
