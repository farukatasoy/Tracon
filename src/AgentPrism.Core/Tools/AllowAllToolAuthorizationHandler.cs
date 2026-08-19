namespace AgentPrism;

/// <summary>
/// The default <see cref="IToolAuthorizationHandler"/>: allows every call.
/// </summary>
/// <remarks>
/// Registered with <c>TryAdd</c> by <c>AddAgentPrism()</c> (K1 — an
/// installation that registers nothing keeps today's behavior exactly). A
/// consumer replaces the registration to enforce its own rule.
/// </remarks>
public sealed class AllowAllToolAuthorizationHandler : IToolAuthorizationHandler
{
    /// <inheritdoc />
    public ValueTask<ToolAuthorizationResult> AuthorizeAsync(
        ToolAuthorizationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return ValueTask.FromResult(ToolAuthorizationResult.Allow());
    }
}
