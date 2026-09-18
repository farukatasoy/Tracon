using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Tracon;

// Composition order and rationale: docs/127 (127.1, 127.2),
// docs/arsiv/fazlar/69-TOOL-YETKILENDIRMESI-VE-TIMEOUT.md (69.1),
// docs/arsiv/fazlar/89-TOOL-CIKTISI-BOYUT-SINIRI.md (89.3).
/// <summary>
/// The single place the tool wrapper chain is composed. The code-defined
/// tool registry (<see cref="ToolRegistry"/>) and the MCP tenant tool set
/// (<c>McpTenantTools</c>) both call this method, so a new layer added here
/// reaches every tool source at once — before this type existed, the chain
/// was built by hand in both places and a layer added to one could silently
/// miss the other.
/// </summary>
/// <remarks>
/// Composition order (outermost first): ExplainedFailure -&gt; Authorizing -&gt; Validating -&gt;
/// Timeout -&gt; ApprovalRequired -&gt; Truncating -&gt; the real function.
/// Authorization runs before everything else: asking for approval, waiting
/// out a timeout, or validating arguments for a call the caller could never
/// make is backwards. Validation runs before the timeout and the approval
/// wait: a malformed call should not consume a timeout budget or wait on a
/// human decision. Timeout sits outside approval: <c>ApprovalRequiredAIFunction</c>
/// never blocks on the human decision within one call — the decision resumes
/// as a NEW run — so this ordering only ever bounds the tool's own
/// execution. Truncating sits directly around the real function, inside
/// approval: it must see only the tool's own output, never the
/// pending-approval signal <c>ApprovalRequiredAIFunction</c> produces instead
/// of running the body.
/// </remarks>
internal static class ToolWrapperChain
{
    /// <summary>Builds the final, wrapped declaration for one tool registration.</summary>
    /// <param name="registration">The registration to wrap.</param>
    /// <param name="descriptor">
    /// The registration's already-built descriptor. Its <see cref="ToolDescriptor.Effect"/>
    /// is passed to the authorization handler as-is — a caller that promotes an
    /// MCP tool's effect (Read -&gt; External) does so before building this
    /// descriptor, not inside this method.
    /// </param>
    /// <param name="authorizationHandler">The authorization policy applied before every server-side call.</param>
    /// <param name="validator">The argument validation policy applied before every server-side call.</param>
    /// <param name="defaultTimeout">The timeout applied when the registration sets none.</param>
    /// <param name="defaultMaxOutputBytes">The output byte limit applied when the registration sets none.</param>
    /// <param name="attribution">The run attribution context, or <see langword="null"/> when none is registered.</param>
    /// <param name="authorizingLogger">The logger passed to the installed <see cref="AuthorizingAIFunction"/>.</param>
    /// <param name="timeoutLogger">The logger passed to the installed <see cref="TimeoutAIFunction"/>.</param>
    /// <param name="validatingLogger">The logger passed to the installed <see cref="ValidatingAIFunction"/>, when one is installed.</param>
    /// <returns>
    /// The wrapped, invocable declaration for a server-side tool, or
    /// <paramref name="registration"/>'s own declaration unchanged for a
    /// client-side (declaration-only) tool.
    /// </returns>
    /// <exception cref="TraconException">
    /// <paramref name="registration"/> requires approval but its body is not
    /// an <see cref="AIFunction"/> — a client-side tool has no server-side
    /// body to defer.
    /// </exception>
    internal static AIFunctionDeclaration Compose(
        TraconToolRegistration registration,
        ToolDescriptor descriptor,
        IToolAuthorizationHandler authorizationHandler,
        IToolArgumentsValidator validator,
        TimeSpan defaultTimeout,
        int? defaultMaxOutputBytes,
        IRunAttributionContext? attribution,
        ILogger<AuthorizingAIFunction> authorizingLogger,
        ILogger<TimeoutAIFunction> timeoutLogger,
        ILogger<ValidatingAIFunction> validatingLogger)
    {
        ArgumentNullException.ThrowIfNull(registration);
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(authorizationHandler);
        ArgumentNullException.ThrowIfNull(validator);
        ArgumentNullException.ThrowIfNull(authorizingLogger);
        ArgumentNullException.ThrowIfNull(timeoutLogger);
        ArgumentNullException.ThrowIfNull(validatingLogger);

        if (registration.Function is not AIFunction invocable)
        {
            if (registration.RequiresApproval)
            {
                throw new TraconException(
                    $"Tool '{registration.Function.Name}' cannot require approval: it runs on the client and has no " +
                    "server-side body to defer. Approval and client-side tools are separate mechanisms.");
            }

            // Declaration-only (client-side) tool: the server never invokes
            // it, so there is no execution to authorize, validate, or bound
            // with a timeout.
            return registration.Function;
        }

        var effectiveMaxOutputBytes = registration.MaxOutputBytes ?? defaultMaxOutputBytes;

        // This wrapper also canonicalizes every inline result and fails
        // closed for a raw CLR object without generated type information. It
        // is therefore present even when no explicit output budget is
        // configured; int.MaxValue means no practical trimming limit while
        // retaining the canonicalization boundary.
        AIFunction wrapped = new TruncatingAIFunction(invocable, effectiveMaxOutputBytes ?? int.MaxValue);

        wrapped = registration.RequiresApproval
            ? new ApprovalRequiredAIFunction(wrapped)
            : wrapped;

        wrapped = new TimeoutAIFunction(wrapped, registration.Timeout ?? defaultTimeout, timeoutLogger);

        // A no-op validator (the untouched default) adds no layer: an
        // installation that never registers its own validator pays no extra
        // cost on the call path — the same pattern ContentGuardPipeline.HasGuards uses.
        wrapped = validator is NoOpToolArgumentsValidator
            ? wrapped
            : new ValidatingAIFunction(wrapped, descriptor, validator, validatingLogger);

        wrapped = new AuthorizingAIFunction(
            wrapped,
            authorizationHandler,
            descriptor.Effect,
            registration.RequiredPermission,
            attribution,
            authorizingLogger);

        // Outermost, so it covers every layer beneath it: a rejected argument,
        // a denied call, a timeout and the tool's own body all reach the model
        // with the sentence Tracon wrote instead of "Error: Function failed."
        return new ExplainedFailureAIFunction(wrapped);
    }
}
