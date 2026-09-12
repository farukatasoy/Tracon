using Microsoft.Extensions.AI;
using Tracon.Testing.Contracts.Tools;

namespace Tracon.Core.UnitTests.Tools;

/// <summary>
/// Proves that <see cref="ToolArgumentValidationContract"/> and
/// <see cref="ToolAuthorizationContract"/> are not test theater: for every
/// <c>[Fact]</c> they declare, a deliberately broken implementation makes
/// that specific scenario fail.
/// </summary>
/// <remarks>
/// The broken fixtures below derive from the contract classes the same way
/// <see cref="ToolArgumentValidationContractTests"/> and a real
/// <see cref="ToolAuthorizationContract"/> consumer would — but they are
/// <see langword="private"/> nested types, so xunit's discovery (which only
/// picks up public classes) never runs them as tests of their own; this
/// class invokes their inherited <c>[Fact]</c> methods directly and asserts
/// each one throws.
/// </remarks>
public sealed class ToolContractSelfProofTests
{
    [Fact]
    public Task Missing_required_argument_check_fails_for_an_accept_all_validator()
        => AssertGoesRedAsync(new AcceptAllValidatorFixture(), static c => c.Missing_required_argument_is_rejected());

    [Fact]
    public Task Type_mismatch_check_fails_for_an_accept_all_validator()
        => AssertGoesRedAsync(new AcceptAllValidatorFixture(), static c => c.Type_mismatch_is_rejected());

    [Fact]
    public Task Out_of_range_number_check_fails_for_an_accept_all_validator()
        => AssertGoesRedAsync(new AcceptAllValidatorFixture(), static c => c.Out_of_range_number_is_rejected());

    [Fact]
    public Task Pattern_violation_check_fails_for_an_accept_all_validator()
        => AssertGoesRedAsync(new AcceptAllValidatorFixture(), static c => c.Pattern_violation_is_rejected());

    [Fact]
    public Task Extra_property_check_fails_for_an_accept_all_validator()
        => AssertGoesRedAsync(new AcceptAllValidatorFixture(), static c => c.Unknown_extra_property_is_handled_deliberately());

    [Fact]
    public Task Throwing_validator_check_fails_for_an_accept_all_validator()
        => AssertGoesRedAsync(new AcceptAllValidatorFixture(), static c => c.Throwing_validator_rejects_the_call());

    [Fact]
    public Task Cancellation_check_fails_for_a_validator_that_ignores_the_token()
        => AssertGoesRedAsync(new AcceptAllValidatorFixture(), static c => c.A_pre_cancelled_token_is_honored());

    [Fact]
    public Task Denied_call_check_fails_for_a_handler_that_always_allows()
        => AssertGoesRedAsync(new AllowAllHandlerFixture(), static c => c.Denied_call_never_runs_the_tool_body());

    [Fact]
    public Task Throwing_handler_check_fails_for_a_handler_that_always_allows()
        => AssertGoesRedAsync(new AllowAllHandlerFixture(), static c => c.Throwing_handler_denies_the_call());

    [Fact]
    public Task Tenant_check_fails_for_a_handler_locked_to_one_tenant()
        => AssertGoesRedAsync(new TenantLockedHandlerFixture(), static c => c.Handler_receives_the_calling_tenant());

    [Fact]
    public Task Tenant_check_fails_for_a_handler_that_always_denies()
        => AssertGoesRedAsync(new AlwaysDenyHandlerFixture(), static c => c.Handler_receives_the_calling_tenant());

    private static async Task AssertGoesRedAsync(ToolArgumentValidationContract contract, Func<ToolArgumentValidationContract, Task> fact)
    {
        await contract.InitializeAsync().ConfigureAwait(false);
        var threw = await ThrowsAsync(() => fact(contract)).ConfigureAwait(false);

        threw.ShouldBeTrue("This deliberately broken fixture should have made the scenario fail, but it passed.");
    }

    private static async Task AssertGoesRedAsync(ToolAuthorizationContract contract, Func<ToolAuthorizationContract, Task> fact)
    {
        await contract.InitializeAsync().ConfigureAwait(false);
        var threw = await ThrowsAsync(() => fact(contract)).ConfigureAwait(false);

        threw.ShouldBeTrue("This deliberately broken fixture should have made the scenario fail, but it passed.");
    }

    private static async Task<bool> ThrowsAsync(Func<Task> action)
    {
        try
        {
            await action().ConfigureAwait(false);
            return false;
        }
        catch
        {
            return true;
        }
    }

    /// <summary>Always valid — the archetypal fail-open bug this contract exists to catch.</summary>
    private sealed class AcceptAllToolArgumentsValidator : IToolArgumentsValidator
    {
        public ValueTask<ToolArgumentsValidationResult> ValidateAsync(
            ToolDescriptor tool, AIFunctionArguments arguments, CancellationToken cancellationToken = default)
            => new(ToolArgumentsValidationResult.Valid);
    }

    private sealed class AcceptAllValidatorFixture : ToolArgumentValidationContract
    {
        protected override ValueTask<AIFunction> CreateToolAsync() => new(ValidatableSearchTool.Instance);

        protected override ValueTask<IToolArgumentsValidator> CreateValidatorAsync() => new(new AcceptAllToolArgumentsValidator());
    }

    /// <summary>Always allows — the archetypal fail-open bug an authorization handler can have.</summary>
    private sealed class AllowAllToolAuthorizationHandler : IToolAuthorizationHandler
    {
        public ValueTask<ToolAuthorizationResult> AuthorizeAsync(ToolAuthorizationRequest request, CancellationToken cancellationToken = default)
            => new(ToolAuthorizationResult.Allow());
    }

    private sealed class AllowAllHandlerFixture : ToolAuthorizationContract
    {
        protected override ValueTask<IToolAuthorizationHandler> CreateHandlerAsync() => new(new AllowAllToolAuthorizationHandler());

        protected override ToolAuthorizationRequest DeniedRequest => new()
        {
            ToolName = "contract_tool",
            Effect = ToolEffect.Write,
            TenantId = "denied-tenant",
            RunId = Guid.NewGuid(),
            AgentName = "contract-agent",
        };

        protected override ToolAuthorizationRequest AllowedRequest => DeniedRequest with { TenantId = "allowed-tenant" };
    }

    /// <summary>Recognizes exactly one hardcoded tenant and throws for any other — a real, easy mistake.</summary>
    private sealed class TenantLockedToolAuthorizationHandler : IToolAuthorizationHandler
    {
        public ValueTask<ToolAuthorizationResult> AuthorizeAsync(ToolAuthorizationRequest request, CancellationToken cancellationToken = default)
        {
            if (!string.Equals(request.TenantId, "denied-tenant", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("This handler only recognizes tenant 'denied-tenant'.");
            }

            return new ValueTask<ToolAuthorizationResult>(ToolAuthorizationResult.Deny("This tenant may not call this tool."));
        }
    }

    private sealed class TenantLockedHandlerFixture : ToolAuthorizationContract
    {
        protected override ValueTask<IToolAuthorizationHandler> CreateHandlerAsync() => new(new TenantLockedToolAuthorizationHandler());

        protected override ToolAuthorizationRequest DeniedRequest => new()
        {
            ToolName = "contract_tool",
            Effect = ToolEffect.Write,
            TenantId = "denied-tenant",
            RunId = Guid.NewGuid(),
            AgentName = "contract-agent",
        };

        protected override ToolAuthorizationRequest AllowedRequest => DeniedRequest with { TenantId = "allowed-tenant" };
    }

    /// <summary>
    /// Always denies, regardless of the request — the counterpart bug to
    /// "always allows": correctly denies <see cref="ToolAuthorizationContract.DeniedRequest"/>
    /// for the wrong reason (it denies everything), which only
    /// <see cref="ToolAuthorizationContract.Handler_receives_the_calling_tenant"/>
    /// catches, since it is the only fact that also exercises
    /// <see cref="ToolAuthorizationContract.AllowedRequest"/>.
    /// </summary>
    private sealed class AlwaysDenyToolAuthorizationHandler : IToolAuthorizationHandler
    {
        public ValueTask<ToolAuthorizationResult> AuthorizeAsync(ToolAuthorizationRequest request, CancellationToken cancellationToken = default)
            => new(ToolAuthorizationResult.Deny("Denied unconditionally."));
    }

    private sealed class AlwaysDenyHandlerFixture : ToolAuthorizationContract
    {
        protected override ValueTask<IToolAuthorizationHandler> CreateHandlerAsync() => new(new AlwaysDenyToolAuthorizationHandler());

        protected override ToolAuthorizationRequest DeniedRequest => new()
        {
            ToolName = "contract_tool",
            Effect = ToolEffect.Write,
            TenantId = "denied-tenant",
            RunId = Guid.NewGuid(),
            AgentName = "contract-agent",
        };

        protected override ToolAuthorizationRequest AllowedRequest => DeniedRequest with { TenantId = "allowed-tenant" };
    }
}
