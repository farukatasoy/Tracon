---
title: Write your own tool
description: Build a safe custom tool without bypassing AgentPrism's authorization, approval, timeout, and output boundaries.
---

Use `[AgentPrismTool]` with `AddGeneratedTools()` for a custom tool. It is the AOT-safe
path. Tool instances are singletons and can run concurrently for different tenants and
runs. Keep no mutable run state in fields.

```csharp
using System.ComponentModel;
using System.Text.Json.Serialization;

[JsonSerializable(typeof(OrderReceipt))]
internal partial class OrderToolJsonContext : JsonSerializerContext;

public sealed record OrderReceipt(string OrderId, string Status);

public static class OrderTools
{
    [AgentPrismTool(
        "submit_order",
        "Submits an order to the fulfillment system.",
        Effect = ToolEffect.External,
        RequiredPermission = "orders.submit",
        RequiresApproval = true,
        SafeToRepeat = true,
        TimeoutSeconds = 30,
        MaxOutputBytes = 4096,
        JsonSerializerContext = typeof(OrderToolJsonContext))]
    public static Task<OrderReceipt> SubmitOrderAsync(
        [Description("The identifier of the order to submit.")] string orderId,
        CancellationToken cancellationToken)
        => Task.FromResult(new OrderReceipt(orderId, "submitted"));
}
```

`[Description]` (`System.ComponentModel.DescriptionAttribute`) reaches the generated
JSON Schema as the parameter's `description` — the strongest signal the model has for
filling in that argument correctly. A parameter without one still compiles; the
generator reports it as a warning (`APG0009`).

**What the generator can express:** a parameter's scalar type (primitive types,
`string`, `Guid`, `DateTime`/`DateTimeOffset`, `enum`), an array of these,
`description`, and whether it is required. **What it cannot express, on any
parameter:** a nested object, or a `minimum`, `maximum`, length, or `pattern`
constraint. For a nested object parameter, register the tool by hand instead:

```csharp
public sealed record OrderFilter(string Status, int MinAmount);

[JsonSerializable(typeof(OrderFilter))]
internal partial class OrderFilterJsonContext : JsonSerializerContext;

agentPrism.Services.AddSingleton<AgentPrismToolRegistration>(provider =>
    new(AIFunctionFactory.Create(
        (OrderFilter filter) => SearchOrders(filter),
        "search_orders",
        "Searches orders matching a filter.",
        OrderFilterJsonContext.Default.Options)));
```

The source generator preserves all this metadata. For a complex result, declare its
`JsonSerializerContext` in your own source as shown above; Roslyn does not let one
source generator feed a context to another in the same compilation. The context makes
the result canonical JSON, so content guards, the output limit, run records, and the
model inspect the same data. Return `AIContent` only for the existing attachment
contract; attachments are not inline output and are not subject to `MaxOutputBytes`.

Do not resolve dependencies from `AIFunctionArguments.Services`: MAF supplies an empty
provider. Resolve singleton dependencies when you register an `AIFunction`. For scoped
work, inject `IServiceScopeFactory` into that registration and create a scope inside the
invocation.

```csharp
agentPrism.Services.AddSingleton<AgentPrismToolRegistration>(provider =>
{
    var scopes = provider.GetRequiredService<IServiceScopeFactory>();
    var function = AIFunctionFactory.Create(async (string orderId) =>
    {
        using var scope = scopes.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IOrderWriter>()
            .SubmitAsync(orderId);
    }, "submit_order", "Submits an order.");

    return new AgentPrismToolRegistration(
        function,
        requiresApproval: true,
        effect: ToolEffect.External,
        requiredPermission: "orders.submit",
        timeout: TimeSpan.FromSeconds(30),
        safeToRepeat: true,
        maxOutputBytes: 4096);
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

## Prove the registration

The `AgentPrism.Testing.Contracts.Xunit` package ships `CustomToolContract`.
Derive it in your test project to check the registration name, metadata, and concurrent
server-side invocation.

```csharp
using AgentPrism.Testing.Contracts;
using AgentPrism.Testing.Contracts.Tools;

public sealed class OrderToolTests : CustomToolContract
{
    protected override ValueTask<AgentPrismToolRegistration> CreateRegistrationAsync()
        => new(new AgentPrismToolRegistration(MyOrderTool));
}

[Fact]
public void All_tool_contracts_are_covered()
    => ContractCoverage.MissingDerivedTypes(
        typeof(OrderToolTests).Assembly,
        ContractCoverage.ToolContracts).ShouldBeEmpty();
```

## Read next

- [Tools, skills, and MCP](/concepts/tools/) — the governance model
- [Adding a tool](/getting-started/tools/) — the generated and reflection paths
