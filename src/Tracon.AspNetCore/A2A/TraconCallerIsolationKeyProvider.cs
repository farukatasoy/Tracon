using Microsoft.Agents.AI.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>
/// Names the caller of the current A2A request, so Microsoft Agent Framework
/// scopes a conversation to that caller.
/// </summary>
/// <remarks>
/// <para>
/// An A2A <c>contextId</c> arrives from the wire. It is a handle that resumes a
/// conversation, not a credential: without a caller in the key, any caller that
/// sent the same value would read — and extend — someone else's conversation.
/// </para>
/// <para>
/// The key is the caller's <strong>kind</strong> and identity, never the identity
/// alone. A claim value that reads <c>apikey:abc</c> is the user
/// <c>user:apikey:abc</c>, not the API key <c>apikey:abc</c>.
/// </para>
/// <list type="number">
/// <item><description>
/// An authenticated user: <c>user:{actor}</c>, the actor
/// <see cref="IAuditActorResolver"/> reads (it honours
/// <c>Tracon:Audit:ActorClaimType</c>). When that resolver has nothing — Tracon's
/// endpoint filter did not run, as on an A2A endpoint the application maps itself
/// with Microsoft Agent Framework — the same rules are applied to the request's
/// user. A user whose actor cannot be read gets
/// <see langword="null"/> — the request fails. It is never moved to the
/// anonymous key, which would merge every such user into one conversation.
/// </description></item>
/// <item><description>An API key: <c>apikey:{id}</c>.</description></item>
/// <item><description>The static <c>AuthToken</c>: <c>static-token</c>. It names the installation, not a caller.</description></item>
/// <item><description>
/// No identity at all: <c>anonymous</c>. This is the zero-configuration local
/// operator: loopback, no token, no key. Every such caller shares one
/// conversation space, as they share every other Tracon surface.
/// </description></item>
/// </list>
/// <para>
/// A consumer replaces this provider by registering an
/// <see cref="AgentIsolationKeyProvider"/> of their own.
/// </para>
/// </remarks>
internal sealed class TraconCallerIsolationKeyProvider : AgentIsolationKeyProvider
{
    private readonly IHttpContextAccessor _accessor;
    private readonly IAuditActorResolver _actors;
    private readonly IOptions<TraconOptions> _options;

    /// <summary>Creates a new provider.</summary>
    /// <param name="accessor">Reads the current request.</param>
    /// <param name="actors">Reads the audit actor of an authenticated user.</param>
    /// <param name="options">Supplies the configured actor claim type.</param>
    /// <exception cref="ArgumentNullException">A dependency is <see langword="null"/>.</exception>
    public TraconCallerIsolationKeyProvider(
        IHttpContextAccessor accessor,
        IAuditActorResolver actors,
        IOptions<TraconOptions> options)
    {
        ArgumentNullException.ThrowIfNull(accessor);
        ArgumentNullException.ThrowIfNull(actors);
        ArgumentNullException.ThrowIfNull(options);

        _accessor = accessor;
        _actors = actors;
        _options = options;
    }

    /// <inheritdoc />
    /// <remarks>
    /// <see langword="null"/> when there is no request, or when an authenticated
    /// user has no readable actor. Microsoft Agent Framework's strict store
    /// refuses the call in both cases.
    /// </remarks>
    public override ValueTask<string?> GetIsolationKeyAsync(CancellationToken cancellationToken = default)
        => ValueTask.FromResult(Resolve(_accessor.HttpContext));

    private string? Resolve(HttpContext? httpContext)
    {
        if (httpContext is null)
        {
            return null;
        }

        if (httpContext.User.Identity?.IsAuthenticated == true)
        {
            var actor = _actors.Resolve()
                ?? AmbientAuditActorResolver.ReadActor(httpContext.User, _options.Value.Audit.ActorClaimType);

            return actor is null ? null : "user:" + actor;
        }

        if (ApiKeyRequestContext.Get(httpContext) is { } apiKey)
        {
            return "apikey:" + apiKey.Id;
        }

        return StaticTokenRequestContext.IsSet(httpContext) ? "static-token" : "anonymous";
    }
}
