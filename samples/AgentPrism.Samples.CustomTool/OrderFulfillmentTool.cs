using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace AgentPrism.Samples.CustomTool;

/// <summary>Submits an order through a dependency that is scoped per tool call.</summary>
/// <remarks>
/// Microsoft Agent Framework invokes a function with an empty service provider.
/// This tool therefore captures <see cref="IServiceScopeFactory"/> at registration
/// time and creates its own scope inside the function body. A scoped repository is
/// resolved inside that scope, not from the ambient function arguments.
/// </remarks>
public sealed class OrderFulfillmentTool(IServiceScopeFactory scopeFactory)
{
    private readonly IServiceScopeFactory _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));

    /// <summary>Creates the AIFunction registered by the host application.</summary>
    public AIFunction CreateFunction()
        => AIFunctionFactory.Create(
            (Func<string, CancellationToken, Task<OrderReceipt>>)SubmitOrderAsync,
            "submit_order",
            "Submits an order to fulfillment.");

    private async Task<OrderReceipt> SubmitOrderAsync(string orderId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orderId);

        using var scope = _scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IOrderRepository>();
        return await repository.SubmitAsync(orderId, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Persists an order submission for one tool invocation.</summary>
public interface IOrderRepository
{
    /// <summary>Submits the order and returns its fulfillment receipt.</summary>
    Task<OrderReceipt> SubmitAsync(string orderId, CancellationToken cancellationToken);
}

/// <summary>The result returned after a fulfillment submission.</summary>
public sealed record OrderReceipt(string OrderId, string Status);
