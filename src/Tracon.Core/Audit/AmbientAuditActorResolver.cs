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
        => ReadActor(AuditActorContext.Current, _options.Value.Audit.ActorClaimType);

    /// <summary>Reads the actor of a user by the resolver's rules.</summary>
    /// <param name="user">The user; <see langword="null"/> or unauthenticated answers <see langword="null"/>.</param>
    /// <param name="claimType">The configured actor claim type, or <see langword="null"/> for the default order.</param>
    /// <returns>The actor, or <see langword="null"/> when the user has none.</returns>
    /// <remarks>
    /// Kept separate from the ambient read so a caller that holds the user itself
    /// (the A2A isolation key) applies the same rules without the ambient value.
    /// </remarks>
    internal static string? ReadActor(ClaimsPrincipal? user, string? claimType)
    {
        if (user?.Identity?.IsAuthenticated != true)
        {
            return null;
        }

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
