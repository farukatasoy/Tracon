# Tracon.Abstractions

The contracts every other Tracon package is written against.

This package holds interfaces, records, and enums — no behaviour. It depends on no
model provider, no web framework, and no database, which is what lets a storage or
provider implementation live outside this repository without dragging the runtime
along with it.

**You normally do not install this package directly.** `Tracon.Core` already
brings it. Reference it on its own when you are writing an implementation of one of
the contracts below and want to depend on the seam rather than the runtime.

```bash
dotnet add package Tracon.Abstractions --prerelease
```

## What is in here

64 interfaces and roughly 280 public types. The ones a consumer actually implements
fall into four groups.

### Storage seams

Every one of them is implemented twice inside Tracon — once in memory
(`Tracon.Core`) and once per SQL provider — so a third implementation has two
working references.

| Interface | Holds |
|---|---|
| `IRunStore` | Run rows and their event streams |
| `IAgentDefinitionStore` | Agent definitions and their version history |
| `ISessionStore` | Sessions and their provider state |
| `IAttachmentStore` / `IAttachmentStorage` | Attachment descriptors and their bytes |
| `IJobStore` / `IJobScheduleStore` | The work queue and its cron schedules |
| `IEvalStore` / `IExperimentStore` | Eval suites, cases, runs; A/B experiments |
| `IAuditLog` | The append-only governance trail |
| `IQuotaStore` / `IRetentionPolicyStore` | Limits and cleanup policies |
| `IWebhookStore` / `IApiKeyStore` | Subscriptions and hashed API keys |
| `IVectorSearchStore` | Knowledge chunks and their embeddings |

Writing a third implementation of one of these? `Tracon.Testing.Contracts.Xunit`
packages the same behavior tests the shipped implementations run — see
[Write your own store](https://tracon.dev/guides/write-your-own-store/).

### Extension points

| Interface | Called when |
|---|---|
| `IAgentSource` | The catalog is built — this is how agents from a source of your own appear |
| `IModelProvider` | An agent needs an `IChatClient` |
| `IToolRegistry` | A definition's tool names are resolved |
| `IContentGuard` | Content enters or leaves a run and may be blocked |
| `IRunJudge` | A finished run is scored automatically |
| `IAgentDecorator` | An `AIAgent` is wrapped before it runs |
| `IJobHandler` | A job of your own kind is leased from the queue |

### Ambient context

`ITenantContext` is the one to know: nearly every store method takes a tenant id, and
this is where it comes from. In a single-tenant application the default
implementation returns a fixed value and nothing else changes.

### Identity and errors

`TraconId.NewId()` produces UUID version 7 — time-ordered, so rows written in
sequence stay clustered in an index instead of scattering the way version 4 does.
`TraconException` is the base of every exception the runtime raises for an
expected failure. Catch the base type to handle them all, or a derived one to react
to a specific case: `TraconCompilationException` (a definition does not compile),
`TraconProviderUnavailableException` (the circuit is open),
`TraconContentFilteredException` / `TraconContentBlockedException` (the
provider's filter versus your own guard — deliberately separate, because the
operator response differs), `TraconSessionConflictException`, and
`TraconExternalCallException`. Database driver exceptions are not wrapped.

## Compatibility

Targets `net8.0`, `net9.0`, and `net10.0`. Trimming- and AOT-compatible: no
reflection, no dynamic code, zero analyzer warnings.

## Stability

Tracon has not shipped a `1.0` yet, and these contracts are still moving. A
change here breaks every implementation of the seam it touches, so each one is
recorded in the repository's decision log before it is made.

## Links

- Full documentation: <https://tracon.dev>
- API reference: <https://tracon.dev/api/>

License: MIT - this package is deliberately permissive so that writing and testing an
extension never needs a commercial licence. Most Tracon packages are PolyForm
Small Business 1.0.0. Details: <https://tracon.dev/reference/licensing/>
