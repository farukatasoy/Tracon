# Tracon.Testing.Contracts.Xunit

Behavior contract suites for Tracon's extension points — the store
interfaces (`IRunStore` and 29 others), `IModelProvider`, `IRunJudge`,
`IAgentSource`, `IJobHandler`, and code-defined custom tools — packaged as
xunit.v3 test base classes.

Tracon ships four store implementations (in-memory, PostgreSQL, SQL
Server, SQLite) and they all pass the same tests — the base classes in this
package. If you write your own store (a fifth SQL dialect, a document
database, a hand-rolled adapter over an existing system), derive one
contract class per interface you implement and inherit the same scenarios.

The same applies to a model provider or run judge Tracon ships no package for. Most
of what `IModelProvider` requires cannot be checked by a compiler — returning
an already-wrapped chat client, capturing a scoped service, rejecting a model
the catalog does not list — so deriving `ModelProviderContract` is how you
find out before a deployment does.

## Install

```bash
dotnet add package Tracon.Testing.Contracts.Xunit --prerelease
```

The package name says what it needs: xunit.v3 and Shouldly are ordinary
dependencies, not private ones — a consuming test project's runner discovers
and runs the `[Fact]` methods the contract classes inherit. There is no
NUnit or MSTest edition; a project on either framework cannot use this
package.

## Use

```csharp
using Tracon.Testing.Contracts.Storage;

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

### Stores — `Tracon.Testing.Contracts.Storage`

One abstract class per `Tracon.Abstractions` store interface:
`RunStoreContract`, `SessionStoreContract`, `AgentDefinitionStoreContract`,
`ExperimentStoreContract`, and 27 more. Every one derives from
`TenantIsolationContract<TStore>`, which supplies the shared lifecycle
plumbing (`InitializeAsync`/`DisposeAsync`) and the two-directional tenant
check every store must pass: a tenant reads its own records, and never
another tenant's.

`TestData` supplies ready-made sample records (`TestData.Run(...)`,
`TestData.Session(...)`, and so on) for writing additional scenarios beside
the inherited ones.

### Model providers — `Tracon.Testing.Contracts.Providers`

`ModelProviderContract` is what **every** provider owes. Fill in one member:

```csharp
using Tracon.Testing.Contracts.Providers;

public sealed class ContosoProviderTests : ModelProviderContract
{
    protected override ValueTask<IModelProvider> CreateProviderAsync()
        => new(new ContosoModelProvider("any-key"));
}
```

It asserts that the provider returns a **raw** chat client (the tool-call
loop, telemetry, the content guard and the circuit breaker belong to
Tracon, and building them inside a provider hides the tool-result turn
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

### Run judges — `Tracon.Testing.Contracts.Judges`

Derive `RunJudgeContract` for a judge that scores completed runs. It checks the
stable metric-safe name, the 0–100 score boundary, large `Reason` values,
repeat calls, concurrent calls, and a pre-cancelled token. The suite does not
require a judge to use cancellation because deterministic judges need no I/O.

```csharp
using Tracon.Testing.Contracts.Judges;

public sealed class ResponseQualityJudgeTests : RunJudgeContract
{
    protected override ValueTask<IRunJudge> CreateJudgeAsync()
        => new(new ResponseQualityJudge());
}
```

### Agent sources — `Tracon.Testing.Contracts.AgentSources`

Derive `AgentSourceContract` for an `IAgentSource`. It checks stable source metadata,
repeat and concurrent list/resolve behavior, listed-name consistency, and cancellation.
Derive `VersionedAgentSourceContract` only when the source implements
`IVersionedAgentSource`. Derive `TenantAwareAgentSourceContract` only when it changes
its list for the ambient tenant.

### Custom tools — `Tracon.Testing.Contracts.Tools`

Derive `CustomToolContract` for a code-defined tool registration. It checks the
registration's declared shape against what the tool actually returns when the
runtime invokes it. Derive `RepeatableToolContract` instead — it extends
`CustomToolContract` — only when the tool sets
`TraconToolRegistration.SafeToRepeat`, because that flag is a promise the
runtime acts on and the extra scenarios are what hold you to it.

Two more classes test your **own** implementations of Tracon's two tool
gates, not a tool itself:

```csharp
using Tracon.Testing.Contracts.Tools;
using Microsoft.Extensions.AI;

public sealed class MyValidatorTests : ToolArgumentValidationContract
{
    protected override ValueTask<AIFunction> CreateToolAsync() => new(MyTools.Search);

    protected override ValueTask<IToolArgumentsValidator> CreateValidatorAsync() => new(new MyValidator());
}
```

`ToolArgumentValidationContract` reads `Tool`'s own JSON Schema and fuzzes
`Validator` against it: a missing required field, a type mismatch, an
out-of-range number, a pattern violation, an unrecognized extra property, and
a poisoned call most likely to fault a naive validator's own code. Every
scenario accepts either a rejection or a thrown exception — Tracon's own
wrapper turns a thrown exception into a rejection (fail-closed), so both count
as the call being turned away. A scenario your tool's schema does not exercise
(no numeric bound, no pattern, a nested-object parameter this generator does
not model) is skipped with an explicit reason, not silently passed. Override
`ExtraPropertyIsRejected` to state whether your validator accepts an
undeclared property — Tracon does not mandate either way.

`ToolAuthorizationContract` tests your `IToolAuthorizationHandler` the same
way `CustomToolContract` tests a tool: you supply the ground truth. Authorization
is your own business policy — there is no schema to derive an "invalid" call
from — so you declare `DeniedRequest` and `AllowedRequest`, two calls your
handler's own policy decides oppositely. The contract checks the handler
actually reaches that denial (by returning it directly, or by throwing and
relying on Tracon's fail-closed wrapper), never runs the protected call
once denied, and genuinely bases its decision on the request it was given —
a handler that returns the same verdict regardless of who is calling cannot
pass by ignoring its input.

### Job handlers — `Tracon.Testing.Contracts.Scheduling`

Derive `JobHandlerContract` for an `IJobHandler`. Job delivery is
**at-least-once**: a lease can expire and hand the same items to your handler
again. The contract holds you to the two behaviors this requires — a
handler must not reprocess an item it already reported, and it must observe
cancellation between items.

```csharp
using Tracon.Testing.Contracts.Scheduling;

public sealed class NightlyReportHandlerTests : JobHandlerContract
{
    protected override ValueTask<IJobHandler> CreateHandlerAsync()
        => new(new NightlyReportHandler());

    protected override JobItemRecord CreateItem(int sequence, JobItemStatus status)
        => new()
        {
            Id = Guid.NewGuid(), JobId = JobId, Seq = sequence,
            Input = $"customer-{sequence}", Status = status,
        };
}
```

### Checking you derived them all

`ContractCoverage` reports contract classes your test assembly has no derived
type for, so a class added in a later release does not silently go
uncovered. Every call names one family:

```csharp
using Tracon.Testing.Contracts;

[Fact]
public void Every_provider_contract_has_a_derived_test()
    => ContractCoverage.MissingDerivedTypes(
        Assembly.GetExecutingAssembly(), ContractCoverage.ProviderContracts).ShouldBeEmpty();
```

One constant per family: `ContractCoverage.StorageContracts`,
`ProviderContracts`, `JudgeContracts`, `AgentSourceContracts`,
`ToolContracts`, and `SchedulingContracts`. A contract you deliberately do
not implement goes in the `except` argument, where a name that matches
nothing is itself reported — a stale exemption must not pass quietly.

## See also

- [Write your own store](https://tracon.dev/guides/write-your-own-store/) —
  a worked example, using this package against a from-scratch `IRunStore`.
- [`IRunStore` reference](https://tracon.dev/api/tracon.irunstore/) —
  the largest and most-documented contract; start there if you are writing a
  custom run store.
- [Model providers](https://tracon.dev/guides/model-providers/) —
  the runtime contract `ModelProviderContract` checks, stated in prose.
- [`IModelProvider` reference](https://tracon.dev/api/tracon.imodelprovider/) —
  the full contract: lifetime, threading, naming, catalog semantics,
  failure classification, and who owns the returned client.

Licence: MIT - deliberately permissive, so that proving your own implementation
correct never needs a commercial licence. It depends only on `Tracon.Abstractions`,
which is MIT for the same reason. Most Tracon packages are PolyForm Small Business
1.0.0. Details: <https://tracon.dev/reference/licensing/>
