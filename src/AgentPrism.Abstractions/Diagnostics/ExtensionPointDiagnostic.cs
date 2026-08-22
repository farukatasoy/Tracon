namespace AgentPrism;

/// <summary>
/// One host-bound extension point and whether the host replaced its built-in default.
/// </summary>
/// <remarks>
/// <para>
/// AgentPrism embeds into a host application through five contracts:
/// <c>ITenantContext</c>, <c>IRunAttributionContext</c>,
/// <c>IToolAuthorizationHandler</c>, <c>IRunEventSink</c>, and
/// <c>IAttachmentStorage</c>. Each is registered with <c>TryAdd</c>, so an
/// application that registers nothing keeps AgentPrism's built-in behavior
/// exactly. This record answers, for each of the five, whether the host
/// replaced that default.
/// </para>
/// <para>
/// This is the list, and it does not grow to cover every contract AgentPrism
/// registers with <c>TryAdd</c>: turning every replaceable registration into a
/// row would produce a dependency-injection dump instead of an answer to
/// "which of my five embedding points are wired".
/// </para>
/// </remarks>
public sealed record ExtensionPointDiagnostic
{
    /// <summary>Gets the contract's name, for example <c>ITenantContext</c>.</summary>
    public required string Contract { get; init; }

    /// <summary>Gets the registered implementation's type name.</summary>
    public required string Implementation { get; init; }

    /// <summary>Gets whether the registration is AgentPrism's built-in default.</summary>
    public required bool IsBuiltInDefault { get; init; }
}
