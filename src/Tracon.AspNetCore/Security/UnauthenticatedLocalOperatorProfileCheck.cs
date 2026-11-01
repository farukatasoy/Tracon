namespace Tracon;

/// <summary>
/// Answers whether an anonymous request is still treated as the local operator.
/// </summary>
/// <remarks>
/// <para>
/// Built by <c>MapTracon</c> from the endpoint options it was given and added to
/// <see cref="LateProductionProfileChecks"/>, not registered in the container: the endpoint options are known only there.
/// A host that never calls <c>MapTracon</c> has no such check, and the validator reports the decision as not applicable.
/// </para>
/// <para>
/// With neither <see cref="TraconEndpointOptions.AuthToken"/> nor <see cref="TraconEndpointOptions.AuthorizationPolicy"/>
/// set, a request that presents no credential is the local operator (<see cref="AnonymousAccessGuard"/>). That promise
/// holds for a caller on this machine. A reverse proxy that listens on a Unix socket and adds no forwarding header makes a
/// remote caller look local, because a connection of that kind has no remote address. An API key does not answer the
/// decision: a request without one is still admitted. Neither value is read into the message, only whether it is set.
/// </para>
/// </remarks>
internal sealed class UnauthenticatedLocalOperatorProfileCheck : IProductionProfileCheck
{
    private const string Setting = "TraconEndpointOptions.AuthToken or TraconEndpointOptions.RequireAuthorization";

    private readonly bool _authenticated;

    /// <summary>Creates the check from the options <c>MapTracon</c> was given.</summary>
    /// <param name="options">The endpoint options.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    public UnauthenticatedLocalOperatorProfileCheck(TraconEndpointOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _authenticated = options.AuthToken is { Length: > 0 } || options.AuthorizationPolicy is { Length: > 0 };
    }

    /// <inheritdoc />
    public TraconProductionRisk Risk => TraconProductionRisk.UnauthenticatedLocalOperator;

    /// <inheritdoc />
    public ProductionProfileResult Evaluate(IServiceProvider services)
        => _authenticated
            ? ProductionProfileResult.Satisfied(Setting)
            : ProductionProfileResult.Permissive(
                Setting,
                "neither is set, so a request that presents no credential is the local operator with full rights, " +
                "and a reverse proxy on a Unix socket that adds no forwarding header makes a remote caller look local",
                "set TraconEndpointOptions.AuthToken, or TraconEndpointOptions.RequireAuthorization with a policy that " +
                "authenticates the caller. Accept the risk only when nothing but this machine can reach the endpoints");
}
