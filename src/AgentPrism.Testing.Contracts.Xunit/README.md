# AgentPrism.Testing.Contracts.Xunit

Behavior contract suite for AgentPrism's store interfaces (`IRunStore` and
32 others), packaged as xunit.v3 test base classes.

AgentPrism ships four store implementations (in-memory, PostgreSQL, SQL
Server, SQLite) and they all pass the same tests — the base classes in this
package. If you write your own store (a fifth SQL dialect, a document
database, a hand-rolled adapter over an existing system), derive one
contract class per interface you implement and inherit the same scenarios.

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

One abstract class per `AgentPrism.Abstractions` store interface, all under
the `AgentPrism.Testing.Contracts.Storage` namespace: `RunStoreContract`,
`SessionStoreContract`, `AgentDefinitionStoreContract`,
`ExperimentStoreContract`, and 29 more. Every one derives from
`TenantIsolationContract<TStore>`, which supplies the shared lifecycle
plumbing (`InitializeAsync`/`DisposeAsync`) and the two-directional tenant
check every store must pass: a tenant reads its own records, and never
another tenant's.

`TestData` supplies ready-made sample records (`TestData.Run(...)`,
`TestData.Session(...)`, and so on) for writing additional scenarios beside
the inherited ones.

## See also

- [Write your own store](https://agentprism.doayen.web.tr/guides/write-your-own-store/) —
  a worked example, using this package against a from-scratch `IRunStore`.
- [`IRunStore` reference](https://agentprism.doayen.web.tr/api/agentprism.irunstore/) —
  the largest and most-documented contract; start there if you are writing a
  custom run store.
