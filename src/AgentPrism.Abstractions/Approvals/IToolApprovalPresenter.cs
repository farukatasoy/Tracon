namespace AgentPrism;

/// <summary>
/// Resolves a human-readable presentation for a tool call that is about to publish an
/// approval request — turning, for example, <c>{ "orderId": "ORD-1001" }</c> into
/// "Cancel order for Priya Shah".
/// </summary>
/// <remarks>
/// <para>
/// <strong>Read-only and best-effort.</strong> This is not an authorization boundary —
/// <see cref="IToolAuthorizationHandler"/> and <see cref="IToolArgumentsValidator"/> are
/// the gates, and both fail closed. A presenter fails <em>open</em>: registered or not,
/// resolving or not, timing out or throwing, the approval request is published either
/// way. A presentation error must never suppress a request that needs a decision. The
/// raw arguments (<see cref="PendingApproval.Arguments"/>) are always available beside
/// whatever this method returns, so an operator can still decide even when it returns
/// <see langword="null"/>.
/// </para>
/// <para>
/// <strong>Register it as a singleton and resolve scoped dependencies through
/// <c>IServiceScopeFactory</c>.</strong> This method runs on the tool-call path, where
/// Microsoft Agent Framework hands every tool an empty service provider
/// (<c>AIFunctionArguments.Services</c> resolves nothing — see the framework notes on
/// <c>IToolAuthorizationHandler</c>). The same limit applies here: a scoped dependency,
/// such as a <c>DbContext</c>, must be resolved through an injected
/// <c>IServiceScopeFactory</c>, not a constructor parameter.
/// </para>
/// <para>
/// <strong>Tenant behavior — EXPECTED tenant.</strong> The tenant is carried by
/// <see cref="ToolApprovalContext.TenantId"/>, the same way the code-defined approval
/// policy callback (<c>IAgentPrismBuilder.AddToolApprovalPolicy</c>) reads it — never the
/// ambient tenant, since a presenter's own scoped lookup must never leak another tenant's
/// entity name into this one's approval card.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public sealed class OrderApprovalPresenter(IServiceScopeFactory scopes) : IToolApprovalPresenter
/// {
///     public async ValueTask&lt;ToolApprovalPresentation?&gt; PresentAsync(
///         ToolApprovalContext context, CancellationToken cancellationToken = default)
///     {
///         if (context.GetString("orderId") is not { } orderId)
///         {
///             return null;
///         }
///
///         using var scope = scopes.CreateScope();
///         var orders = scope.ServiceProvider.GetRequiredService&lt;IOrderRepository&gt;();
///         var customerName = await orders.GetAsync(orderId);
///
///         return new ToolApprovalPresentation
///         {
///             EntityType = "order",
///             EntityId = orderId,
///             EntityName = $"Order {orderId}",
///             Message = $"Cancel order {orderId} for {customerName}.",
///         };
///     }
/// }
/// </code>
/// </example>
public interface IToolApprovalPresenter
{
    /// <summary>Resolves a presentation for one pending tool call.</summary>
    /// <param name="context">The tool call the approval request was raised for.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// The presentation, or <see langword="null"/> when nothing could be resolved. A
    /// thrown exception or a call that runs past the configured timeout
    /// (<c>AgentPrismToolOptions.ApprovalPresentationTimeout</c>) is treated the same way.
    /// </returns>
    ValueTask<ToolApprovalPresentation?> PresentAsync(ToolApprovalContext context, CancellationToken cancellationToken = default);
}
