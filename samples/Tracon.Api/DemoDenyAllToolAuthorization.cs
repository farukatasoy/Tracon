namespace Tracon.Api;

/// <summary>
/// 🚨 <strong>DEMONSTRATION ONLY. NEVER USE THIS IN PRODUCTION.</strong>
/// </summary>
/// <remarks>
/// <para>
/// Refuses every tool call. It expresses no policy: a real deployment writes
/// its own <see cref="IToolAuthorizationHandler"/> against its own permission
/// model and refuses only what that model refuses.
/// </para>
/// <para>
/// It exists so the refusal PATH can be observed. Tracon allows every tool
/// call unless a consumer refuses one, so without this the seam does nothing
/// in the reference application, and the behavior that matters — the model
/// receives the refusal as a tool RESULT and carries on, rather than the run
/// failing — has no way to be seen. Off by default; turns on only with
/// <c>Tracon:Demo:ToolAuthorization:Mode=deny-all</c>.
/// </para>
/// </remarks>
internal sealed class DemoDenyAllToolAuthorization : IToolAuthorizationHandler
{
    /// <inheritdoc />
    public ValueTask<ToolAuthorizationResult> AuthorizeAsync(
        ToolAuthorizationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return ValueTask.FromResult(ToolAuthorizationResult.Deny(
            $"The demonstration handler refuses every tool call, including '{request.ToolName}'."));
    }
}
