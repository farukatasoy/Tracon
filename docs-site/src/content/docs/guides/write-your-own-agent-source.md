---
title: Write your own agent source
description: Add agents from Git, another database, or an external runtime through IAgentSource without bypassing AgentPrism's catalog and run pipeline.
---

`IAgentSource` is the catalog extension point. Use it when agent definitions do not
live in the management database, or when an agent is backed by another runtime.
Register it through `AddAgentSource()`; the catalog keeps the normal decorator chain,
so recording, telemetry, and approval behavior still apply.

A complete, buildable definition source lives in
`samples/AgentPrism.Samples.CustomAgentSource` — it reads `AgentDefinition` JSON files
from a directory, uses only published packages (`PackageReference`, not a
project-internal type), and its test project runs the official contract suite plus a
real agent run. Read its source alongside this guide.

`AddAgentSource` has three overloads. The generic one resolves the source's own
constructor dependencies (including its own settings, registered separately) through DI:

```csharp
builder.Services.AddSingleton(new GitAgentSourceOptions { RepositoryUrl = url });
builder.AddAgentPrism()
       .AddAgentSource<GitAgentSource>();
```

Pass a ready instance, or a factory, when the source needs something that does not
belong in the container as its own singleton:

```csharp
builder.AddAgentPrism().AddAgentSource(sp => new GitAgentSource(repositoryPath, sp.GetRequiredService<AgentDefinitionCompiler>()));
```

All three register the source as a **singleton**; calling the generic overload twice
for the same type registers it once.

## Choose the source shape

| Shape | Use it when | Resolve behavior |
|---|---|---|
| Definition source | You own an `AgentDefinition` in Git, object storage, or another database | Call `AgentDefinitionCompiler.CompileCachedAsync()` |
| Agent source | You already own a runnable MAF `AIAgent` | Return the agent directly |

For a definition source, let the compiler own dependency fingerprints and the BYOK
cache bypass. Do not repeat that logic in your source.

```csharp
public async ValueTask<AIAgent?> ResolveAsync(string name, string? culture = null, CancellationToken ct = default)
{
    var definition = await _repository.GetAsync(name, ct);
    return definition is null
        ? null
        : await _compiler.CompileCachedAsync(definition, _cache, _tenants.TenantId, culture, ct);
}
```

## Source contract

Sources are singleton and calls can run concurrently. Keep no request or run state in
fields, and do not capture scoped services. `ListAsync()` is on the run path: it must
be repeatable and side-effect free. AgentPrism does not add a global snapshot, timeout,
retry, or circuit breaker for a source.

`ListAsync()` and `ResolveAsync()` must describe the same agent set: a name
`ResolveAsync()` resolves has to appear in `ListAsync()`, and vice versa. Return stable
descriptors and never mutate one — or its nested collections — after returning it. The
catalog defensively copies each descriptor at its own boundary too, but that is
belt-and-suspenders, not a license to violate the contract; a source that shares a
mutable list with the catalog is still a bug the copy merely contains.

Honor the `CancellationToken` you are given — AgentPrism adds no timeout of its own, so
an uncancellable source blocks its caller indefinitely. A genuine
`OperationCanceledException` must propagate; do not catch and convert it into anything
else.

A source can be global or tenant-aware; when it reads `ITenantContext`, any local cache
must include the tenant identity. Startup and background calls see the default tenant.

## Priority and management API

Smaller priority values win a name collision. `AgentSourcePriority.Code` (`0`) and
`AgentSourcePriority.Database` (`100`) are the values AgentPrism's own built-in sources
use, not values a custom source is barred from choosing. Pick `1` through `99` to run
between code and database definitions, or `101` or greater to run after the database.
Choosing `0` or `100` ties with the matching built-in source instead, and DI
registration order breaks the tie — the same rule as any other equal-priority pair.

Set custom descriptors to `AgentDefinitionOrigin.Custom`. They appear in the console
but are read-only: the management API cannot update or delete a definition that your
source owns.

## Prove the implementation

```csharp
using AgentPrism.Testing.Contracts.AgentSources;

public sealed class GitAgentSourceTests : AgentSourceContract
{
    protected override string KnownAgentName => "support";
    protected override ValueTask<IAgentSource> CreateSourceAsync()
        => new(new GitAgentSource(/* test repository and services */));
}
```

Derive `VersionedAgentSourceContract` when your source implements
`IVersionedAgentSource`. Derive `TenantAwareAgentSourceContract` when its list changes
by tenant. The suite checks repeatability, concurrency, cancellation, descriptor
integrity, and list/resolve consistency without a network call.

## Scale and operations

`GET /api/agents` is not paged. Resolution also scans the winning source's list, and
version resolution can list each source until it finds the owner. A source holding many
definitions should keep its own correctly invalidated cache. Source failures appear in
logs, diagnostics, and the `agentprism.agent_source.failures` metric. List failures are
isolated; resolution failures stop so a lower-priority agent cannot run by mistake.

## Read next

- [Agents](/concepts/agents/) — the catalog and agent-definition model
- [Model providers](/guides/model-providers/) — write a custom model provider
- [Reference: IAgentSource](/api/agentprism.iagentsource/) — full API contract
