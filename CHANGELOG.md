# Changelog

All notable changes to Tracon are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [Unreleased]

Nothing here has been published yet. Tracon is in development: no version
has been pushed to NuGet or npm, and there is no release tag. The entries below
describe what is on `main`. The first real release will get its own section,
fixed to the artifacts it actually ships, and carries the date it shipped on.

The public API is not frozen either: `PublicAPI.Shipped.txt` is empty in every
package, and the surface may still be reduced before 1.0.

### Added

- `RequireProductionProfile()` on the Tracon chain. `AddTracon()` brings every
  security-sensitive switch up permissive, so a deployment can reach production
  having never separated tenants, never decided who owns a session and never
  registered a content guard — in silence. This call refuses to start such a
  host. It **changes no setting and secures nothing by itself**: it sets no
  value, chooses no policy and turns nothing on; the only thing it does is turn
  a skipped decision into a startup failure. Six decisions are asked about —
  tenant separation, session ownership, at-rest content protection, content
  inspection, request rate limiting and retention — and each is either answered
  by turning the feature on or accepted by name with
  `profile.Accept(TraconProductionRisk.SingleTenant)`. There is no way to accept
  all six at once, and every accepted risk is logged at information level, by
  name, on each host start. It is off by default: a host that never calls it
  behaves exactly as before and resolves nothing extra at startup.
  **The set of decisions is a versioned contract**: a later release that adds
  one stops a host that calls this method until the new decision is answered or
  accepted, and such a release declares the addition as a behavioural breaking
  change naming the new risk and why it was added. `IProductionProfileCheck` is
  the seam behind it — more than one check may carry the same risk, and the
  strictest answer wins, which is how `Tracon.AspNetCore` adds the
  `UseTenancy(options => options.Enabled = false)` case the core cannot see.
- `Tracon:SessionOwnership:RefuseUnownedSessions` (default `false`).
  Session ownership is not retroactive, so rows written before it was turned
  on belong to nobody; until now those stayed readable by id to anyone in the
  tenant, and only disappeared from owner-filtered listings. With this on they
  are refused instead — `404` on the session and `/v1/conversations` routes,
  `403` (`errorType` `session_owner_required`) on a run that names one, `404`
  on a voice socket. A caller who satisfies `ManagementPolicy` still reads
  them, so support keeps the access it already had in the management listing;
  that exemption does not extend to starting a run. A session that does not
  exist yet is unaffected: the first turn still opens it and claims it.
- `TraconEndpointOptions.MapOpenAIConversations` (default `true`). Set it
  to `false` in `MapTracon` to leave the four `/v1/conversations` routes
  unmapped; they then answer `404` and disappear from the OpenAPI document.
  `/v1/responses` and `/v1/chat/completions` are unaffected.

### Changed

- **Licence.** Tracon now ships under the **PolyForm Small Business License
  1.0.0** instead of MIT. Use is free of charge for an individual, an open source
  project, and any company with fewer than 100 total individuals working as
  employees and independent contractors and less than 1,000,000 USD (2019,
  inflation adjusted) revenue in the prior tax year; above that threshold a
  commercial licence applies (hfarukatasoy@gmail.com). Three packages stay MIT so
  that writing an extension, proving it against the behaviour contracts, and
  owning the code `dotnet new` generates never need one: `Tracon.Abstractions`,
  `Tracon.Testing.Contracts.Xunit` and `Tracon.Templates`. The npm client
  `@tracon/client` follows its NuGet twin and is PolyForm. No package contains
  a licence key, an activation call or a feature gate, and the licence of a
  published version never changes. Because PolyForm is not OSI approved, packages
  now declare `<license type="file">` and carry the text inside the `.nupkg`
  rather than naming an SPDX expression. Nothing was published under MIT, so no
  existing consumer is affected. See
  <https://tracon.dev/reference/licensing/>.

- **The `/v1/conversations` read and delete routes now go through your
  registered `IRunAuthorizationHandler`.** `GET /v1/conversations/{id}`,
  `GET /v1/conversations/{id}/items` and `DELETE /v1/conversations/{id}` ask
  it with `SessionAccess.Read`, `Read` and `Delete`; they previously never
  asked it at all, while `/api/sessions/{id}` did. If you have a handler
  registered that refuses some sessions, it now refuses them on this surface
  too. The direction is fail-closed and it closes a real gap — an installation
  whose handler denied `GET /api/sessions/{id}` had
  `GET /v1/conversations/{id}/items` hand back the same chat history — but it
  is a behaviour change for existing setups. `POST /v1/conversations` is not
  gated: it reserves an identifier and writes nothing.

### Added — foundation

- Model provider adapters for OpenAI (Chat Completions and Responses),
  Anthropic (Claude), Google (Gemini), and Azure OpenAI, plus an
  `IModelProvider` extension point for any other provider — with per-tenant
  bring-your-own-key (BYOK) support and per-tenant egress policy.
- Persistence backends for PostgreSQL, SQL Server, and SQLite, each with
  embedded migrations; the runtime (`Tracon.Core`) needs no database.
- An embedded React console — 30 screens across 36 routes (Dashboard,
  Agents, Skills, Playground, Sessions, Runs, Workflows, Jobs, Evals,
  Experiments, Approvals, Tools, Models, MCP, Triggers, Audit, Diagnostics,
  Settings) — with zero JavaScript dependency in the consuming project.
- A management HTTP API alongside OpenAI-compatible endpoints
  (`/v1/responses`, `/v1/chat/completions`, `/v1/conversations`). Multi-tenancy
  is opt-in: `TraconTenancyOptions.Enabled` is `false` by default and
  every request resolves to the single default tenant until you turn it on.
- Run recording with spans, metrics, and cost; a tamper-evident audit trail
  with a verifiable hash chain; data subject export and erasure.
- Cost provenance: a priced run records the unit prices actually applied and
  the provider that answered it, so a historical cost stays explainable after
  the price list changes.
- Named job lanes: a job carries a lane, a worker subscribes to the lanes it
  serves, and each lane can hold its own concurrency budget — unrelated kinds
  of background work no longer block one another. Queue depth, throughput, and
  duration are reported as OpenTelemetry metrics.
- Opt-in structured-response validation: when an agent asks for JSON or a JSON
  schema, the response can be checked before the run closes, with a bounded
  repair turn that stays inside the same run's budget, cost accounting, and
  cancellation. A run that carries a durable session gets no repair budget:
  the underlying agent framework persists a turn as soon as that one model
  call completes, so a repaired run would return an answer its own session
  never recorded. Such a rejection fails the run exactly as it would with
  repair switched off, and its rejection event carries
  `repairSuppressedBySession` to tell that apart from a run that simply had no
  repair configured.
- Tool schemas generated from your method signatures express JSON Schema
  constraints taken from standard `System.ComponentModel.DataAnnotations`
  attributes, and support nested object and object-array parameters.
- Extension points for custom tools, model providers, run stores, run
  judges, agent sources, and scheduled job handlers, each with a published
  behavior-contract test suite (`Tracon.Testing.Contracts.Xunit`) so a
  third-party implementation can be verified against the same tests
  Tracon's own implementations run.
- Workflow execution with five orchestration patterns, checkpoints, resume,
  and human-in-the-loop approval.
- MCP tool discovery from remote servers, and agent exposure over MCP and
  A2A.
- A typed management client generated from the OpenAPI document, published
  both as a NuGet package (`Tracon.Client`) and an npm package
  (`@tracon/client`).
- A `dotnet new tracon-api` project template and an `tracon` global
  CLI tool (`migrate`, `migrate status`, `health`, `eval`).
- Native AOT and trimming compatibility for `Tracon.Abstractions`,
  `Tracon.Core`, `Tracon.PostgreSql`, `Tracon.OpenAI`,
  `Tracon.Anthropic`, `Tracon.Google`, `Tracon.Azure`, and
  `Tracon.Voice`.
