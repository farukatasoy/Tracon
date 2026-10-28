namespace Tracon;

/// <summary>
/// The tools a tenant may run (tool policy): two closed lists, one of tool names and one of permissions.
/// </summary>
/// <remarks>
/// <para>
/// A call passes when its tool name is in <see cref="AllowedTools"/>, or when the tool declares a required
/// permission (<see cref="ToolDescriptor.RequiredPermission"/>) and that permission is in
/// <see cref="GrantedPermissions"/>. A tool that declares no permission and is not named is refused. Both
/// lists are compared exactly (ordinal), the way tool names are resolved. Two empty lists refuse every tool:
/// an empty policy is not the same as having none.
/// </para>
/// <para>
/// A tenant with no saved policy is <strong>unrestricted</strong>, unless <c>Tracon:Tools:RequireTenantPolicy</c>
/// is on. Then a tenant with no policy runs no tool at all, and the default tenant needs a policy of its own.
/// </para>
/// <para>
/// The policy is a gate in front of <see cref="IToolAuthorizationHandler"/>, not a replacement for it: a call
/// needs both to allow it. It covers the tools the handler is asked about (see <see cref="IToolAuthorizationHandler"/>
/// for the list) and no others.
/// </para>
/// </remarks>
public sealed record TenantToolPolicy
{
    /// <summary>Gets the tenant this policy belongs to.</summary>
    public required string TenantId { get; init; }

    /// <summary>Gets the closed set of tool names the tenant may run.</summary>
    public required IReadOnlyList<string> AllowedTools { get; init; }

    /// <summary>
    /// Gets the closed set of permissions granted to the tenant: a tool whose required permission is in this set
    /// may run.
    /// </summary>
    public required IReadOnlyList<string> GrantedPermissions { get; init; }

    /// <summary>Gets the time this policy was last written.</summary>
    public required DateTimeOffset UpdatedAt { get; init; }
}
