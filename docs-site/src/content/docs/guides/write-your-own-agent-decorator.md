---
title: Write your own agent decorator
description: Wrap every resolved agent with custom cross-cutting behavior via IAgentDecorator.
---

`IAgentDecorator` wraps every agent the catalog resolves, the same mechanism
Tracon uses for run recording, telemetry, and the tool-approval gate. Use
it for cross-cutting behavior that has to see every agent — an audit trail,
a per-tenant rate limiter, a custom authorization check — rather than
repeating that logic inside each agent definition.

```csharp
public sealed class AuditingAgentDecorator(IAuditLog auditLog) : IAgentDecorator
{
    public int Order => 5;

    public AIAgent Decorate(AIAgent agent, AgentDescriptor descriptor)
        => new AuditingAgent(agent, descriptor.Name, auditLog);
}
```

`AddAgentDecorator` has three overloads, the same shape as `AddAgentSource` and
`AddRunJudge`. The generic one resolves the decorator's own constructor
dependencies through DI:

```csharp
builder.AddTracon()
       .AddAgentDecorator<AuditingAgentDecorator>();
```

Pass a ready instance, or a factory, when the decorator needs something that
does not belong in the container as its own singleton:

```csharp
builder.AddTracon()
       .AddAgentDecorator(sp => new AuditingAgentDecorator(sp.GetRequiredService<IAuditLog>()));
```

All three register the decorator as a **singleton** in the same
`IEnumerable<IAgentDecorator>` the catalog resolves; calling the generic
overload twice for the same type registers it once.

## Where it wraps

`Order` decides the position: the catalog sorts decorators by **descending**
`Order` and applies them in that sequence, so the **highest** value runs
first and ends up **innermost** — closest to the agent's own calls. Tracon's
own decorators use `0` (run recording, outermost), `10` (telemetry), and `20`
(tool approval). Place a decorator that has to observe or veto everything
below it at a low value; place one that has to sit close to the model call
itself at a high value.

A decorator must be thread-safe — the catalog can decorate agents
concurrently — and must not swallow a genuine `OperationCanceledException`. If
`Decorate` throws anything else, the catalog normalizes it into a
`TraconAgentSourceException` (the same shape a broken `IAgentSource`
produces) rather than letting a raw, unclassified error reach the caller.

A parameterized run compiled **outside** the catalog (bypassing
`CompiledAgentCache`) still needs the same pipeline; call
`AgentDecoratorPipeline.Apply(agent, descriptor, decorators)` by hand rather
than applying decorators one at a time — otherwise the agent runs
undecorated, with no run recording, no telemetry, and no tool-approval gate.

## How registration order decides who wins

`ITraconBuilder.Services` is the escape hatch for anything Tracon does
not model directly. Every Tracon service is registered with `TryAdd`, so
registration **order** decides who wins — and the answer is different
depending on whether the extension point allows one implementation or many.

| Seam shape | Examples | Registered **before** `AddTracon()` | Registered **after** `AddTracon()` |
|---|---|---|---|
| Single-instance | `ITenantContext`, `IProviderRetryClassifier`, `IRunErrorClassifier` | Your registration wins outright; only one registration remains | Also wins for a direct resolve, but Tracon's own registration is not removed — it stays behind as a second, unused entry |
| Multi-registration | `IAgentDecorator`, `IAgentSource`, `IRunJudge`, `IContentGuard`, `IModelProvider`, `IJobHandler` | Your registration joins the list alongside the built-in ones | Also joins the list — the built-in implementation keeps running too |

The dedicated `Add*()` methods (`AddAgentDecorator`, `AddAgentSource`,
`AddRunJudge`, `AddContentGuard`, `AddModelProvider`) exist for the
multi-registration seams precisely because getting this wrong there is
silent: a decorator you meant to *replace* the built-in one instead runs
*alongside* it, and both apply. A single-instance seam has no such trap — a
consumer's own `services.AddSingleton<IProviderRetryClassifier, T>()` simply
overrides the default, in either order — which is why those extension points
have no dedicated registration method: the plain escape hatch already behaves
the way you would expect.

```csharp
// Multi-registration seam: joins the list, does not replace anything.
builder.AddTracon()
       .AddAgentDecorator<AuditingAgentDecorator>();

// Single-instance seam: this line alone replaces the built-in classifier,
// whether it runs before or after AddTracon().
builder.Services.AddSingleton<IProviderRetryClassifier, AcmeRetryClassifier>();
```

## Read next

- [Runs and recording](/concepts/runs/) — the decorator that gives every run its record
- [Write your own agent source](/guides/write-your-own-agent-source/) — the sibling multi-registration seam for the catalog's data, not its decoration
- [Reference: IAgentDecorator](/api/tracon.iagentdecorator/) — full API contract
