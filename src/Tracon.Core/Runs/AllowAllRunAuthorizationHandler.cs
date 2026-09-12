namespace Tracon;

/// <summary>
/// The default <see cref="IRunAuthorizationHandler"/>: allows every call.
/// </summary>
/// <remarks>
/// Registered with <c>TryAdd</c> by <c>AddTracon()</c> (the no-surprises rule — an
/// installation that registers nothing keeps today's behavior exactly). A
/// consumer replaces the registration to enforce its own rule.
/// </remarks>
internal sealed class AllowAllRunAuthorizationHandler : IRunAuthorizationHandler
{
    /// <inheritdoc />
    public ValueTask<RunAuthorizationResult> AuthorizeRunAsync(
        RunAuthorizationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return ValueTask.FromResult(RunAuthorizationResult.Allow());
    }

    /// <inheritdoc />
    public ValueTask<RunAuthorizationResult> AuthorizeSessionAsync(
        SessionAuthorizationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return ValueTask.FromResult(RunAuthorizationResult.Allow());
    }
}
