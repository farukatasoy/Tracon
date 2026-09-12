using Tracon.Testing.Contracts.Tools;

namespace Tracon.Core.UnitTests.Tools;

/// <summary>
/// A correct <see cref="IToolAuthorizationHandler"/>: denies exactly the
/// tenant its own policy names, allows everything else. Used as this
/// contract's own proof that a genuinely correct handler passes every
/// <see cref="ToolAuthorizationContract"/> scenario.
/// </summary>
internal sealed class ReferenceToolAuthorizationHandler : IToolAuthorizationHandler
{
    public ValueTask<ToolAuthorizationResult> AuthorizeAsync(ToolAuthorizationRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return new ValueTask<ToolAuthorizationResult>(
            string.Equals(request.TenantId, "denied-tenant", StringComparison.Ordinal)
                ? ToolAuthorizationResult.Deny("This tenant may not call this tool.")
                : ToolAuthorizationResult.Allow());
    }
}

public sealed class ToolAuthorizationContractTests : ToolAuthorizationContract
{
    protected override ValueTask<IToolAuthorizationHandler> CreateHandlerAsync() => new(new ReferenceToolAuthorizationHandler());

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
