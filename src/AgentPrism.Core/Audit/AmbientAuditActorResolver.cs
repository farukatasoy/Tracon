using System.Security.Claims;
using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>
/// Aktoru <see cref="AuditActorContext"/> icindeki kullanicidan okuyan varsayilan uygulama.
/// </summary>
/// <remarks>
/// Okuma sirasi: <see cref="AgentPrismAuditOptions.ActorClaimType"/> ayarliysa o claim;
/// aksi halde <see cref="ClaimTypes.NameIdentifier"/> → <see cref="ClaimTypes.Name"/> →
/// <c>sub</c> → <see langword="null"/>.
/// </remarks>
public sealed class AmbientAuditActorResolver : IAuditActorResolver
{
    private readonly IOptions<AgentPrismOptions> _options;

    /// <summary>Yeni bir cozumleyici olusturur.</summary>
    /// <param name="options">AgentPrism ayarlari.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> <see langword="null"/> ise.</exception>
    public AmbientAuditActorResolver(IOptions<AgentPrismOptions> options)
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
