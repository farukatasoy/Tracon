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
/// <para>
/// A name over <see cref="MaxActorLength"/> characters is shortened (<see cref="LabelFit"/>: its start, then a digest of the
/// whole), the same on every store. The actor is written to ten narrow columns (<c>created_by</c>, <c>decided_by</c>,
/// <c>granted_by</c>, <c>responded_by</c> and the like), and a claim of an identity provider has no bound of its own: a write
/// that SQL Server refuses for its width loses the very record that names who acted.
/// </para>
/// </remarks>
internal sealed class AmbientAuditActorResolver : IAuditActorResolver
{
    /// <summary>The longest actor name any store column holds; a longer one is shortened.</summary>
    internal const int MaxActorLength = 200;

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
            return Fitted(NonEmpty(user.FindFirst(claimType)?.Value));
        }

        return Fitted(
            NonEmpty(user.FindFirst(ClaimTypes.NameIdentifier)?.Value)
            ?? NonEmpty(user.FindFirst(ClaimTypes.Name)?.Value)
            ?? NonEmpty(user.FindFirst("sub")?.Value));
    }

    private static string? Fitted(string? actor) => LabelFit.Fit(actor, MaxActorLength);

    private static string? NonEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
