---
title: Write your own tool
description: Build a safe custom tool without bypassing AgentPrism's authorization, approval, timeout, and output boundaries.
---

Use `[AgentPrismTool]` with `AddGeneratedTools()` for a custom tool. It is the AOT-safe
path. Tool instances are singletons and can run concurrently for different tenants and
runs. Keep no mutable run state in fields.

```csharp
[AgentPrismTool(
    "submit_order",
    "Submits an order to the fulfillment system.",
    Effect = ToolEffect.External,
    RequiredPermission = "orders.submit",
    RequiresApproval = true,
    SafeToRepeat = true,
    TimeoutSeconds = 30,
    MaxOutputBytes = 4096)]
public static async Task<OrderReceipt> SubmitOrderAsync(
    string orderId,
    CancellationToken cancellationToken)
    => await Orders.SubmitAsync(orderId, cancellationToken);
```

The source generator preserves all this metadata. Complex results are emitted as JSON,
so content guards, the output limit, run records, and the model inspect the same data.
Return `AIContent` only for the existing attachment contract; attachments are not
inline output and are not subject to `MaxOutputBytes`.

Do not resolve dependencies from `AIFunctionArguments.Services`: MAF supplies an empty
provider. Resolve singleton dependencies when you register an `AIFunction`. For scoped
work, inject `IServiceScopeFactory` into that registration and create a scope inside the
invocation.

```csharp
var scopes = services.GetRequiredService<IServiceScopeFactory>();
agentPrism.AddTool(AIFunctionFactory.Create(async (string orderId) =>
{
    await using var scope = scopes.CreateAsyncScope();
    return await scope.ServiceProvider.GetRequiredService<IOrderWriter>().SubmitAsync(orderId);
}, "submit_order", "Submits an order."), options =>
{
    options.Effect = ToolEffect.External;
    options.RequiredPermission = "orders.submit";
    options.RequiresApproval = true;
    options.MaxOutputBytes = 4096;
});
```

`ToolRegistrationOptions` contains `RequiresApproval`, `Effect`,
`RequiredPermission`, `Timeout`, `SafeToRepeat`, `MaxOutputBytes`, and `Source`.
Code-defined tools normally leave `Source` unset.

Do not replace `IToolRegistry`. It is AgentPrism's immutable startup snapshot and owns
authorization, timeout, approval, and output truncation. Startup rejects an unverified
replacement unless `Tools.AllowUnverifiedToolRegistry` is explicitly enabled.

Timeout is not a forced abort: the model stops waiting, but a tool body can still have
started an external side effect. Make external calls idempotent. AgentPrism records and
streams controlled error text, but arguments and successful results can be persisted;
never return a secret.

## Read next

- [Tools, skills, and MCP](/concepts/tools/) — the governance model
- [Adding a tool](/getting-started/tools/) — the generated and reflection paths
