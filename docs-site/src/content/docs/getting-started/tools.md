---
title: Adding a tool
description: Register a tool so an agent can do something, and understand the one rule that trips people up.
sidebar:
  order: 3
---

An agent without tools can only talk. A tool is a method in your codebase that the
model may call.

## Register one

Mark the method and let the source generator find it:

```csharp title="Tools/OrderTools.cs"
using AgentPrism;

internal static class OrderTools
{
    /// <summary>Returns the shipping status of an order.</summary>
    /// <param name="orderId">The order number.</param>
    [AgentPrismTool("get_order_status", "Returns the shipping status of an order.")]
    public static string GetOrderStatus(string orderId)
        => $"Order {orderId} has shipped. Estimated delivery: 2 days.";
}
```

```csharp title="Program.cs"
builder.AddAgentPrism()
       .AddGeneratedTools();
```

`AddGeneratedTools()` is filled in at compile time — no reflection, no trimming or
AOT warning. A method without the attribute is not a tool, so adding a helper to the
class does not quietly expose it to a model.

Then name it on the agent:

```csharp
agentPrism.AddAgent(new AgentDefinition
{
    Name = "support",
    // …
    ToolNames = ["get_order_status"],
});
```

An unknown tool name fails at compile time for the agent — the definition does not
compile and the agent will not resolve. You find out when you save it, not on the
first run.

### The other two ways

```csharp
.AddTool(GetOrderStatus)      // from a delegate — uses reflection
.AddToolsFrom<OrderTools>()   // scans a type — uses reflection
```

Both work and both carry `[RequiresUnreferencedCode]` and `[RequiresDynamicCode]`.
The warning is handed to you rather than suppressed. Use `AddGeneratedTools()` unless
your tools live in an assembly you do not compile.

## The rule that trips people up

:::danger[A tool sees an empty service provider]
MAF hands a tool an **empty** `IServiceProvider` at call time. Resolving a dependency
inside a tool body fails — and it fails at run time, in a model call, not at startup.

Take dependencies at **registration**:

```csharp
// Wrong — services is empty when the model calls this
[AgentPrismTool("get_order_status", "…")]
public static string GetOrderStatus(string orderId, IServiceProvider services)
    => services.GetRequiredService<IOrderRepository>().Find(orderId).Status;

// Right — the dependency is captured when the tool is registered
var repository = app.Services.GetRequiredService<IOrderRepository>();
agentPrism.AddTool(AIFunctionFactory.Create(
    (string orderId) => repository.Find(orderId).Status,
    "get_order_status",
    "Returns the shipping status of an order."));
```

The same applies to instance-method tools picked up by scanning: the instance has to
come from somewhere, and at call time nothing can supply it.
:::

## Tools that need a human

Some tools should not run unattended:

```csharp
[AgentPrismTool("issue_refund", "Refunds an order.", RequiresApproval = true)]
public static string IssueRefund(string orderId) => /* … */;

// or, for a tool registered from a delegate:
agentPrism.AddTool(IssueRefund, requiresApproval: true);
```

The tool is wrapped in the registry — the single place where "an agent may only point
at a registered tool" is enforced, so there is no code path that skips the wrapper.
When the model calls it, the run pauses and an approval request appears in the
console. What happens next depends on how the run was started: a streaming run
carries the approval in its next turn, while a queued run stops at
`AwaitingApproval` and waits in the approval mailbox.

Standing decisions are possible too — an approval rule pre-approves a tool, or one
exact set of arguments, so it stops asking. Those rules are visible and revocable
under **Governance**.

## Try it

```bash
curl -N -X POST http://localhost:5081/agentprism/api/agents/support/run \
     -H 'Content-Type: application/json' \
     -d '{"message":"Where is order 4182?"}'
```

The stream carries `ToolInvoking` and `ToolInvoked` events around the model's reply,
and each call is written to the run with its arguments, its result, its duration, and
its error if it had one.

## Next

- [Persistence](/AgentPrism/getting-started/persistence/) — keep the history
- [Tools, skills, and MCP](/AgentPrism/concepts/tools/) — the whole picture, including
  the two exceptions to "code only"
