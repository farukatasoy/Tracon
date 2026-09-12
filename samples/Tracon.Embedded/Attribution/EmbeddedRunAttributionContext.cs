using Microsoft.AspNetCore.Http;

namespace Tracon.Embedded;

/// <summary>
/// 🚨 <strong>DEMONSTRATION ONLY.</strong> Resolves run attribution from a
/// header that stands in for the host's own identity pipeline — a claim on an
/// authenticated principal in a real deployment.
/// </summary>
/// <remarks>
/// Checks <see cref="AmbientRunAttributionScope"/> first, the same way
/// <see cref="EmbeddedTenantContext"/> checks <see cref="AmbientTenantScope"/>:
/// background work opens that scope directly (see
/// <c>Jobs/EmbeddedJobWorker.cs</c>) since there is no request header to read.
/// </remarks>
internal sealed class EmbeddedRunAttributionContext(IHttpContextAccessor accessor) : IRunAttributionContext
{
    /// <summary>The header carrying the host's own user identity.</summary>
    public const string UserHeader = "X-Host-User";

    public string? UserId =>
        AmbientRunAttributionScope.CurrentUserId
        ?? NullIfEmpty(accessor.HttpContext?.Request.Headers[UserHeader].ToString());

    /// <inheritdoc />
    /// <remarks>This sample assigns no labels; a real host can add its own job or feature labels here.</remarks>
    public IReadOnlyDictionary<string, string>? Labels => AmbientRunAttributionScope.CurrentLabels;

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
