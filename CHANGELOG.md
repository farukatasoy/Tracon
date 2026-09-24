# Changelog

All notable changes to Tracon are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [Unreleased]

### Security

Every change below except the audit trail one can refuse a request or a
record that a running setup accepted before. Each refusal names what to
change.

- A stored record may name only a configuration key inside its own tenant.
  The allowed prefix is one per installation, so a record of one tenant could
  name another tenant's key and point it at an address it controls. A
  non-default tenant now names keys under `{prefix}{tenant}:` (for tenant
  `acme`, `Tracon:McpSecrets:acme:GithubToken`); a flat name directly under the
  prefix belongs to the default tenant, so a single-tenant installation changes
  nothing. The rule covers MCP servers, tenant provider bindings, webhook
  subscriptions and inbound triggers, where the record is saved (`400`) and
  where its value is resolved. Move an out-of-tenant name under the tenant's
  segment.
- `/api/tenants/{tenantId}/providers`, `/api/tenants/{tenantId}/egress` and the
  tenant records (`/api/tenants`) act on a tenant other than the caller's own
  only with platform authority: an API key that also carries `PlatformAdmin`,
  the static `AuthToken`, or — for a claims principal while multi-tenancy is
  on — the new `TraconPolicies.PlatformAdmin` policy. A missing registration of
  that policy denies (`403`). Before, a key bound to one tenant with
  `SecurityAdmin`, or a claims-based Admin of one tenant, could rewrite another
  tenant's provider binding.
- With `AllowRemoteAccess` on and neither `AuthToken` nor an authorization
  policy configured, an anonymous remote request now returns `401`; API keys
  keep working. A request that still carries `X-Forwarded-For`, `Forwarded` or
  `X-Real-IP` counts as remote, so a reverse proxy on the same machine no longer
  makes every caller look local. Without a token or a policy, an anonymous
  request with a non-loopback `Host` or `Origin` returns `403`, which blocks DNS
  rebinding and cross-site requests; the voice WebSocket follows the same rule.
- Creating an API key that carries the `PlatformAdmin` scope now needs the same
  platform authority as acting on another tenant. Before, a claims-based Admin
  of one tenant, without the `TraconPolicies.PlatformAdmin` policy, could create
  such a key and use it to reach other tenants. An API key can still only
  create scopes it carries itself.
- `GET /api/mcp-servers` and the `PUT /api/mcp-servers/{name}` response no
  longer return stored extra header values: every header keeps its name and
  carries the value `***`. Before, any `Reader` or `AgentsRead` API key could
  read a header value such as an API key and call the MCP server directly, past
  the tool approval gate. Webhook subscriptions (`GET /api/webhooks`,
  `GET /api/webhooks/{name}` and the `PUT` response) follow the same rule. The
  stored value does not change and is still sent to the target.
- The MCP server audit trail records header names only. The values of headers
  whose names the secret filter does not recognize, such as `Cookie` or
  `Ocp-Apim-Subscription-Key`, no longer enter the audit log as plain text.
- An `OperationCanceledException` that an `IRunAuthorizationHandler` throws,
  but that the request itself did not cause (for example, an `HttpClient`
  timeout inside the handler), is now a failed check. A single resource answers
  the same `404` as a missing one. Before, it answered `500`, which revealed
  that the resource exists.

### Fixed

- A slow or unresponsive database no longer blocks the metrics collection
  thread while the quota or queue-depth gauge refreshes. Before, this delayed
  the export of every other instrument of the meter provider.
- The quota threshold notification cache in `QuotaEnforcer` no longer grows for
  the life of the process. Entries of closed quota periods are now dropped.
- A failed or cancelled durable threshold claim no longer keeps a quota
  threshold notification silent until the end of the period. The next run in
  the same period retries the claim.
- The quota admission check, the threshold accounting, the quota gauge and
  `GET /api/quotas/usage` now read only the counters of the current periods.
  Before, every read returned the tenant's whole usage history, twice per run,
  and that history only grows.
- `GET /api/models/health` no longer fails for every provider when one
  provider's health check throws. The failing provider is reported `Unhealthy`
  with the exception type in `detail` (`The health check failed (...)`), and the
  exception is logged at Warning level. The background health refresh also
  continues past that provider.
- An Azure OpenAI `CredentialFactory` credential that throws while it gets a
  token (for example `DefaultAzureCredential` without an Azure sign-in) now
  reports `Unhealthy` with `Credential error (<exception type>)` and logs the
  exception, instead of escaping the health check.
- `UseAzureOpenAI(IConfiguration)` now binds a relative `Endpoint` and a
  `Models` entry without a `Name`, so start-up validation rejects them with the
  specific message, the same as the other three providers. Before, a relative
  endpoint was reported as missing and a nameless entry was silently dropped.
  An application that relied on the silent drop now fails at start-up.
- The out-of-catalog log entry of an `UseOpenAICompatible()` provider now names
  `OpenAIProviderOptions.Models` instead of the `Tracon:Providers:OpenAI`
  section, which that provider does not read.
- Two approval rules whose argument conditions differ are no longer merged
  into one by the SQL stores when a condition path carries the U+001E or
  U+001F control character. Before, saving the second rule returned the first
  one unchanged. Existing rules keep their stored fingerprint; no migration
  runs.
- The in-memory approval rule store now treats a list value that differs only
  in whitespace (`["eu", "us"]` and `["eu","us"]`) as the same condition, the
  same as the SQL stores. Before, it kept two rules.

### Removed

These public types are now `internal`. None is part of a seam you implement
or a call you make: the same behavior is reached through the public
registration methods, options, and interfaces. The API is not frozen in the
preview line, and the counts below are types, not members.

- `Tracon.PostgreSql`, `Tracon.SqlServer`, `Tracon.Sqlite`: `MigrationRunner`.
  It existed once in each provider package under the same name, so an
  application that referenced two providers could not name it (`CS0433`).
  Resolve `IMigrationApplier` to apply migrations and
  `ISqlPersistenceDiagnostics` for the migration snapshot; both resolve to the
  same runner. The `tracon migrate` command is unchanged.
- `Tracon.Anthropic`, `Tracon.Azure`, `Tracon.Google`, `Tracon.OpenAI`: the
  `*ChatClientFactory` and `*ModelCatalog` types, and `OpenAIApiSurface`. The
  `Use*` registration methods and the `*ProviderOptions` types are the
  configuration surface; a factory created through `FromClient` could not be
  plugged into a registered provider anyway.
- `Tracon.Abstractions` (11): `ChatHistoryState`, `ExperimentAssignment`,
  `JobPayload`, `ReplayToolMismatchException`, `RunAttributionReader`,
  `SafeErrorText`, `SchemaReadyGate`, `ToolArgumentConditionLimits`,
  `TraconSessionStateKeys`, `WebhookEventPayloadJsonContext`, and
  `WorkflowCheckpointState`. A store's checkpoint listing still returns an empty
  JSON object (`{}`) as `State`; the documentation of `IWorkflowCheckpointStore`
  now says so.
- `Tracon.Core` (69): `AgentCallGraph`, `AgentCallGraphProblem`,
  `AgentSessionIdentity`, `AttachmentTypeGuard`, `AttachmentUriReference`,
  `AttachmentValidationResult`, `AuditActorContext`, `AuditChainHasher`,
  `AuditChainWalker`, `AuditPayload`, `AuditRecorder`, `AuditSecretFilter`,
  `AuthorizingAIFunction`, `CanaryEvaluator`, `ChildRunApproval`,
  `CodeAgentRegistration`, `ConfigurationKeyGuard`, `CronExpression`,
  `DefaultProviderRetryClassifier`, `DocumentAttachmentSummary`,
  `DocumentChannelMessageBuilder`, `EgressAddressPolicy`,
  `EgressAddressValidator`, `EgressAddressVerdict`, `EgressSocketGuard`,
  `InMemoryWorkflowCheckpointStore`, `InboundTriggerDispatchResult`,
  `InboundTriggerOutcome`, `InboundTriggerRateLimiter`,
  `InboundTriggerSecretResolver`, `InboundTriggerValidatedRequest`,
  `InboundTriggerValidationResult`, `InstructionCultureResolver`,
  `InstructionParameterBinder`, `OnlineEvalSummaryService`,
  `ProviderCredentialClientCache`, `QuotaPeriodCalculator`,
  `ResolvedRetentionPolicy`, `RetentionExecutor`, `RetentionPolicyResolver`,
  `RunPromotionOutcome`, `RunPromotionStatus`, `RunReplayOutcome`,
  `RunReplayPreparation`, `SessionBranchOutcome`, `SessionBranchStatus`,
  `SkillScriptExecutionResult`, `SkillScriptNaming`, `StateGenerationCount`,
  `StatePreflight`, `StatePreflightReport`, `StateSampleFailure`, `TextChunker`,
  `TextTrimming`, `ToolApprovalPolicyRegistration`,
  `ToolApprovalPolicyRegistry`, `ToolApprovalRuleEvaluator`,
  `TraconEvalCheckRegistration`, `TruncatingAIFunction`, `ValidatingAIFunction`,
  `VoiceAudioFormats`, `VoiceConnectionLease`, `VoiceConnectionLimiter`,
  `VoiceConversationProtocol`, `VoiceConversationRequest`, `WebhookHttpClient`,
  `WebhookUrlValidator`, `WebhookUrlVerdict`, `WorkflowDefinitionValidator`.
- `Tracon.Voice`: `VoiceProviderNames`. The provider value stays the plain
  string `elevenlabs`.

### Changed

- Every Tracon package now depends on its Tracon siblings at exactly its own
  version (`[1.0.0-preview.3]`), not at that version or any later one.
  Upgrading one Tracon package and not the others now fails restore with
  `NU1107`, or warns `NU1608` when your project references the shared package
  directly.
- A host whose loaded Tracon package assemblies come from more than one release
  no longer starts. It stops before any hosted service starts, with a
  `TraconException` that lists each Tracon assembly and its version. Before, it
  started and failed later, at the first call into a member the other release
  no longer has. Every Tracon library package is compared except
  `Tracon.Client`, which talks to a server over HTTP and may call another
  release by design. The check has no setting to turn it off. A process that
  never starts a host (a bare `BuildServiceProvider()`, or a test project that
  runs the store contracts) does not run it.
- A project created with `dotnet new tracon-api` treats `NU1608` as an error, so
  such a graph stops at restore. The template's project file explains the line;
  remove it to keep `NU1608` a warning.
- Upgrade every Tracon package together, `Tracon.Testing.Contracts.Xunit`
  included. Measured against `1.0.0-preview.2`, these calls fail at run time
  when a `1.0.0-preview.2` package runs next to a newer sibling:
  `Tracon.AspNetCore` calls `TenantProviderCredentialResolver.ValidatePrefix`
  (a tenant provider binding `PUT`) and `InboundTriggerSecretResolver.ValidatePrefix`
  (a trigger save), which no longer exist (`MissingMethodException`), and
  `ConfigurationKeyGuard.RequirePrefix` (an MCP server or webhook save), which
  is no longer reachable (`MethodAccessException`); `Tracon.Mcp` calls
  `ConfigurationKeyGuard.RequirePrefix` when it opens an MCP transport; and
  `Tracon.Testing.Contracts.Xunit` calls `JobPayload.ExtractItems` (two
  `JobScheduleStoreContract` cases) and `WorkflowCheckpointState.IsOmitted` (one
  `WorkflowCheckpointStoreContract` case), which are now internal
  (`MethodAccessException`). The published `1.0.0-preview.1` and
  `1.0.0-preview.2` packages accept any newer sibling, so restore does not stop
  that graph: a host stops at start-up, and a test project that runs the store
  contracts without a host fails at those calls.
- `QuotaUsageQuery` has a new `PeriodStarts` filter: for each period, the first
  day whose counters to return, computed by the caller from the configured time
  zone. Without it, `IQuotaStore.GetUsageAsync` returns every period's counters,
  and its documentation now says so. A custom `IQuotaStore` must apply the
  filter; `QuotaStoreContract` has four new scenarios for it.
- An interval that drives a background timer now fails startup validation when
  it is outside 1 millisecond to about 49.7 days: `Approvals:ScanInterval`,
  `Canary:ScanInterval`, `RunReconciliation:HeartbeatInterval` and
  `ScanInterval`, `Scheduling:PollInterval`, `Health:BackgroundInterval` and
  `Mcp:RefreshInterval`. `Scheduling:LeaseDuration` may be at most about 99 days
  and `SingletonExecution:LeaseDuration` about 149 days, because each lease
  renews at a fraction of its length. Each of these checks applies only while
  its feature is on. `Retention:BatchDelay` may be zero and is always held to
  at most about 49.7 days. Before, such a value let the host report that it had started and then
  stopped it; approval expiration is on by default, so
  `Approvals:ScanInterval=00:00:00` did this to a default setup.
- The `tracon.quota.usage`, `tracon.quota.limit` and `tracon.job.queue.depth`
  gauges no longer read the database inside the metrics collection callback.
  When a gauge is enabled, a background service reads the values once at
  startup and then every `QuotaUsageRefreshInterval` /
  `JobQueueDepthRefreshInterval`. A collection only reads the last result. The
  first collection after startup can be empty, and a value can be up to one
  interval old.
- `EnableQuotaUsageGauge`, `EnableJobQueueDepthGauge` and their refresh
  intervals are now read at host start. Turning a gauge on, or changing its
  interval, takes effect at the next start.
- An enabled gauge whose refresh interval is outside 1 millisecond to about
  49.7 days now fails startup validation. Before, zero was accepted and meant
  a refresh on every collection.
- A `PUT /api/mcp-servers/{name}` or `PUT /api/webhooks/{name}` with a header
  value of `***` is rejected with `400`. A client that reads, edits and saves a
  record must send the real value of every header again.
- When an `IRunAuthorizationHandler` throws, the request is still denied
  (fail-closed), and Tracon now writes one `Error` log line in the
  `Tracon.RunAuthorization` category. The line holds the handler type, the
  access kind, the tenant, agent, run and session ids, and the exception. A
  `403` now says `The run authorization check failed. Retry the request.`
  instead of claiming that the handler denied the call. This covers run starts,
  the session list, the run, attachment and approval lists, replay, and
  workflow resume and respond. A `404` does not change. The exception text
  never enters the response.
- A session management policy (`TraconSessionOwnershipOptions.ManagementPolicy`)
  whose policy provider or requirement handler throws is now logged as an
  `Error` in the `Tracon.SessionOwnership` category, and the caller still gets
  the narrowed result. An unregistered policy writes no log line. An
  `IRunAttributionContext` that throws during an authorization check is logged
  once per request, and the caller is treated as having no identity.
- An `IAuthorizationPolicyProvider` that throws while `MapTracon` resolves a
  role policy is now logged as a `Warning` in the `Tracon.RolePolicies`
  category. The role is still treated as not registered. With
  `RequireRolePolicies` on, the startup exception carries the provider's
  exception as `InnerException`.
- `TenantProviderCredentialResolver` takes `IOptions<TraconOptions>` as a third
  constructor argument, and `ValidatePrefix(string)` is replaced by
  `ValidateKeyName(string tenantId, string configurationKeyName)`. The old
  name checked only the prefix; the rename keeps a caller from compiling
  against the weaker rule unchanged.
- `ToolApprovalRuleStoreContract` (in `Tracon.Testing.Contracts.Xunit`) has two
  new cases. A store of your own must keep two condition sets apart even when a
  path carries a control character, and must treat a list value that differs
  only in whitespace between its elements as the same condition. The identity
  rule is now written on `IToolApprovalRuleStore.AddAsync`. A store that compares
  a list value's raw JSON text fails the second case.
- The out-of-catalog log entry of the OpenAI, Anthropic, and Google providers
  now has one shared wording (`Use the <key> setting to add the model to the
  catalog.`). The Gemini health detail for an unparsable model list is now
  `The response is not valid JSON.`, the same as the other providers.

A version section is not written ahead of time. At tag time this heading is
renamed to the version and the date it shipped on, and a fresh empty
`## [Unreleased]` is opened above it, so a section always names artifacts that
actually exist. Until then the release rehearsal and the GitHub release body
both read the notes from here.

### Deprecated

- `QuotaUsageQuery.AsOf`. No store ever applied it, and a store cannot turn an
  instant into a period without the configured time zone. Use
  `QuotaUsageQuery.PeriodStarts`.
- The `net8.0` and `net9.0` targets. Microsoft support for .NET 8 and .NET 9
  ends on 2026-11-10, and the first Tracon release after that date drops both
  targets from every package; `net10.0` stays. An urgent security release may
  still carry them. Move an application that targets `net8.0` or `net9.0` to
  `net10.0` before you take that release.

## [1.0.0-preview.2] - 2026-09-20

The consumer-entry repair release. It keeps the same public API as preview.1
and corrects the package, documentation, and registry paths a new consumer
meets first.

### Fixed

- `Tracon.Templates` now stamps its own resolved package version into generated
  projects. `dotnet new tracon-api` therefore restores the published preview
  without a hidden `--TraconVersion` override. The override remains available
  and is now visible in template help.
- The root README agent-catalog example now calls the shipped `ResolveAsync`
  overload with its required culture and cancellation arguments.
- Package descriptions, package READMEs, API counts, target-framework claims,
  extension samples, and console screenshots now match the shipped artifacts.
- NuGet-rendered package documentation no longer relies on Mermaid rendering.

### Added

- `tracon --version` and `tracon -v` report the informational package version.
- The TypeScript client README documents all seven SSE operations and its
  exported `readSse`/`SseDecoder` helpers.
- Regression gates now verify the packed template's default version, rendered
  GitHub edit links, package README operation counts, shipped-language scope,
  and console screenshot freshness against UI source.

### Changed

- Preview npm releases use the `next` dist-tag, so install previews with
  `npm install @tracon/client@next`. npm does not let the `latest` tag be
  removed, and a package's first publish binds `latest` to it, so `latest`
  tracks the newest preview until the first stable release claims it.
- GitHub repository metadata, Discussions, preview release classification, and
  public documentation links now describe the published product.

## [1.0.0-preview.1] - 2026-09-20

The first published version: twenty packages on one version line.

The public API is not frozen: `PublicAPI.Shipped.txt` is empty in every
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
