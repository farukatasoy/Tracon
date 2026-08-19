---
title: Configuration
description: Verified configuration sections and defaults for AgentPrism core services, providers, persistence, operations, security, and optional surfaces.
slug: reference/configuration
---

This page lists values that the current option types define. It does not infer a
default from provider behavior. `null`, empty, and off mean exactly that.

## Binding rules

`builder.AddAgentPrism()` on an `IHostApplicationBuilder` reads the `AgentPrism`
section. Package extensions then register their capability and read or accept their
own options. A section in a file does not load a package by itself.

```csharp
var agentPrism = builder.AddAgentPrism()
    .UseOpenAI(builder.Configuration.GetSection(OpenAIProviderOptions.SectionName))
    .UsePostgreSql(builder.Configuration.GetSection(AgentPrismPostgreSqlOptions.SectionName))
    .UseWorkflows()
    .UseUI();
```

The configuration key separator is `:`. Environment variables use `__`, so
`AgentPrism:Providers:OpenAI:ApiKey` becomes
`AgentPrism__Providers__OpenAI__ApiKey`.

:::danger[Secrets do not belong in a settings file]
Provider keys, connection strings, bearer tokens, webhook secrets, and MCP
credentials must come from user-secrets, environment variables, or a secret manager.
Database records keep a configuration **key name**, never the secret value.
:::

```bash
dotnet user-secrets set "AgentPrism:Providers:OpenAI:ApiKey" "<value>"
dotnet user-secrets set "AgentPrism:PostgreSql:ConnectionString" "<value>"
```

## Section index

| Section | Option type | Registration that uses it |
|---|---|---|
| `AgentPrism` | `AgentPrismOptions` | `AddAgentPrism()` |
| `AgentPrism:Approvals` | `AgentPrismApprovalOptions` | `AddAgentPrism()` |
| `AgentPrism:AsyncRun` | `AgentPrismAsyncRunOptions` | `AddAgentPrism()` |
| `AgentPrism:Canary` | `CanaryOptions` | `AddAgentPrism()` |
| `AgentPrism:ContentGuard` | `AgentPrismContentGuardOptions` | Guard pipeline after a guard is registered |
| `AgentPrism:ContentGuard:Pattern` | `PatternContentGuardOptions` | Section presence or `AddPatternContentGuard()` |
| `AgentPrism:Idempotency` | `AgentPrismIdempotencyOptions` | `AddAgentPrism()` and the HTTP layer |
| `AgentPrism:Knowledge` | `AgentPrismKnowledgeOptions` | `AddAgentPrism()`; PostgreSQL and embeddings make it functional |
| `AgentPrism:OnlineEvaluation` | `OnlineEvaluationOptions` | `AddAgentPrism()` plus at least one judge |
| `AgentPrism:Quotas` | `AgentPrismQuotaOptions` | `AddAgentPrism()` |
| `AgentPrism:RateLimit` | `AgentPrismRateLimitOptions` | `MapAgentPrism()` |
| `AgentPrism:Retention` | `AgentPrismRetentionOptions` | `AddAgentPrism()` |
| `AgentPrism:RunReconciliation` | `RunReconciliationOptions` | `AddAgentPrism()` |
| `AgentPrism:Scheduling` | `AgentPrismSchedulingOptions` | `AddAgentPrism()`; `UseScheduling()` can override from code |
| `AgentPrism:SingletonExecution` | `SingletonExecutionOptions` | `AddAgentPrism()` |
| `AgentPrism:TenantProviders` | `AgentPrismTenantProviderOptions` | `AddAgentPrism()` |
| `AgentPrism:Webhooks` | `AgentPrismWebhookOptions` | `AddAgentPrism()` |
| `AgentPrism:Providers:OpenAI` | `OpenAIProviderOptions` | The configuration overload of `UseOpenAI()` |
| `AgentPrism:Providers:OpenAICompatible:{name}` | `OpenAIProviderOptions` shape | The configuration overload of `UseOpenAICompatible()` |
| `AgentPrism:Providers:Anthropic` | `AnthropicProviderOptions` | The configuration overload of `UseAnthropic()` |
| `AgentPrism:Providers:Google` | `GoogleProviderOptions` | The configuration overload of `UseGoogle()` |
| `AgentPrism:Providers:AzureOpenAI` | `AzureOpenAIProviderOptions` | The configuration overload of `UseAzureOpenAI()` |
| `AgentPrism:PostgreSql` | `AgentPrismPostgreSqlOptions` | The configuration overload of `UsePostgreSql()` |
| `AgentPrism:SqlServer` | `AgentPrismSqlServerOptions` | The configuration overload of `UseSqlServer()` |
| `AgentPrism:Sqlite` | `AgentPrismSqliteOptions` | The configuration overload of `UseSqlite()` |
| `AgentPrism:Mcp` | `AgentPrismMcpOptions` | The explicit configuration overload of `UseMcp()` |
| `AgentPrism:Workflows` | `AgentPrismWorkflowOptions` | `UseWorkflows()` |
| `AgentPrism:Voice` | `VoiceOptions` | `UseVoice()` |
| `AgentPrism:Voice:Conversation` | `VoiceConversationOptions` | The configuration overload of `UseVoiceConversation()` |

`AddAgentPrism()` binds `Skills:Scripts` values but does not register a script runner.
Call `UseSkillScripts()` to cross that execution boundary; the call sets `Enabled`
and registers the runner. A settings section or grant record alone cannot execute a
script.

## Core defaults under `AgentPrism`

### Identity, validation, and agent graphs

| Key relative to `AgentPrism` | Default | Meaning |
|---|---:|---|
| `DefaultTenantId` | `default` | Tenant used when no request resolver supplies one |
| `Validation:McpTimeout` | 5 seconds | Fresh MCP lookup limit during definition validation |
| `AgentGraph:MaxDepth` | `3` | Largest child-agent call depth; root depth is zero |
| `AgentGraph:MaxTotalTokens` | `200000` | Token budget shared by the whole call tree |
| `AgentGraph:MaxTotalRuns` | `25` | Largest child-run count; the root does not count |
| `UtilityModel` | `null` | Optional model binding for compaction summarization |

Non-positive graph token or run limits remove that limit. See
[`AgentPrismAgentGraphOptions`](/AgentPrism/api/agentprism.agentprismagentgraphoptions/)
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

`Enabled=true` is invalid until `PlatformIsolationAcknowledged=true`. AgentPrism
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
[Reliable runs](/AgentPrism/guides/reliability/) and
[Model providers](/AgentPrism/guides/model-providers/).

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

These settings control AgentPrism span creation and its own trace store. They do not
replace the application's OpenTelemetry exporter.

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

### Pricing

Pricing has no built-in values. `Currency`, provider/model token prices, and voice
prices all start empty. The supported shape is:

```text
AgentPrism:Pricing:Currency
AgentPrism:Pricing:{provider}:{model}:Input
AgentPrism:Pricing:{provider}:{model}:Output
AgentPrism:Pricing:Voice:{provider}:{model}:PerMillionCharacters
AgentPrism:Pricing:Voice:{provider}:{model}:PerMinute
```

Values label and calculate reports only. AgentPrism performs no currency conversion.
The shorter configuration keys `Input` and `Output` bind to the code properties
`InputCostPerMillionTokens` and `OutputCostPerMillionTokens`.

## Operational sections

### Scheduling, async runs, leases, and recovery

| Section and key | Default |
|---|---:|
| `Scheduling:Enabled` | `true` |
| `Scheduling:RunWorker` | `true` |
| `Scheduling:MaxConcurrentJobs` | `2` |
| `Scheduling:PollInterval` | 10 seconds |
| `Scheduling:LeaseDuration` | 5 minutes |
| `Scheduling:MaxAttempts` | `3` |
| `Scheduling:MaxItemsPerJob` | `1000` |
| `AsyncRun:Enabled` | `true` |
| `AsyncRun:MaxAttempts` | `1` |
| `SingletonExecution:Enabled` | `false` |
| `SingletonExecution:LeaseDuration` | 60 seconds |
| `SingletonExecution:OwnerId` | `null`; generated by the runtime |
| `RunReconciliation:Enabled` | `false` |
| `RunReconciliation:HeartbeatInterval` | 30 seconds |
| `RunReconciliation:OrphanThreshold` | 5 minutes |
| `RunReconciliation:ScanInterval` | 1 minute |
| `RunReconciliation:MaxRunsPerScan` | `100` |

`UseScheduling()` changes worker settings from code; it does not create a second
queue. `AddAgentPrism()` already registers the core job contracts.

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

### Webhooks

| Key relative to `AgentPrism:Webhooks` | Default |
|---|---:|
| `Enabled` | `true` |
| `AllowPrivateNetworkTargets` | `false` |
| `AllowInsecureHttp` | `false` |
| `Timeout` | 10 seconds |
| `MaxResponseBytes` | `8192` |
| `DisableAfterConsecutiveFailures` | `20` |
| `RetryDelays` | 1 minute, 5 minutes, 30 minutes, 2 hours, 6 hours |
| `SignatureTolerance` | 5 minutes |

No request is sent while no subscription exists. Private targets, insecure HTTP, and
redirects remain blocked by default.

## Retention defaults

`AgentPrism:Retention:Enabled` defaults to `false`. `BatchSize` defaults to `5000`
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
`Configure(AgentPrismOptions)` or a stored retention policy.

## Persistence sections

| Section | Key | Default |
|---|---|---|
| `AgentPrism:PostgreSql` | `ConnectionString` | `null`; required by the configuration registration |
|  | `SchemaName` | `agentprism` |
|  | `AutoApplyMigrations` | `true` |
|  | `CommandTimeoutSeconds` | `30` |
| `AgentPrism:SqlServer` | `ConnectionString` | `null`; required by the configuration registration |
|  | `SchemaName` | `agentprism` |
|  | `AutoApplyMigrations` | `true` |
|  | `CommandTimeoutSeconds` | `30` |
| `AgentPrism:Sqlite` | `ConnectionString` | `null`; required by the configuration registration |
|  | `TablePrefix` | `agentprism_` |
|  | `AutoApplyMigrations` | `true` |
|  | `CommandTimeoutSeconds` | `30` |

SQLite rejects bare `Data Source=:memory:`. Use a shared in-memory URI when a file is
not suitable. PostgreSQL and SQL Server use a schema; SQLite uses a table prefix.

## Provider sections

All provider model lists start empty. All nullable request timeouts use the provider
library default.

| Section | Verified defaults |
|---|---|
| `AgentPrism:Providers:OpenAI` | `ApiKey=null`, `DefaultModel=null`, `Endpoint=null` for OpenAI, `Organization=null`, `Timeout=null`, `Models=[]` |
| `AgentPrism:Providers:OpenAICompatible:{name}` | Same shape as OpenAI; `EnableResponsesSurface=false` |
| `AgentPrism:Providers:Anthropic` | `ApiKey=null`, `DefaultModel=null`, `Endpoint=null` for Anthropic, `DefaultMaxOutputTokens=4096`, `Timeout=null`, `MaxRetries=null`, `Models=[]` |
| `AgentPrism:Providers:Google` | `ApiKey=null`, `DefaultModel=null`, `Endpoint=null` for Google, `ApiVersion=null`, `Timeout=null`, `Models=[]` |
| `AgentPrism:Providers:AzureOpenAI` | `Endpoint=null` but required, `ApiKey=null`, `CredentialFactory=null`, `DefaultDeployment=null`, `Audience=null` for public cloud, `Timeout=null`, `Models=[]` |

`CredentialFactory` is code-only because it is a delegate. When it is set, Azure does
not use `ApiKey`. The consumer chooses and references `Azure.Identity` when managed
identity is required.

| `AgentPrism:TenantProviders` | `AllowedConfigurationPrefix="AgentPrism:ProviderKeys:"` |

A tenant provider binding's configuration key name must start with
`AllowedConfigurationPrefix`; a name outside it is rejected with `400`, both when the
binding is saved and again when it is resolved. See
[Per-tenant credentials](/AgentPrism/guides/model-providers/#per-tenant-credentials-byok).

## MCP, workflows, and voice

### MCP client

| Key relative to `AgentPrism:Mcp` | Default |
|---|---:|
| `Enabled` | `true` |
| `RefreshInterval` | 5 minutes |
| `ConnectionTimeout` | 30 seconds |
| `MaxToolsPerServer` | `100` |
| `MaxResourceBytesPerResource` | `65536` |
| `MaxResourceBytesTotal` | `262144` |
| `OAuthCallbackBaseUri` | `null` |

`UseMcp()` without arguments uses these defaults but does not read `IConfiguration`
implicitly. Use the explicit overload when you want the section:

```csharp
agentPrism.UseMcp(
    builder.Configuration.GetSection(AgentPrismMcpOptions.SectionName),
    configure: null);
```

### Workflows

| Key relative to `AgentPrism:Workflows` | Default |
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

| Key relative to `AgentPrism:Voice` | Default |
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

| Key relative to `AgentPrism:Voice:Conversation` | Default |
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

`AgentPrismEndpointOptions` belongs to the `MapAgentPrism()` call. It has no named
configuration section.

| Option | Default |
|---|---:|
| `AllowRemoteAccess` | `false` |
| `AuthToken` | `null` |
| `AuthorizationPolicy` | `null` |
| `RequireRolePolicies` | `false` |
| `EnableDiagnosticsEndpoint` | `false` |
| `RunEventPollInterval` | 250 ms |
| `AllowedOrigins` | empty (no CORS header sent) |

```csharp
app.MapAgentPrism("/agentprism", options =>
{
    options.AllowRemoteAccess = true;
    options.AuthToken = builder.Configuration["AgentPrism:AuthToken"];
    options.EnableDiagnosticsEndpoint = true;
    options.RequireAuthorization("AgentPrismAccess");
});
```

Do not enable remote access without a token, API key, or authorization policy.
Behind a reverse proxy, configure forwarded headers and do not treat the proxy's
loopback address as a security boundary. Set `RequireRolePolicies=true` only after
you register the Reader, Operator, and Admin policies; otherwise mapping fails at
startup by design.

## Options without a named section

The following features use explicit code options because they contain delegates,
freeze an exposure allowlist at registration, or define request-resolution policy:

- `UseTenancy(AgentPrismTenancyOptions)` selects the claim or explicitly allowed
  header resolver.
- `UseMcpServer(AgentPrismMcpServerOptions)` exposes no agent by default.
- `UseA2A(AgentPrismA2AOptions)` exposes no agent by default.
- `AddModelRunJudge(ModelRunJudgeOptions)` defines the judge model and criteria.
- `AddAgent()`, `AddSkill()`, `AddWorkflow()`, and `AddEvalCheck()` define executable
  or compiled behavior.
- `IDataSubjectResolver` (`services.AddSingleton<IDataSubjectResolver, ...>()`) maps a
  data subject id to their sessions, runs, and conversations — no default
  implementation, no configuration section; see
  [Data subject rights](/AgentPrism/concepts/governance/#data-subject-rights).

Use the [API reference](/AgentPrism/api/) for every property on these code-only
types. Their absence from this section table is deliberate.
