using AgentPrism.Core.UnitTests.Fakes;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgentPrism.Core.UnitTests.Tools;

/// <summary>
/// Verifies the composition order <c>ToolRegistry</c> installs — Authorizing
/// (outermost) then Timeout then ApprovalRequired (innermost) then the real
/// function — docs/69-TOOL-YETKILENDIRMESI-VE-TIMEOUT.md, section 69.1.
/// </summary>
public sealed class ToolRegistryWrapperOrderTests
{
    [Fact]
    public async Task A_denied_call_never_reaches_the_approval_wrapper_or_the_real_body()
    {
        var ran = false;

        var registration = new AgentPrismToolRegistration(
            AIFunctionFactory.Create(() => { ran = true; return "should never run"; }, "cancel_order"),
            requiresApproval: true,
            effect: ToolEffect.Destructive);

        var registry = new ToolRegistry(
            [registration],
            new DenyingHandler("This account cannot cancel orders."),
            TestData.DefaultOptionsMonitor(),
            attribution: null,
            NullLogger<AuthorizingAIFunction>.Instance,
            NullLogger<TimeoutAIFunction>.Instance);

        registry.TryGet("cancel_order", out var tool).ShouldBeTrue();

        // Authorization is outermost: a denied call is answered directly, it
        // never reaches ApprovalRequiredAIFunction (which would otherwise
        // produce a pending-approval signal) or the real body.
        var result = await ((AIFunction)tool!).InvokeAsync(new AIFunctionArguments(StringComparer.Ordinal));

        result?.ToString().ShouldBe("This account cannot cancel orders.");
        ran.ShouldBeFalse();
    }

    [Fact]
    public void The_approval_wrapper_stays_discoverable_through_the_outer_layers()
    {
        // 🚨 MEASURED, not assumed: ApprovalRequiredAIFunction.InvokeCoreAsync
        // does NOT itself defer — calling it directly runs the real body.
        // Microsoft Agent Framework's function-invoking client short-circuits
        // BEFORE ever calling InvokeAsync, by locating the ApprovalRequiredAIFunction
        // through the AITool.GetService(Type) pipeline (the same pattern
        // IChatClient.GetService uses for its own middleware chain). If
        // Authorizing/TimeoutAIFunction did not forward GetService to their
        // inner function (DelegatingAIFunction's default implementation
        // does), that lookup would fail and approval would silently stop
        // working for every tool wrapped by this registry.
        var registration = new AgentPrismToolRegistration(
            AIFunctionFactory.Create(() => "result", "dangerous_tool"),
            requiresApproval: true);

        var registry = new ToolRegistry(
            [registration],
            new AllowAllToolAuthorizationHandler(),
            TestData.DefaultOptionsMonitor(),
            attribution: null,
            NullLogger<AuthorizingAIFunction>.Instance,
            NullLogger<TimeoutAIFunction>.Instance);

        registry.TryGet("dangerous_tool", out var tool).ShouldBeTrue();

        ((AITool)tool!).GetService<ApprovalRequiredAIFunction>().ShouldBeOfType<ApprovalRequiredAIFunction>();
    }

    private sealed class DenyingHandler(string reason) : IToolAuthorizationHandler
    {
        public ValueTask<ToolAuthorizationResult> AuthorizeAsync(
            ToolAuthorizationRequest request, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(ToolAuthorizationResult.Deny(reason));
    }
}
