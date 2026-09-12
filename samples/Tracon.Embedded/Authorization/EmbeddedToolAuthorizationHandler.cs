namespace Tracon.Embedded;

/// <summary>
/// 🚨 <strong>DEMONSTRATION ONLY.</strong> Denies a tool call when the tool
/// declared a <see cref="ToolAuthorizationRequest.RequiredPermission"/> the
/// caller's tenant is not on the allow list for.
/// </summary>
/// <remarks>
/// A real host answers this from its own permission system — a role table, a
/// policy engine, an authorization service it already calls for its other
/// features. This class stands in with a fixed in-memory map so the sample
/// runs without one.
/// </remarks>
internal sealed class EmbeddedToolAuthorizationHandler : IToolAuthorizationHandler
{
    // Tenant -> permissions it may use. A permission absent from EVERY set
    // (like "admin" here) is unreachable by any tenant in this sample.
    private static readonly Dictionary<string, IReadOnlySet<string>> Grants =
        new(StringComparer.Ordinal)
        {
            ["acme"] = new HashSet<string>(StringComparer.Ordinal) { "read-account" },
            ["globex"] = new HashSet<string>(StringComparer.Ordinal) { "read-account" },
        };

    public ValueTask<ToolAuthorizationResult> AuthorizeAsync(
        ToolAuthorizationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.RequiredPermission is null)
        {
            return new ValueTask<ToolAuthorizationResult>(ToolAuthorizationResult.Allow());
        }

        var allowed = Grants.TryGetValue(request.TenantId, out var permissions)
            && permissions.Contains(request.RequiredPermission);

        return new ValueTask<ToolAuthorizationResult>(
            allowed
                ? ToolAuthorizationResult.Allow()
                : ToolAuthorizationResult.Deny($"'{request.TenantId}' is not granted '{request.RequiredPermission}'."));
    }
}
