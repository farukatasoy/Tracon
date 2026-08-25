# AgentPrism.Testing.Contracts.Xunit

Behavior contract suites for AgentPrism's extension points — the store
interfaces (`IRunStore` and 32 others) and `IModelProvider` — packaged as
xunit.v3 test base classes.

AgentPrism ships four store implementations (in-memory, PostgreSQL, SQL
Server, SQLite) and they all pass the same tests — the base classes in this
package. If you write your own store (a fifth SQL dialect, a document
database, a hand-rolled adapter over an existing system), derive one
contract class per interface you implement and inherit the same scenarios.

The same applies to a model provider or run judge AgentPrism ships no package for. Most
of what `IModelProvider` requires cannot be checked by a compiler — returning
an already-wrapped chat client, capturing a scoped service, rejecting a model
the catalog does not list — so deriving `ModelProviderContract` is how you
find out before a deployment does.

## Install

```bash
dotnet add package AgentPrism.Testing.Contracts.Xunit
```

The package name says what it needs: xunit.v3 and Shouldly are ordinary
dependencies, not private ones — a consuming test project's runner discovers
and runs the `[Fact]` methods the contract classes inherit. There is no
NUnit or MSTest edition; a project on either framework cannot use this
package.

## Use

```csharp
using AgentPrism.Testing.Contracts.Storage;

public sealed class MyRunStoreTests : RunStoreContract
{
    protected override ValueTask<IRunStore> CreateStoreAsync()
        => new(new MyRunStore());
}
```

`dotnet test` then runs every scenario `RunStoreContract` defines —
idempotent `StartRunAsync`, tenant isolation (both directions), event
ordering, statistics aggregation, and more — against your implementation.
A method your store does not implement makes the inherited test fail with
your own exception; it does not fail to compile.

## What is covered

### Stores — `AgentPrism.Testing.Contracts.Storage`

One abstract class per `AgentPrism.Abstractions` store interface:
`RunStoreContract`, `SessionStoreContract`, `AgentDefinitionStoreContract`,
`ExperimentStoreContract`, and 29 more. Every one derives from
`TenantIsolationContract<TStore>`, which supplies the shared lifecycle
plumbing (`InitializeAsync`/`DisposeAsync`) and the two-directional tenant
check every store must pass: a tenant reads its own records, and never
another tenant's.

`TestData` supplies ready-made sample records (`TestData.Run(...)`,
`TestData.Session(...)`, and so on) for writing additional scenarios beside
the inherited ones.

### Model providers — `AgentPrism.Testing.Contracts.Providers`

`ModelProviderContract` is what **every** provider owes. Fill in one member:

```csharp
using AgentPrism.Testing.Contracts.Providers;

public sealed class ContosoProviderTests : ModelProviderContract
{
    protected override ValueTask<IModelProvider> CreateProviderAsync()
        => new(new ContosoModelProvider("any-key"));
}
```

It asserts that the provider returns a **raw** chat client (the tool-call
loop, telemetry, the content guard and the circuit breaker belong to
AgentPrism, and building them inside a provider hides the tool-result turn
from the guard), that `CreateChatClient` survives concurrent calls, that a
model absent from the catalog is not rejected, that a binding whose provider
name differs in casing still works, and that a client nobody disposes does
not break the provider.

Two more classes cover behavior that is a provider's *choice*. Derive them
only if you offer it — deriving is the statement of intent, which is why no
scenario in this package silently skips:

| Class | Derive it when |
|---|---|
| `ModelProviderCredentialContract` | The provider honors a per-tenant credential (BYOK) |
| `ModelProviderSettingsContract` | The provider reads `ModelBinding.ProviderSettings` |

`ModelProviderCredentialContract` also requires
`AssertCredentialIsApplied(IChatClient, ModelProviderCredential)`. Make that
assertion observe the provider request boundary, such as a recording transport
or SDK request factory. A different client object is not enough proof: it can
still send the setup-time key and bill the wrong tenant.

### Run judges — `AgentPrism.Testing.Contracts.Judges`

Derive `RunJudgeContract` for a judge that scores completed runs. It checks the
stable metric-safe name, the 0–100 score boundary, large `Reason` values,
repeat calls, concurrent calls, and a pre-cancelled token. The suite does not
require a judge to use cancellation because deterministic judges need no I/O.

```csharp
using AgentPrism.Testing.Contracts.Judges;

public sealed class ResponseQualityJudgeTests : RunJudgeContract
{
    protected override ValueTask<IRunJudge> CreateJudgeAsync()
        => new(new ResponseQualityJudge());
}
```

### Agent sources — `AgentPrism.Testing.Contracts.AgentSources`

Derive `AgentSourceContract` for an `IAgentSource`. It checks stable source metadata,
repeat and concurrent list/resolve behavior, listed-name consistency, and cancellation.
Derive `VersionedAgentSourceContract` only when the source implements
`IVersionedAgentSource`. Derive `TenantAwareAgentSourceContract` only when it changes
its list for the ambient tenant.

### Checking you derived them all

`ContractCoverage` reports contract classes your test assembly has no derived
type for, so a class added in a later release does not silently go
uncovered. Every call names one family:

```csharp
using AgentPrism.Testing.Contracts;

[Fact]
public void Every_provider_contract_has_a_derived_test()
    => ContractCoverage.MissingDerivedTypes(
        Assembly.GetExecutingAssembly(), ContractCoverage.ProviderContracts).ShouldBeEmpty();
```

Pass `ContractCoverage.StorageContracts` for stores. A contract you
deliberately do not implement goes in the `except` argument, where a name
that matches nothing is itself reported — a stale exemption must not pass
quietly.

## See also

- [Write your own store](https://agentprism.doayen.web.tr/guides/write-your-own-store/) —
  a worked example, using this package against a from-scratch `IRunStore`.
- [`IRunStore` reference](https://agentprism.doayen.web.tr/api/agentprism.irunstore/) —
  the largest and most-documented contract; start there if you are writing a
  custom run store.
- [Model providers](https://agentprism.doayen.web.tr/guides/model-providers/) —
  the runtime contract `ModelProviderContract` checks, stated in prose.
- [`IModelProvider` reference](https://agentprism.doayen.web.tr/api/agentprism.imodelprovider/) —
  the full contract: lifetime, threading, naming, catalog semantics,
  failure classification, and who owns the returned client.
