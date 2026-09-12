using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tracon.Core.UnitTests.Tools;

/// <summary>
/// Verifies <see cref="AuthorizingAIFunction"/>: allow passes through, denial
/// returns an ordinary result instead of throwing, and a faulting handler
/// denies (fail-closed) instead of letting the call through.
/// </summary>
public sealed class AuthorizingAIFunctionTests
{
    [Fact]
    public async Task Allowed_call_runs_the_inner_function()
    {
        var inner = AIFunctionFactory.Create(() => "the real result", "get_order");

        var wrapped = new AuthorizingAIFunction(
            inner,
            new AllowAllToolAuthorizationHandler(),
            ToolEffect.Read,
            requiredPermission: null,
            attribution: null,
            NullLogger<AuthorizingAIFunction>.Instance);

        // A bare AIFunctionArguments() carries the SAME empty service provider
        // MAF supplies at call time (K-218). This proves the wrapper resolves
        // nothing from it — every dependency was already captured at
        // construction time.
        var result = await wrapped.InvokeAsync(new AIFunctionArguments(StringComparer.Ordinal));

        result?.ToString().ShouldBe("the real result");
    }

    [Fact]
    public async Task Denied_call_returns_the_reason_as_an_ordinary_result_and_does_not_run_the_inner_function()
    {
        var ran = false;
        var inner = AIFunctionFactory.Create(() => { ran = true; return "should never run"; }, "cancel_order");

        var wrapped = new AuthorizingAIFunction(
            inner,
            new DenyingHandler("Refunds require the 'orders.refund' permission."),
            ToolEffect.Destructive,
            requiredPermission: "orders.refund",
            attribution: null,
            NullLogger<AuthorizingAIFunction>.Instance);

        var result = await wrapped.InvokeAsync(new AIFunctionArguments(StringComparer.Ordinal));

        result?.ToString().ShouldBe("Refunds require the 'orders.refund' permission.");
        ran.ShouldBeFalse();
    }

    [Fact]
    public async Task A_faulting_handler_denies_the_call_fail_closed()
    {
        var ran = false;
        var inner = AIFunctionFactory.Create(() => { ran = true; return "should never run"; }, "cancel_order");

        var wrapped = new AuthorizingAIFunction(
            inner,
            new ThrowingHandler(),
            ToolEffect.Destructive,
            requiredPermission: null,
            attribution: null,
            NullLogger<AuthorizingAIFunction>.Instance);

        // A fail-open gate is not a gate: the call must be DENIED, not
        // propagate the handler's exception and drop the run.
        var result = await wrapped.InvokeAsync(new AIFunctionArguments(StringComparer.Ordinal));

        result.ShouldNotBeNull();
        ran.ShouldBeFalse();
    }

    private sealed class DenyingHandler(string reason) : IToolAuthorizationHandler
    {
        public ValueTask<ToolAuthorizationResult> AuthorizeAsync(
            ToolAuthorizationRequest request, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(ToolAuthorizationResult.Deny(reason));
    }

    private sealed class ThrowingHandler : IToolAuthorizationHandler
    {
        public ValueTask<ToolAuthorizationResult> AuthorizeAsync(
            ToolAuthorizationRequest request, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("the policy backend is unreachable");
    }
}
