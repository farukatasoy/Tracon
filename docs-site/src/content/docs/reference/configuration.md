---
title: Configuration
description: Verified configuration sections and defaults for Tracon core services, providers, persistence, operations, security, and optional surfaces.
slug: reference/configuration
---

This page lists values that the current option types define. It does not infer a
default from provider behavior. `null`, empty, and off mean exactly that.

## Binding rules

`builder.AddTracon()` on an `IHostApplicationBuilder` reads the `Tracon`
section. Package extensions then register their capability and read or accept their
own options. A section in a file does not load a package by itself.

```csharp
var tracon = builder.AddTracon()
    .UseOpenAI(builder.Configuration.GetSection(OpenAIProviderOptions.SectionName))
    .UsePostgreSql(builder.Configuration.GetSection(TraconPostgreSqlOptions.SectionName))
    .UseWorkflows()
    .UseUI();
```

The configuration key separator is `:`. Environment variables use `__`, so
`Tracon:Providers:OpenAI:ApiKey` becomes
`Tracon__Providers__OpenAI__ApiKey`.

:::danger[Secrets do not belong in a settings file]
Provider keys, connection strings, bearer tokens, webhook secrets, and MCP
credentials must come from user-secrets, environment variables, or a secret manager.
Database records keep a configuration **key name**, never the secret value.
:::

```bash
dotnet user-secrets set "Tracon:Providers:OpenAI:ApiKey" "<value>"
dotnet user-secrets set "Tracon:PostgreSql:ConnectionString" "<value>"
```

## Section index

| Section | Option type | Registration that uses it |
|---|---|---|
| `Tracon` | `TraconOptions` | `AddTracon()` |
| `Tracon:Approvals` | `TraconApprovalOptions` | `AddTracon()` |
| `Tracon:AsyncRun` | `TraconAsyncRunOptions` | `AddTracon()` |
| `Tracon:Canary` | `CanaryOptions` | `AddTracon()` |
| `Tracon:ContentGuard` | `TraconContentGuardOptions` | Guard pipeline after a guard is registered |
| `Tracon:ContentGuard:Pattern` | `PatternContentGuardOptions` | Section presence or `AddPatternContentGuard()` |
| `Tracon:ContentProtection` | `TraconContentProtectionOptions` | `AddTracon()` binds it; `AddContentProtection()` makes it apply |
| `Tracon:Drain` | `TraconDrainOptions` | `AddTracon()` |
| `Tracon:Idempotency` | `TraconIdempotencyOptions` | `AddTracon()` and the HTTP layer |
| `Tracon:Knowledge` | `TraconKnowledgeOptions` | `AddTracon()`; PostgreSQL and embeddings make it functional |
| `Tracon:OnlineEvaluation` | `OnlineEvaluationOptions` | `AddTracon()` plus at least one judge |
| `Tracon:Quotas` | `TraconQuotaOptions` | `AddTracon()` |
| `Tracon:RateLimit` | `TraconRateLimitOptions` | `MapTracon()` |
| `Tracon:Retention` | `TraconRetentionOptions` | `AddTracon()` |
| `Tracon:RunContinuation` | `TraconRunContinuationOptions` | `AddTracon()` |
| `Tracon:RunReconciliation` | `RunReconciliationOptions` | `AddTracon()` |
| `Tracon:Scheduling` | `TraconSchedulingOptions` | `AddTracon()`; `UseScheduling()` can override from code |
| `Tracon:SingletonExecution` | `SingletonExecutionOptions` | `AddTracon()` |
| `Tracon:StructuredResponse` | `TraconStructuredResponseOptions` | `AddTracon()` |
| `Tracon:TenantProviders` | `TraconTenantProviderOptions` | `AddTracon()` |
| `Tracon:Webhooks` | `TraconWebhookOptions` | `AddTracon()` |
| `Tracon:Egress` | `TraconEgressOptions` | `AddTracon()` |
| `Tracon:Providers:OpenAI` | `OpenAIProviderOptions` | The configuration overload of `UseOpenAI()` |
| `Tracon:Providers:OpenAICompatible:{name}` | `OpenAIProviderOptions` shape | The configuration overload of `UseOpenAICompatible()` |
| `Tracon:Providers:Anthropic` | `AnthropicProviderOptions` | The configuration overload of `UseAnthropic()` |
| `Tracon:Providers:Google` | `GoogleProviderOptions` | The configuration overload of `UseGoogle()` |
| `Tracon:Providers:AzureOpenAI` | `AzureOpenAIProviderOptions` | The configuration overload of `UseAzureOpenAI()` |
| `Tracon:PostgreSql` | `TraconPostgreSqlOptions` | The configuration overload of `UsePostgreSql()` |
| `Tracon:SqlServer` | `TraconSqlServerOptions` | The configuration overload of `UseSqlServer()` |
| `Tracon:Sqlite` | `TraconSqliteOptions` | The configuration overload of `UseSqlite()` |
| `Tracon:Mcp` | `TraconMcpOptions` (connection) and `TraconMcpSecurityOptions` (key prefix) | `UseMcp()`; the prefix binds through `AddTracon()` |
| `Tracon:Workflows` | `TraconWorkflowOptions` | `UseWorkflows()` |
| `Tracon:Voice` | `VoiceOptions` | `UseVoice()` |
| `Tracon:Voice:Conversation` | `VoiceConversationOptions` | The configuration overload of `UseVoiceConversation()` |

`AddTracon()` binds `Skills:Scripts` values but does not register a script runner.
Call `UseSkillScripts()` to cross that execution boundary; the call sets `Enabled`
and registers the runner. A settings section or grant record alone cannot execute a
script.

## Core defaults under `Tracon`

<p class="reads-this">Read by <code>Tracon.Core</code> through <code>AddTracon()</code>. No extra package reference.</p>

### Identity, validation, and agent graphs

| Key relative to `Tracon` | Default | Meaning |
|---|---:|---|
| `DefaultTenantId` | `default` | Tenant used when no request resolver supplies one |
| `MaxParameterValueLength` | `4096` bytes | Largest UTF-8 size for one `AgentParameter` value in a run request or eval case |
| `Validation:McpTimeout` | 5 seconds | Fresh MCP lookup limit during definition validation |
| `AgentGraph:MaxDepth` | `3` | Largest child-agent call depth; root depth is zero |
| `AgentGraph:MaxTotalTokens` | `200000` | Token budget shared by the whole call tree; enforced between model turns, mid-run |
| `AgentGraph:MaxTotalCost` | none | Cost budget shared by the whole call tree; falls back to the token limit when a model's price is undefined |
| `AgentGraph:MaxTotalRuns` | `25` | Largest child-run count; the root does not count |
| `UtilityModel` | `null` | Optional model binding for compaction summarization |
| `Tools:DefaultTimeout` | 30 seconds | Longest one tool call may run when its own registration sets no timeout |
| `Tools:DefaultMaxOutputBytes` | `null` (unlimited) | UTF-8 byte limit for a tool result when its own registration sets none; must be at least 57 bytes when set |

Non-positive graph token, cost, or run limits remove that limit. See
[`TraconAgentGraphOptions`](/api/tracon.traconagentgraphoptions/)
for the exact runtime interpretation.

### Skills and script execution

| Key | Default |
|---|---:|
| `Skills:MaxSkillsPerAgent` | `10` |
| `Skills:MaxInstructionsLength` | `65536` bytes |
| `Skills:MaxResourceContentLength` | `262144` bytes per resource |
| `Skills:MaxResourcesPerSkill` | `20` |
| `Skills:Scripts:Enabled` | `false` |
| `Skills:Scripts:PlatformIsolationAcknowledged` | `false` |
| `Skills:Scripts:AllowStoredScripts` | `false` |
| `Skills:Scripts:SkillRoots` | Empty |
| `Skills:Scripts:Interpreters` | Empty; therefore no script can start |
| `Skills:Scripts:EnvironmentAllowList` | `PATH`, `HOME` |
| `Skills:Scripts:Timeout` | 30 seconds |
| `Skills:Scripts:MaxOutputBytes` | `262144` |
| `Skills:Scripts:MaxArgumentBytes` | `16384` |
| `Skills:Scripts:MaxScriptContentLength` | `65536` |
| `Skills:Scripts:MaxScriptsPerSkill` | `10` |
| `Skills:Scripts:MaxConcurrentPerTenant` | `2` |
| `Skills:Scripts:MaxConcurrentTotal` | `8` |
| `Skills:Scripts:SearchDepth` | `2` |

`Enabled=true` is invalid until `PlatformIsolationAcknowledged=true`. Tracon
does not provide a filesystem, network, CPU, memory, or privilege sandbox.

### Attachments and audit

| Key | Default |
|---|---|
| `Attachments:MaxBytes` | `20971520` bytes, or 20 MiB |
| `Attachments:AllowedMediaTypes` | `image/png`, `image/jpeg`, `image/webp`, `image/gif`, `application/pdf`, `text/plain`, `audio/*` |
| `Audit:ActorClaimType` | `null`; resolution tries name identifier, name, then `sub` |

The attachment guard checks file signatures. It does not trust the client's
`Content-Type` header.

### Provider resilience and health

| Key | Default |
|---|---:|
| `CircuitBreaker:Enabled` | `true` |
| `CircuitBreaker:FailureThreshold` | `5` consecutive failures |
| `CircuitBreaker:BreakDuration` | 30 seconds |
| `Health:CacheTtl` | 60 seconds |
| `Health:BackgroundInterval` | `null`; no background provider polling |

The on-demand model-health endpoint can still refresh a provider while background
polling is off.

### Model fallback and outgoing concurrency

| Key | Default |
|---|---:|
| `Preflight:Enabled` | `false` |
| `Preflight:ReserveRatio` | `0.2` |
| `ModelConcurrency:MaxConcurrentCallsPerProvider` | `null`, unlimited |

`Preflight` governs the inline pre-flight check on `POST /api/agents/{name}/run`;
`POST /api/agents/{name}/estimate` reports the same numbers regardless of this
flag. `ModelBinding.Fallbacks` (the fallback chain itself) is per-agent, defined
on the model binding, not a global setting. See
[Reliable runs](/guides/reliability/) and
[Model providers](/guides/model-providers/).

### Observability

| Key | Default |
|---|---:|
| `Observability:Enabled` | `true` |
| `Observability:PersistSpans` | `true` |
| `Observability:SuccessSampleRatio` | `0.1` |
| `Observability:AlwaysPersistFailures` | `true` |
| `Observability:MaxSpansPerRun` | `200` |
| `Observability:RecordSensitiveData` | `false` |
| `Observability:IncludeAgentVersionTag` | `true` |
| `Observability:EnableQuotaUsageGauge` | `false` |
| `Observability:QuotaUsageRefreshInterval` | 30 seconds |
| `Observability:EnableJobQueueDepthGauge` | `false` |
| `Observability:JobQueueDepthRefreshInterval` | 30 seconds |
| `Observability:MaxJobLaneCardinality` | `64` |

These settings control Tracon span creation and its own trace store. They do not
replace the application's OpenTelemetry exporter.

Both gauges are off by default because each one reads the database on a scrape;
the matching refresh interval caches those reads.
`MaxJobLaneCardinality` bounds how many distinct job lanes get a metric series of
their own before the rest are folded into a single `other` series — see
[observability](/guides/observability/#what-the-job-metrics-count).

### Run recording

| Key | Default |
|---|---:|
| `RunRecording:Enabled` | `true` |
| `RunRecording:RecordMessageDeltas` | `true` |
| `RunRecording:RecordToolPayloads` | `true` |
| `RunRecording:MaxPayloadLength` | `8192` characters |
| `RunRecording:RecordRunInput` | `true` |

Disable tool payloads when they can contain personal or regulated data. Replay needs
recorded run input. A store failure is logged and never blocks the agent response, so
recording is best-effort during a storage outage.

### Structured response validation

| Key | Default |
|---|---:|
| `StructuredResponse:Enabled` | `false` |
| `StructuredResponse:MaxRepairAttempts` | `0` |

Off by default: a response is never inspected after the model returns it. Applies only
to an agent whose `ResponseFormat.Kind` is `Json` or `JsonSchema`. See
[Validate the response](/guides/structured-output/#validate-the-response).

`MaxRepairAttempts` is off by default too: a value of `0` fails the run on the first
invalid response, exactly as when repair does not exist. A value of `2` permits at
most three model calls in total — the original turn plus two repairs — all inside the
same run, under the same budget. See
[Let the model repair a rejected response](/guides/structured-output/#let-the-model-repair-a-rejected-response).

### Pricing

Pricing has no built-in values. `Currency`, provider/model token prices, and voice
and image prices all start empty. The supported shape is:

```text
Tracon:Pricing:Currency
Tracon:Pricing:{provider}:{model}:Input
Tracon:Pricing:{provider}:{model}:Output
Tracon:Pricing:{provider}:{model}:CachedInput
Tracon:Pricing:Voice:{provider}:{model}:PerMillionCharacters
Tracon:Pricing:Voice:{provider}:{model}:PerMinute
Tracon:Pricing:Images:{provider}:{model}:PerImage
Tracon:Pricing:Images:{provider}:{model}:SizeMultipliers:{provider-size}
Tracon:Pricing:Images:{provider}:{model}:OutputCostPerMillionTokens
```

Values label and calculate reports only. Tracon performs no currency conversion.
The shorter configuration keys `Input`, `Output` and `CachedInput` bind to the code
properties `InputCostPerMillionTokens`, `OutputCostPerMillionTokens` and
`CachedInputCostPerMillionTokens`.

`CachedInput` is the rate for input tokens the provider served from its prompt
cache. It is optional, and its absence is not a configuration error — unlike
`Input` and `Output`, where an entry with neither is rejected at startup as a
likely typo.

Cost is computed by **subtraction**, because a provider counts cached tokens
*inside* the input total rather than beside it:

```text
full_price_input = InputTokens - CachedInputTokens
cost             = full_price_input   * Input
                 + CachedInputTokens  * CachedInput
                 + OutputTokens       * Output
```

:::note
Leave `CachedInput` unset and the whole input is priced at `Input`, exactly as
before the rate existed. A missing cache rate never makes a run's `pricingSource`
`Unknown` — that value means the **model** price is missing, which is a different
fault. Set it to `0` to state that cache reads are free.
:::

Reasoning tokens are recorded (`reasoningTokens`) but priced at the output rate:
providers do not bill them separately today, and an unmeasured distinction is not
worth widening the price schema.

An image model uses either `PerImage` or `OutputCostPerMillionTokens`, never both.
`SizeMultipliers` applies only to a per-image price and uses the exact provider size
string, for example `1024x1024`. An image response with no matching configured price
records its real quantity but leaves `cost` as `null`; Tracon does not estimate
image prices.

### Images

`Tracon:Images` is off by default (`false`).<!-- claim:option TraconImageOptions.Enabled=false --> When `Enabled` is `true`, `Provider` and
`Model` are required and `MaxImagesPerRequest` must be at least one. Enabling it adds
the `generate_image` tool and maps `POST /api/images/generate`.

`Timeout` bounds one `generate_image` call and defaults to `00:02:00`,<!-- claim:option TraconImageOptions.Timeout=00:02:00 -->
**not** the 30 second `Tracon:Tools:DefaultTimeout` every other tool inherits. Image
generation is slower than the tool the generic default was chosen for: a measured
`gpt-image-1` request routinely needs 30 to 35 seconds, so the generic bound cut off
a call that was about to succeed. Shorten it if you would rather fail fast — a call
that outlives it is cancelled, and if the provider finishes anyway the spend is still
recorded against the original call.

```json
{
  "Tracon": {
    "Images": {
      "Enabled": true,
      "Provider": "openai",
      "Model": "gpt-image-1",
      "MaxImagesPerRequest": 1,
      "Timeout": "00:02:00"
    },
    "Pricing": {
      "Currency": "USD",
      "Images": {
        "openai": {
          "gpt-image-1": {
            "PerImage": 0.04,
            "SizeMultipliers": { "1024x1024": 2.0 }
          }
        }
      }
    }
  }
}
```

Provider extensions set `Provider` when it is empty. Set it explicitly when the host
registers more than one image provider or a custom keyed generator.

## Operational sections

<p class="reads-this">Read by <code>Tracon.Core</code>. The lease-backed entries coordinate more than one process only with a SQL persistence package.</p>

### Scheduling, async runs, leases, and recovery

| Section and key | Default |
|---|---:|
| `Scheduling:Enabled` | `true` |
| `Scheduling:RunWorker` | `true`<!-- claim:option TraconSchedulingOptions.RunWorker=true --> |
| `Scheduling:MaxConcurrentJobs` | `2` |
| `Scheduling:PollInterval` | 10 seconds |
| `Scheduling:LeaseDuration` | 5 minutes |
| `Scheduling:MaxAttempts` | `3` |
| `Scheduling:MaxItemsPerJob` | `1000` |
| `Scheduling:Lanes` | `null`; leases from every lane |
| `Scheduling:MaxConcurrentJobsPerLane` | empty; a lane not listed shares `MaxConcurrentJobs` |
| `Scheduling:LaneByHandlerKey` | empty; maps a handler key to a lane when the caller left it unset |
| `Scheduling:HttpSchedulableHandlerKeys` | empty; the handler keys `PUT /api/schedules/{name}` accepts. Empty means the built-in keys; a non-empty list replaces that default rather than extending it |
| `AsyncRun:Enabled` | `true` |
| `AsyncRun:MaxAttempts` | `1` |
| `SingletonExecution:Enabled` | `false` |
| `SingletonExecution:LeaseDuration` | 60 seconds; at least 3 seconds while `SingletonExecution:Enabled` is `true` |
| `SingletonExecution:OwnerId` | `null`; generated by the runtime |
| `RunReconciliation:Enabled` | `false` |
| `RunReconciliation:HeartbeatInterval` | 30 seconds |
| `RunReconciliation:OrphanThreshold` | 5 minutes |
| `RunReconciliation:ScanInterval` | 1 minute |
| `RunReconciliation:MaxRunsPerScan` | `100` |
| `RunContinuation:Enabled` | `false`; has no effect unless `RunReconciliation:Enabled` is also `true` |
| `RunContinuation:MaxAttempts` | `1` |
| `Drain:Enabled` | `false` |
| `Drain:Timeout` | 30 seconds |

`UseScheduling()` changes worker settings from code; it does not create a second
queue. `AddTracon()` already registers the core job contracts.

### Approvals, evaluation, and canaries

| Section and key | Default |
|---|---:|
| `Approvals:DefaultExpiration` | 24 hours |
| `Approvals:ExpirationEnabled` | `true` |
| `Approvals:ScanInterval` | 1 minute |
| `Approvals:MaxPerScan` | `100` |
| `OnlineEvaluation:Enabled` | `false` |
| `OnlineEvaluation:SampleRate` | `0` |
| `OnlineEvaluation:MaxScoresPerHour` | `100` |
| `OnlineEvaluation:AgentNames` | Empty; all agents are eligible |
| `OnlineEvaluation:LowScoreThreshold` | `60` |
| `OnlineEvaluation:MinSampleSize` | `20` |
| `OnlineEvaluation:JudgeTimeout` | 60 seconds per judge call |
| `OnlineEvaluation:EvaluationWindow` | 1 hour |
| `Canary:AutoRollbackEnabled` | `false` |
| `Canary:ScanInterval` | 5 minutes |

Online evaluation needs both `Enabled=true`, a positive sample rate, and a registered
`IRunJudge`. `AddModelRunJudge()` registers the built-in model-backed judge but does
not enable sampling.

### Admission, guards, and knowledge

| Section and key | Default |
|---|---:|
| `Quotas:Enabled` | `true`; the rule set starts empty |
| `Quotas:TimeZone` | `UTC` |
| `Quotas:ThresholdPercents` | `80`, `100` |
| `Quotas:AllowOnStoreFailure` | `true` |
| `Quotas:PublishThresholdToRunStream` | `false` |
| `RateLimit:Enabled` | `false` |
| `RateLimit:PermitLimit` | `60` |
| `RateLimit:Window` | 1 minute |
| `RateLimit:QueueLimit` | `0` |
| `RateLimit:Partition` | `Tenant` |
| `Idempotency:Enabled` | `true` |
| `Idempotency:MaxKeyLength` | `255` |
| `ContentGuard:InspectInput` | `true` |
| `ContentGuard:InspectOutput` | `true` |
| `ContentGuard:BufferStreamingOutput` | `true` |
| `ContentGuard:Pattern:DeniedTerms` | Empty |
| `ContentGuard:Pattern:MaskedPii` | `None` |
| `ContentGuard:Pattern:MaskReplacement` | `[redacted]` |
| `Knowledge:Dimensions` | `1536` |
| `Knowledge:ChunkSize` | `1000` characters |
| `Knowledge:ChunkOverlap` | `100` characters |
| `Knowledge:MaxResults` | `5` |

Quota enforcement with no stored rule rejects nothing. Pattern settings do no work
until the built-in guard is registered; a present Pattern section is itself a
registration signal. Knowledge stays unavailable until both the vector store and an
embedding generator exist.

### Outbound network targets

| Key relative to `Tracon:Egress` | Default |
|---|---:|
| `AllowPrivateNetworkTargets` | `false`<!-- claim:option TraconEgressOptions.AllowPrivateNetworkTargets=false --> |

Covers all three surfaces that reach the network: webhook delivery, MCP server
connections, and per-tenant model provider endpoints. While it is off, a target that
resolves to a private network address is refused — at save time for an address written
as an IP literal, and on every connection for one written as a host name. See
[Security](/getting-started/security/#outbound-requests-are-guarded-too).

### Webhooks

| Key relative to `Tracon:Webhooks` | Default |
|---|---:|
| `Enabled` | `true` |
| `AllowPrivateNetworkTargets` | `false` |
| `AllowInsecureHttp` | `false` |
| `AllowedConfigurationPrefix` | `Tracon:WebhookSecrets:` |
| `MaxExtraHeaders` | `20` |
| `Timeout` | 10 seconds |
| `MaxResponseBytes` | `8192` |
| `DisableAfterConsecutiveFailures` | `20` |
| `RetryDelays` | 1 minute, 5 minutes, 30 minutes, 2 hours, 6 hours |
| `SignatureTolerance` | 5 minutes |

No request is sent while no subscription exists. Private targets, insecure HTTP, and
redirects remain blocked by default.

`AllowedConfigurationPrefix` bounds which configuration key a subscription may name as
its signing secret; a name outside it is refused both when the subscription is saved
and when the secret is resolved. `MaxExtraHeaders` bounds a subscription's own extra
headers — headers whose name Tracon sets itself are always dropped, whatever the
limit is. `AllowPrivateNetworkTargets` here applies to webhook delivery only; the
shared `Tracon:Egress` setting covers this surface too, and either one being on is
enough.

### At-rest content protection

| Key relative to `Tracon:ContentProtection` | Default |
|---|---:|
| `Enabled` | `false` |
| `ActiveKeyId` | (none) |
| `Keys` | Empty |
| `Columns` | All ten protected columns |

Encryption is off until both `AddContentProtection(...)` is called and `Enabled` is
`true`. `Keys` maps a key id to the **name** of another configuration key — never to
the key's raw value — the same indirection
[other configuration keys](/getting-started/security/#configuration-keys-are-fenced-too)
use elsewhere. See
[at-rest content protection](/getting-started/security/#at-rest-content-protection)
for the full key setup and its limits.

## Retention defaults

<p class="reads-this">Read by <code>Tracon.Core</code>. Deletion needs a persistence package; the in-memory stores have nothing to retain across a restart.</p>

`Tracon:Retention:Enabled` defaults to `false`<!-- claim:option TraconRetentionOptions.Enabled=false -->. `BatchSize` defaults to `5000`
and `BatchDelay` to 100 ms. The following ages become configuration-based defaults
only after retention is enabled. A database policy for a target takes precedence.

| Child key | Default `MaxAgeDays` | `Archive` default |
|---|---:|---:|
| `RunEvents` | 30 days | `false` |
| `ToolInvocations` | 90 days | `false` |
| `Spans` | 14 days | `false` |
| `Jobs` | 30 days; completed jobs only | `false` |
| `WebhookDeliveries` | 7 days; delivered rows only | `false` |
| `EvalCaseResults` | 180 days | `false` |
| `WorkflowCheckpoints` | 7 days after completion | `false` |
| `SkillScriptGrants` | 30 days after expiry or revocation | `false` |
| `Attachments` | 7 days for orphaned rows | `false` |
| `Sessions` | Disabled, `null` | `false` |
| `Conversations` | Disabled, `null` | `false` |
| `IdempotencyKeys` | 1 day | `false` |
| `RunInputs` | 30 days | `false` |
| `VoiceSessions` | 30 days | `false` |
| `RunScores` | 180 days | `false` |
| `DocumentEmbeddings` | Disabled, `null` | `false` |

`IdempotencyKeys` is not a bindable child section. Its one-day option default still
applies when configuration defaults are enabled. Change that target through
`Configure(TraconOptions)` or a stored retention policy.

## Persistence sections

<p class="reads-this">Read by <code>Tracon.PostgreSql</code>, <code>Tracon.SqlServer</code>, and <code>Tracon.Sqlite</code>. A section without its package does nothing.</p>

| Section | Key | Default |
|---|---|---|
| `Tracon:PostgreSql` | `ConnectionString` | `null`; required by the configuration registration |
|  | `SchemaName` | `tracon` |
|  | `AutoApplyMigrations` | `true` |
|  | `CommandTimeoutSeconds` | `30` |
| `Tracon:SqlServer` | `ConnectionString` | `null`; required by the configuration registration |
|  | `SchemaName` | `tracon` |
|  | `AutoApplyMigrations` | `true` |
|  | `CommandTimeoutSeconds` | `30` |
| `Tracon:Sqlite` | `ConnectionString` | `null`; required by the configuration registration |
|  | `TablePrefix` | `tracon_` |
|  | `AutoApplyMigrations` | `true` |
|  | `CommandTimeoutSeconds` | `30` |

SQLite rejects bare `Data Source=:memory:`. Use a shared in-memory URI when a file is
not suitable. PostgreSQL and SQL Server use a schema; SQLite uses a table prefix.

## Provider sections

<p class="reads-this">Read by <code>Tracon.OpenAI</code>, <code>Tracon.Anthropic</code>, <code>Tracon.Google</code>, and <code>Tracon.AzureOpenAI</code>, each through its own <code>Use*()</code> call.</p>

All provider model lists start empty. All nullable request timeouts use the provider
library default.

| Section | Verified defaults |
|---|---|
| `Tracon:Providers:OpenAI` | `ApiKey=null`, `DefaultModel=null`, `Endpoint=null` for OpenAI, `Organization=null`, `Timeout=null`, `Models=[]` |
| `Tracon:Providers:OpenAICompatible:{name}` | Same shape as OpenAI; `EnableResponsesSurface=false` |
| `Tracon:Providers:Anthropic` | `ApiKey=null`, `DefaultModel=null`, `Endpoint=null` for Anthropic, `DefaultMaxOutputTokens=4096`, `Timeout=null`, `MaxRetries=null`, `Models=[]` |
| `Tracon:Providers:Google` | `ApiKey=null`, `DefaultModel=null`, `Endpoint=null` for Google, `ApiVersion=null`, `Timeout=null`, `Models=[]` |
| `Tracon:Providers:AzureOpenAI` | `Endpoint=null` but required, `ApiKey=null`, `CredentialFactory=null`, `DefaultDeployment=null`, `Audience=null` for public cloud, `Timeout=null`, `Models=[]` |

`CredentialFactory` is code-only because it is a delegate. When it is set, Azure does
not use `ApiKey`. The consumer chooses and references `Azure.Identity` when managed
identity is required.

| `Tracon:TenantProviders` | `AllowedConfigurationPrefix="Tracon:ProviderKeys:"` |

A tenant provider binding's configuration key name must start with
`AllowedConfigurationPrefix`; a name outside it is rejected with `400`, both when the
binding is saved and again when it is resolved. See
[Per-tenant credentials](/guides/model-providers/#per-tenant-credentials-byok).

## MCP, workflows, and voice

<p class="reads-this">Read by <code>Tracon.Mcp</code>, <code>Tracon.Workflows</code>, and <code>Tracon.Voice</code>. Live conversation is the exception: it is in <code>Tracon.Core</code>, behind <code>UseVoiceConversation()</code>.</p>

### MCP client

| Key relative to `Tracon:Mcp` | Default |
|---|---:|
| `Enabled` | `true` |
| `RefreshInterval` | 5 minutes |
| `ConnectionTimeout` | 30 seconds |
| `MaxToolsPerServer` | `100` |
| `MaxResourceBytesPerResource` | `65536` |
| `MaxResourceBytesTotal` | `262144` |
| `OAuthCallbackBaseUri` | `null` |
| `AllowedConfigurationPrefix` | `Tracon:McpSecrets:` |

`AllowedConfigurationPrefix` belongs to `TraconMcpSecurityOptions` rather than
`TraconMcpOptions`, and binds through `AddTracon()` — the rule is enforced
both where a server definition is saved and where its key is resolved, and those two
live in packages that do not reference each other. The section name is the same, so
it stays one section to configure. It bounds both `authorizationConfigurationKey` and
`oauthClientSecretConfigurationKey`.

The remaining keys belong to `TraconMcpOptions`. `UseMcp()` without arguments uses
those defaults but does not read `IConfiguration` implicitly. Use the explicit overload when you want the section:

```csharp
tracon.UseMcp(
    builder.Configuration.GetSection(TraconMcpOptions.SectionName),
    configure: null);
```

### Workflows

| Key relative to `Tracon:Workflows` | Default |
|---|---:|
| `Enabled` | `true` |
| `EnableCheckpointing` | `true` |
| `MaxConcurrentRuns` | `4` |
| `RunTimeout` | 10 minutes |
| `MaxSuperSteps` | `100` |
| `KeepCheckpointsAfterCompletion` | `true` |

The section does not register the execution engine. Call `UseWorkflows()`; that call
binds this section and then applies its optional code override.

### Speech tools

| Key relative to `Tracon:Voice` | Default |
|---|---:|
| `Provider` | `elevenlabs` |
| `ApiKey` | `null` |
| `Endpoint` | `null`; provider public endpoint |
| `DefaultVoiceId` | `null` |
| `SynthesisModelId` | `null` |
| `TranscriptionModelId` | `null` |
| `OutputFormat` | `mp3_44100_128` |
| `MaxCharactersPerRequest` | `5000` |
| `MaxConcurrentRequests` | `2` |
| `RequireApproval` | `false` |
| `Timeout` | `null`; implementation uses 100 seconds |

### Live voice conversation

| Key relative to `Tracon:Voice:Conversation` | Default |
|---|---:|
| `MaxConcurrentConnectionsPerTenant` | `5` |
| `MaxConnectionDuration` | 30 minutes |
| `IdleTimeout` | 2 minutes |
| `MaxUtteranceDuration` | 60 seconds |
| `MaxUtteranceBytes` | `8388608` |
| `PersistAudio` | `false` |
| `VoiceId` | `null` |
| `OutputMediaType` | `audio/mpeg` |
| `InputSampleRate` | `16000` Hz |
| `MaxSpokenCharactersPerTurn` | `5000` |

The WebSocket route does not exist until `UseVoiceConversation()` is called. A ready
conversation also needs both `ISpeechTranscriber` and `ISpeechSynthesizer`.

## Endpoint options are code-only

<p class="reads-this">Read by <code>Tracon.AspNetCore</code> at the <code>MapTracon()</code> call, not from configuration.</p>

`TraconEndpointOptions` belongs to the `MapTracon()` call. It has no named
configuration section.

| Option | Default |
|---|---:|
| `AllowRemoteAccess` | `false` |
| `AuthToken` | `null` |
| `AuthorizationPolicy` | `null` |
| `RequireRolePolicies` | `false` |
| `EnableDiagnosticsEndpoint` | `false` |
| `MapOpenAIConversations` | `true` |
| `RunEventPollInterval` | 250 ms |
| `AllowedOrigins` | empty (no CORS header sent) |

```csharp
app.MapTracon("/tracon", options =>
{
    options.AllowRemoteAccess = true;
    options.AuthToken = builder.Configuration["Tracon:AuthToken"];
    options.EnableDiagnosticsEndpoint = true;
    options.RequireAuthorization("TraconAccess");
});
```

`MapOpenAIConversations` is the only one that starts on (`true`).<!-- claim:option TraconEndpointOptions.MapOpenAIConversations=true --> Setting it to `false`
leaves the four `/v1/conversations` routes unmapped: they answer `404` and
disappear from the OpenAPI document, while `/v1/responses` and
`/v1/chat/completions` are unaffected. It shrinks the surface a deployment that
never calls those routes exposes; it is not a security control, since the
routes go through the same role policies, API key scopes, session ownership and
`IRunAuthorizationHandler` gate as `/api/sessions`.

Do not enable remote access without a token, API key, or authorization policy.
Behind a reverse proxy, configure forwarded headers and do not treat the proxy's
loopback address as a security boundary. Set `RequireRolePolicies=true` only after
you register the Reader, Operator, and Admin policies; otherwise mapping fails at
startup by design.

## Options without a named section

<p class="reads-this">Read at registration time by the package that owns each call. None of them can be set from a settings file.</p>

The following features use explicit code options because they contain delegates,
freeze an exposure allowlist at registration, or define request-resolution policy:

- `UseTenancy(TraconTenancyOptions)` selects the claim or explicitly allowed
  header resolver. Four properties decide how a request's tenant is resolved, and
  the default of each is the safe one:

  | Property | Default | What it does |
  |---|---|---|
  | `Enabled` | `false` | Multi-tenancy is off until you turn it on; every request then resolves a tenant |
  | `ClaimType` | *(none)* | The claim the resolver reads from the authenticated principal. **Left unset, no claim is read at all** — set it explicitly, for example to `tenant_id` |
  | `AllowHeaderResolution` | `false` | Whether `X-Tracon-Tenant` may name the tenant. **Leave this off in production** unless a trusted gateway sets the header and strips any client copy |
  | `HeaderName` | `X-Tracon-Tenant` | The header consulted when header resolution is on |
  | `AllowedTenants` | empty | When populated, an allowlist: a resolved tenant outside it is rejected rather than served |
- `UseMcpServer(TraconMcpServerOptions)` exposes no agent by default (`false`).<!-- claim:option TraconMcpServerOptions.ExposeAllAgents=false -->
- `UseA2A(TraconA2AOptions)` exposes no agent by default.
- `AddModelRunJudge(ModelRunJudgeOptions)` defines the judge model and its
  `Criteria`: the plain-language standard the judge scores a run against.
- `AddAgent()`, `AddSkill()`, `AddWorkflow()`, and `AddEvalCheck()` define executable
  or compiled behavior.
- `IDataSubjectResolver` (`services.AddSingleton<IDataSubjectResolver, ...>()`) maps a
  data subject id to their sessions, runs, and conversations — no default
  implementation, no configuration section; see
  [Data subject rights](/concepts/governance/#data-subject-rights).

Use the [API reference](/api/) for every property on these code-only
types. Their absence from this section table is deliberate.

## Read next

- [Securing the endpoints](/getting-started/security/) — the options above that decide who can call what
- [Production deployment](/guides/production/) — which of these you set differently per environment
- [Troubleshooting](/troubleshooting/) — what a wrong value looks like at run time
