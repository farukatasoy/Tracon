using System.Security.Claims;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>
/// The default implementation that reads the actor from the user in <see cref="AuditActorContext"/>.
/// </summary>
/// <remarks>
/// Read order: the configured <see cref="TraconAuditOptions.ActorClaimType"/> claim;
/// otherwise <see cref="ClaimTypes.NameIdentifier"/> → <see cref="ClaimTypes.Name"/> →
/// <c>sub</c> → <see langword="null"/>.
/// </remarks>
internal sealed class AmbientAuditActorResolver : IAuditActorResolver
{
    private readonly IOptions<TraconOptions> _options;

    /// <summary>Initializes a new resolver.</summary>
    /// <param name="options">The Tracon options.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    public AmbientAuditActorResolver(IOptions<TraconOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    /// <inheritdoc />
    public string? Resolve()
    {
        var user = AuditActorContext.Current;

        if (user?.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        var claimType = _options.Value.Audit.ActorClaimType;

        if (claimType is { Length: > 0 })
        {
            return NonEmpty(user.FindFirst(claimType)?.Value);
        }

        return NonEmpty(user.FindFirst(ClaimTypes.NameIdentifier)?.Value)
            ?? NonEmpty(user.FindFirst(ClaimTypes.Name)?.Value)
            ?? NonEmpty(user.FindFirst("sub")?.Value);
    }

    private static string? NonEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
