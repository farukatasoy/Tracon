using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Tracon.Core.UnitTests.Fakes;

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
            TestData.OpenToolPolicy(),
            ToolEffect.Read,
            requiredPermission: null,
            attribution: null,
            NullLogger<AuthorizingAIFunction>.Instance);

        // A bare AIFunctionArguments() carries the SAME empty service provider
        // MAF supplies at call time. This proves the wrapper resolves
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
            TestData.OpenToolPolicy(),
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
            TestData.OpenToolPolicy(),
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

    // --- The tenant tool policy, in front of the handler ---

    private static AuthorizingAIFunction Wrap(
        AIFunction inner,
        IToolAuthorizationHandler handler,
        TenantToolPolicyGate gate,
        string? requiredPermission = null,
        ILogger<AuthorizingAIFunction>? logger = null)
        => new(
            inner,
            handler,
            gate,
            ToolEffect.Read,
            requiredPermission,
            attribution: null,
            logger ?? NullLogger<AuthorizingAIFunction>.Instance);

    private static async Task<object?> InvokeAsTenantAsync(AIFunction function, string tenantId, CancellationToken cancellationToken = default)
    {
        // Set in the body of the test method: an AsyncLocal assigned in a helper does not flow back to the caller.
        TraconRunContext.SetCurrent(new AgentRunScope { RunId = Guid.NewGuid(), RootRunId = Guid.NewGuid(), TenantId = tenantId });

        try
        {
            return await function.InvokeAsync(new AIFunctionArguments(StringComparer.Ordinal), cancellationToken);
        }
        finally
        {
            TraconRunContext.SetCurrent(null);
        }
    }

    [Fact]
    public async Task A_call_the_policy_refuses_reaches_neither_the_handler_nor_the_tool()
    {
        var store = new InMemoryTenantToolPolicyStore();
        await store.UpsertAsync("acme", ["get_weather"], [], TestContext.Current.CancellationToken);

        var handler = new CountingHandler();
        var ran = false;
        var inner = AIFunctionFactory.Create(() => { ran = true; return "should never run"; }, "get_order");

        var result = await InvokeAsTenantAsync(Wrap(inner, handler, TestData.ToolPolicyGate(store)), "acme");

        result?.ToString().ShouldBe("This tool is not enabled for this tenant.");
        ran.ShouldBeFalse();
        handler.Calls.ShouldBe(0, "the consumer's handler was asked for a call that cannot run");
    }

    [Fact]
    public async Task A_call_both_gates_allow_runs_and_the_handler_sees_the_tenant_and_the_permission()
    {
        var store = new InMemoryTenantToolPolicyStore();
        await store.UpsertAsync("acme", [], ["orders.refund"], TestContext.Current.CancellationToken);

        var handler = new CountingHandler();
        var inner = AIFunctionFactory.Create(() => "refunded", "refund_order");

        var result = await InvokeAsTenantAsync(
            Wrap(inner, handler, TestData.ToolPolicyGate(store), requiredPermission: "orders.refund"),
            "acme");

        result?.ToString().ShouldBe("refunded");
        handler.Calls.ShouldBe(1);
        handler.LastRequest.ShouldNotBeNull().TenantId.ShouldBe("acme");
        handler.LastRequest.RequiredPermission.ShouldBe("orders.refund");
    }

    [Fact]
    public async Task A_call_the_policy_allows_and_the_handler_refuses_does_not_run()
    {
        var store = new InMemoryTenantToolPolicyStore();
        await store.UpsertAsync("acme", ["cancel_order"], [], TestContext.Current.CancellationToken);

        var ran = false;
        var inner = AIFunctionFactory.Create(() => { ran = true; return "should never run"; }, "cancel_order");

        var result = await InvokeAsTenantAsync(
            Wrap(inner, new DenyingHandler("Only a manager cancels."), TestData.ToolPolicyGate(store)),
            "acme");

        result?.ToString().ShouldBe("Only a manager cancels.");
        ran.ShouldBeFalse();
    }

    [Fact]
    public async Task A_run_outside_any_scope_is_the_default_tenant_for_the_policy_and_the_handler()
    {
        var store = new InMemoryTenantToolPolicyStore();
        await store.UpsertAsync("default", [], [], TestContext.Current.CancellationToken);

        var handler = new CountingHandler();
        var inner = AIFunctionFactory.Create(() => "result", "get_order");

        var result = await Wrap(inner, handler, TestData.ToolPolicyGate(store))
            .InvokeAsync(new AIFunctionArguments(StringComparer.Ordinal));

        result?.ToString().ShouldBe("This tool is not enabled for this tenant.");
        handler.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task A_store_that_throws_denies_the_call_fail_closed_and_logs_an_error()
    {
        var handler = new CountingHandler();
        var ran = false;
        var inner = AIFunctionFactory.Create(() => { ran = true; return "should never run"; }, "get_order");
        var logger = new RecordingLogger();

        var result = await InvokeAsTenantAsync(
            Wrap(inner, handler, TestData.ToolPolicyGate(new ThrowingPolicyStore()), logger: logger),
            "acme");

        // An exception from the store must not let the call through: with the switch off a missing policy is
        // "run everything", so a gate that read a throw as "no policy" would open exactly when the store is down.
        result?.ToString().ShouldBe("The authorization check failed. This call was denied and can be retried.");
        ran.ShouldBeFalse();
        handler.Calls.ShouldBe(0);
        logger.Errors.ShouldHaveSingleItem().ShouldContain("get_order");
    }

    [Fact]
    public async Task The_callers_own_cancellation_is_not_turned_into_a_denial()
    {
        using var source = new CancellationTokenSource();
        await source.CancelAsync();

        var inner = AIFunctionFactory.Create(() => "result", "get_order");
        var wrapped = Wrap(inner, new CountingHandler(), TestData.ToolPolicyGate(new ThrowingPolicyStore(new OperationCanceledException(source.Token))));

        await Should.ThrowAsync<OperationCanceledException>(
            async () => await InvokeAsTenantAsync(wrapped, "acme", source.Token));
    }

    private sealed class CountingHandler : IToolAuthorizationHandler
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public ToolAuthorizationRequest? LastRequest { get; private set; }

        public ValueTask<ToolAuthorizationResult> AuthorizeAsync(
            ToolAuthorizationRequest request, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _calls);
            LastRequest = request;

            return ValueTask.FromResult(ToolAuthorizationResult.Allow());
        }
    }

    private sealed class ThrowingPolicyStore(Exception? failure = null) : ITenantToolPolicyStore
    {
        public ValueTask<TenantToolPolicy?> GetAsync(string tenantId, CancellationToken cancellationToken = default)
            => throw (failure ?? new InvalidOperationException("the policy store is unreachable"));

        public ValueTask<TenantToolPolicy> UpsertAsync(
            string tenantId, IReadOnlyList<string> allowedTools, IReadOnlyList<string> grantedPermissions, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<bool> DeleteAsync(string tenantId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class RecordingLogger : ILogger<AuthorizingAIFunction>
    {
        private readonly List<string> _errors = [];

        public IReadOnlyList<string> Errors
        {
            get
            {
                lock (_errors)
                {
                    return [.. _errors];
                }
            }
        }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
            => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel < LogLevel.Error)
            {
                return;
            }

            lock (_errors)
            {
                _errors.Add(formatter(state, exception));
            }
        }
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
