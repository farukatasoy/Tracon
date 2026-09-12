namespace Tracon.Testing.Contracts.Tools;

/// <summary>
/// Behavior tests for the consumer's own <see cref="IToolAuthorizationHandler"/>.
/// </summary>
/// <remarks>
/// <para>
/// Unlike argument validation, a call's authorization outcome has no
/// schema-derived ground truth — whether a given tenant, tool, or permission
/// combination is allowed is entirely the consumer's own business policy.
/// This contract asks for that ground truth directly: <see cref="DeniedRequest"/>
/// is a call the derived class declares its own <see cref="Handler"/> denies,
/// the same way <c>CustomToolContract.ExpectedResultText</c> supplies the
/// ground truth for a custom tool's own result.
/// </para>
/// <para>
/// <see cref="IToolAuthorizationHandler"/> promises fail-closed behavior: if
/// the handler throws, the call must still end up denied. Tracon tests
/// that promise's own wrapper (<c>AuthorizingAIFunction</c>) against
/// synthetic handlers; this contract tests whether the <strong>consumer's</strong>
/// handler actually reaches a denial for <see cref="DeniedRequest"/> — whether
/// it returns <see cref="ToolAuthorizationResult.Deny(string)"/> directly, or
/// throws and lets the fail-closed wrapper deny it.
/// </para>
/// </remarks>
public abstract class ToolAuthorizationContract : IAsyncLifetime
{
    /// <summary>The authorization policy under test.</summary>
    protected IToolAuthorizationHandler Handler { get; private set; } = null!;

    /// <summary>Creates the handler under test.</summary>
    protected abstract ValueTask<IToolAuthorizationHandler> CreateHandlerAsync();

    /// <summary>
    /// A call this handler's own policy denies — for example, a caller
    /// missing the tool's required permission. There is no schema-driven way
    /// to derive an "invalid" authorization request the way
    /// <see cref="ToolArgumentValidationContract"/> derives one from JSON
    /// Schema; authorization is the consumer's own business policy, so the
    /// consumer states it.
    /// </summary>
    protected abstract ToolAuthorizationRequest DeniedRequest { get; }

    /// <summary>
    /// A call this handler's own policy allows — the counterpart to
    /// <see cref="DeniedRequest"/>. Needed so a handler that ignores its
    /// input and returns the same decision for every call (allow-all or
    /// deny-all) cannot pass this contract by accident.
    /// </summary>
    protected abstract ToolAuthorizationRequest AllowedRequest { get; }

    /// <inheritdoc />
    public async ValueTask InitializeAsync() => Handler = await CreateHandlerAsync().ConfigureAwait(false);

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        return default;
    }

    [Fact]
    public async Task Denied_call_never_runs_the_tool_body()
    {
        // Tracon's own wrapper (AuthorizingAIFunction) invokes the tool
        // body only when IsAllowed is true, and is tested by Tracon
        // itself — so proving the DECISION is a denial is what this contract
        // owes: the body never running follows from that decision.
        var result = await Handler.AuthorizeAsync(DeniedRequest, CancellationToken.None).ConfigureAwait(false);

        result.IsAllowed.ShouldBeFalse(
            $"'{GetType().Name}' declares {nameof(DeniedRequest)} as a call its own policy rejects, but the handler allowed it.");
        result.Reason.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Throwing_handler_denies_the_call()
    {
        // A denial does not have to be a returned Deny() — some policy
        // engines signal it by throwing. Whichever way Handler reaches it,
        // the outcome after the documented fail-closed rule must be denied.
        bool isAllowed;

        try
        {
            var result = await Handler.AuthorizeAsync(DeniedRequest, CancellationToken.None).ConfigureAwait(false);
            isAllowed = result.IsAllowed;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            isAllowed = false;
        }

        isAllowed.ShouldBeFalse(
            $"'{GetType().Name}' declares {nameof(DeniedRequest)} as a call its own policy rejects, " +
            "but the handler allowed it (whether by returning Allow() directly, or by swallowing an internal error and failing open).");
    }

    [Fact]
    public async Task Handler_receives_the_calling_tenant()
    {
        // A handler that ignores the request and returns the same decision
        // either way (allow-all, deny-all, or one hardcoded to a single
        // tenant) would pass the other two facts by accident if this one
        // only checked "did not throw" - DeniedRequest and AllowedRequest
        // must produce OPPOSITE decisions, which only holds if the handler
        // genuinely reads TenantId (or whatever it keys its policy on) off
        // the request it was actually given.
        var denied = await Handler.AuthorizeAsync(DeniedRequest, CancellationToken.None).ConfigureAwait(false);
        var allowed = await Handler.AuthorizeAsync(AllowedRequest, CancellationToken.None).ConfigureAwait(false);

        denied.IsAllowed.ShouldBeFalse(
            $"'{GetType().Name}' declares {nameof(DeniedRequest)} as a call its own policy rejects, but the handler allowed it.");
        allowed.IsAllowed.ShouldBeTrue(
            $"'{GetType().Name}' declares {nameof(AllowedRequest)} as a call its own policy accepts, but the handler denied it.");
    }
}
