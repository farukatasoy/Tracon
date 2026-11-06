---
title: Configuration
description: Verified configuration sections and defaults for Tracon core services, operations, and security, and the index of every section a package reads.
slug: reference/configuration
---

This page lists values that the current option types define. It does not infer a
default from provider behavior. `null`, empty, and off mean exactly that.

The sections that a package of its own reads (`Tracon.PostgreSql`, `Tracon.SqlServer`,
`Tracon.Sqlite`, the model provider packages, `Tracon.Mcp`, `Tracon.Workflows`, and
`Tracon.Voice`) are on [Package configuration](/reference/configuration-packages/); the
section index below lists all of them.

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

### A value that cannot be read stops the host

A number, flag, time span, enum or address key that holds text it cannot read stops the
application at startup. The message names the full key and quotes the text, and it lists
every such key of the same options type at once:

```text
Tracon:Tools:DefaultMaxOutputBytes is '10MB', which is not a 32-bit whole number.
Write a valid value, or remove the key to keep the default.
```

Tracon does not skip such a key and keep the default. Several defaults are the loose
side: `nope` or `yes` instead of `false` or `true` would leave a protection or a limit as
it was. A number is a whole number or a decimal written with `.`, a flag is `true` or
`false`, a duration is a time span written `[days.]hh:mm:ss`, and an enum is one of its
member names. A duration is a clock: `00:00:30` is thirty seconds and `2.00:00:00` is two
days. A bare number (`30`) and an hour above 23 (`48:00:00`) are refused, because .NET would
read them as days and a limit meant as seconds would become a month. A plain enum
takes one member: `Tenant,Global` is refused, and so is a number that names no member.
`Voice:Endpoint`, `Providers:OpenAI:Live:Endpoint` and `Mcp:OAuthCallbackBaseUri` take an
absolute address such as `https://gateway.example.com/v1/`; `gateway.example.com/v1` is
refused. The `Endpoint` keys of the model providers are checked by the provider's own
validator, which refuses a relative address.

- **Empty means not given.** A key with no text keeps the default, and so does a number, flag, duration or
  address key that holds only white space.
- **A text option made only of white space stops the host.** `Audit:ChainKeyId` set to `"   "`, a `Provider` or a
  `Model` of spaces, a mask of blanks: the value would look applied and name nothing, so the message names the member and
  asks you to write a value or remove the key. This holds for a value assigned in code as well.
- **A list is written as one entry per key.** `EnvironmentAllowList:0=PATH` is a list of
  one; `EnvironmentAllowList=PATH` is refused, and the message shows the `:0` form. A list
  that is present replaces the default. A JSON `[]` and a key with no text are "not
  given" and keep the default: to empty a list, clear it in code.
- **A map is written as one entry per name.** `MaxConcurrentJobsPerLane:media=2` limits the
  `media` lane; `MaxConcurrentJobsPerLane=2` names no lane and is refused, and the message
  shows the `:<name>` form.
- **An entry that cannot be read is named by its position** (`Quotas:ThresholdPercents:1`)
  or by its name (`Scheduling:MaxConcurrentJobsPerLane:media`).
- **Code that sets the option after binding wins.** A value you assign in a `Configure`
  callback replaces the one from configuration, and a typo in the file that you have
  overridden in code does not stop the host. Writing the value the option already has is
  not told apart from leaving it.

### A change to the configuration needs a restart

Tracon reads its options once, when the host starts. A configuration provider that reloads
(a file added with `reloadOnChange`, a vault, a configuration server) changes `IConfiguration`;
it does not change the options Tracon reads, and no option of Tracon is meant to change while
the host runs. Restart the host to apply a change.

One part follows a reload anyway: the `Tracon:Workflows` options. `UseWorkflows()` binds the
section through the .NET configuration binder, which registers a change token source on its own.
The readers that look these options up through `IOptionsMonitor` see the new value: the run
limits a save checks (`RunTimeout`, `MaxRunTimeout`, `MaxSuperSteps`), the resume settings the
job handlers read (`ResumeInterruptedRuns`, `MaxResumeAttempts`), and the cap of the
request-expiration sweep. The workflow runner does not: it keeps the values it read at start. A
reload therefore splits the settings, so a save can check a new limit while the runner applies
the old one. Treat it as unsupported and restart.

A reloaded `Tracon:Workflows` value that fails validation does not stop the request-expiration
sweep. Every later read of the options throws until a valid value replaces it, so the sweep skips
each tick it cannot read them for, writes one warning, and works again on the first tick after the
value is valid. The scans and loops that read their options on every tick behave the same way: the
approval-expiration and run-reconciliation scans, the job worker, and the database-backed gauges.

Many readers look an option up on every call, which can look like live reconfiguration. It is
not supported. A host that registers its own `IOptionsChangeTokenSource<T>` for a Tracon options
type gets an unsupported half of it: a monitor sees the new value, but a reader that built
something from the old one keeps it (the model concurrency limit holds a semaphore of the size
of its first call), and a new value that fails validation makes every later read of that options
type throw, which for `Tracon:ModelConcurrency` is every model call. Tracon's own tests pin this
behaviour, so a release that supports reload will say so here.

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
| `Tracon:InboundTriggers` | `TraconInboundTriggerOptions` | `AddTracon()`; keys in [Inbound triggers](/guides/inbound-triggers/) |
| `Tracon:Knowledge` | `TraconKnowledgeOptions` | `AddTracon()`; PostgreSQL and embeddings make it functional |
| `Tracon:OnlineEvaluation` | `OnlineEvaluationOptions` | `AddTracon()` plus at least one judge |
| `Tracon:Quotas` | `TraconQuotaOptions` | `AddTracon()` |
| `Tracon:RateLimit` | `TraconRateLimitOptions` | `MapTracon()` |
| `Tracon:Retention` | `TraconRetentionOptions` | `AddTracon()` |
| `Tracon:RunContinuation` | `TraconRunContinuationOptions` | `AddTracon()` |
| `Tracon:RunReconciliation` | `RunReconciliationOptions` | `AddTracon()` |
| `Tracon:Scheduling` | `TraconSchedulingOptions` | `AddTracon()`; `UseScheduling()` can override from code |
| `Tracon:SessionOwnership` | `TraconSessionOwnershipOptions` | `AddTracon()`; keys in [Sessions](/concepts/sessions/) |
| `Tracon:SingletonExecution` | `SingletonExecutionOptions` | `AddTracon()` |
| `Tracon:StructuredResponse` | `TraconStructuredResponseOptions` | `AddTracon()` |
| `Tracon:TenantProviders` | `TraconTenantProviderOptions` | `AddTracon()` |
| `Tracon:Webhooks` | `TraconWebhookOptions` | `AddTracon()` |
| `Tracon:Workspace` | `TraconWorkspaceOptions` | `AddTracon()` |
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
| `Tracon:Voice:Live` | `VoiceLiveOptions` | The configuration overload of `UseLiveVoice()`; keys in [Voice](/guides/voice/) |

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
| `AgentGraph:MaxTotalCost` | none | Cost budget shared by the whole call tree. It counts the token cost and the amounts that function nodes and tools reported beyond tokens; falls back to the token limit when a model's price is undefined. Once spent, the next model call is refused and a workflow function node does not start |
| `AgentGraph:MaxTotalRuns` | `25` | Largest child-run count; the root does not count |
| `AgentGraph:MaxDuration` | none | Wall-clock budget for the whole call tree, counted from the root run's start. Once passed, the next model call is refused and a workflow function node does not start; a call or node already running is not stopped |
| `AgentGraph:ChildDeadline` | 2 minutes | Longest one sub-agent call, or one agent step of a workflow, may take before it is cancelled (the cooperative layer). A workflow agent step can carry its own `timeout`, which replaces this value for that step. From 1 millisecond to about 49.7 days |
| `AgentGraph:WaitTimeout` | `ChildDeadline` plus 30 seconds | Hard cutoff for a call that ignores cancellation. Must be greater than `ChildDeadline`; when unset it follows it |
| `UtilityModel` | `null` | Optional model binding for compaction summarization. `Provider` and `Model` are both required, or no binding is built. A `Temperature`, `TopP` or `MaxOutputTokens` that is not a number stops the host at startup; an empty one means the provider default |
| `Tools:DefaultTimeout` | 30 seconds | Longest one tool call may run when its own registration sets no timeout |
| `Tools:DefaultMaxOutputBytes` | `1048576` (1 MiB) | UTF-8 byte limit for a tool result when its own registration sets none; must be at least 57 bytes. A longer result is trimmed into an envelope. Code can set `null` for no limit; a configuration key cannot, so write a large number there |
| `Tools:MaxToolsPerAgent` | `128` | Most tools one agent may carry (the selected ones, the vector search tool and the sub-agent tools). An agent over it does not compile and the error names this key. Must be at least 1 |
| `Tools:RequireTenantPolicy` | `false` | When `true`, a tenant with no [tool policy](/concepts/tools/#per-tenant-tool-policy) runs no tool, and the default tenant needs a policy of its own. Read at host start; a value that is not `true` or `false` stops the host |
| `AgentCache:MaxEntries` | `4096` | Most compiled agents the cache holds (`TraconAgentCacheOptions`), one for each tenant, agent name, definition version and culture. A new one that would pass the limit makes the ones used least recently leave first. From 1 to 1,000,000 |
| `AgentCache:IdleLifetime` | 1 hour | How long a compiled agent may go unused before it leaves the cache. Checked while the cache is in use, not on a timer. Greater than zero, at most 30 days |

A compiled agent weighs about 8 KiB with no tools and a short instruction, and about 42 KiB with sixty tools and an
8 KiB instruction (measured; the instruction counts about four bytes per character). The cache used to keep one for every
tenant that ran an agent and never gave one back, so an installation that takes the tenant from a header and lists no
`AllowedTenants` let the caller choose its size. An agent that leaves the cache is not disposed: a run that holds it
keeps it, and the next run compiles the agent again, which costs a fraction of a millisecond. A single tenant with a
hundred agents, three cultures and three live versions of each holds 900 entries and does not reach the default.
Read at host start.

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
does not provide a filesystem, network, CPU, memory, or privilege sandbox. The
environment allowlist only stops a script from inheriting variables: the script
still runs as the same operating-system user and reads what that user can read.

### Attachments and audit

| Key | Default |
|---|---|
| `Attachments:MaxBytes` | `20971520` bytes, or 20 MiB |
| `Attachments:AllowedMediaTypes` | `image/png`, `image/jpeg`, `image/webp`, `image/gif`, `application/pdf`, `text/plain`, `audio/*` |
| `Audit:ActorClaimType` | `null`; resolution tries name identifier, name, then `sub` |
| `Audit:ChainKeyId` | `null`; entries are written without a signing key. The id of the key that signs the audit hash chain |
| `Deployment:Instances` | `1`. How many instances run against the same store; at least `1`. It changes nothing at run time: above `1`, `RequireProductionProfile()` asks five more decisions. See [Run more than one instance](/guides/multiple-instances/) |
| `AuditChainKeys:<id>` | none. The base64 value of one chain key, at least 32 bytes; every key the chain was ever signed with stays here. Keep it in a secret store |

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
| `Observability:IncludeTenantTag` | `false` |
| `Observability:EnableQuotaUsageGauge` | `false` |
| `Observability:QuotaUsageRefreshInterval` | 30 seconds |
| `Observability:EnableJobQueueDepthGauge` | `false` |
| `Observability:JobQueueDepthRefreshInterval` | 30 seconds |
| `Observability:MaxJobLaneCardinality` | `64` |

These settings control Tracon span creation and its own trace store. They do not
replace the application's OpenTelemetry exporter.

`RecordSensitiveData` filters what the trace store keeps, not what the rings that
write the spans record. While it is `false` the store drops every content key from
every span: message, prompt and completion text, system instructions, tool arguments
and results, and workflow executor input and output. Token counts stay. See
[content and the two switches](/guides/observability/#content-and-the-two-switches).

`IncludeTenantTag` adds `tracon.tenant.id` to eight metrics (run, cost, judge,
response cache, audit failures, recording failures and jobs). It is off by default,
because it multiplies each series by the number of tenants. Spans and the quota
gauges always carry the tenant. A value other than `true` or `false` stops startup.
See [the tenant tag on metrics](/guides/observability/#the-tenant-tag-on-metrics).

Both gauges are off by default because each one reads the database. When a gauge
is on, a background refresh reads it at startup and then once per matching
refresh interval, and a collection only reads the last result. Turning a gauge on,
or changing its interval, takes effect at the next start. An enabled gauge needs a
refresh interval from one millisecond to about 49.7 days, or startup validation
fails.
`MaxJobLaneCardinality` bounds how many distinct job lanes get a metric series of
their own before the rest are folded into a single `other` series — see
[observability](/guides/observability/#what-the-job-metrics-count).

### Run recording

| Key | Default |
|---|---:|
| `RunRecording:Enabled` | `true` |
| `RunRecording:RecordMessageDeltas` | `true` |
| `RunRecording:RecordToolPayloads` | `true` |
| `RunRecording:RecordCitationUrls` | `false` |
| `RunRecording:MaxPayloadLength` | `8192` characters |
| `RunRecording:RecordRunInput` | `true` |
| `RunRecording:SinkTimeout` | 30 seconds. How long the writer waits for one `IRunEventSink` to take one event; a sink that does not return is dropped for that run. `00:00:00` waits for ever |

Disable tool payloads when they can contain personal or regulated data. A citation event records only the scheme and host of the source by default; `RecordCitationUrls` records the whole address, which can hold a signed credential when a retrieval tool produced it. Replay needs
recorded run input, and so does continuing a parameterized run: the parameter values a
run was bound with are recorded beside its input, and a replay, an automatic continuation,
a resume after a tool approval or a resume of a workflow that declares parameters is
refused when `RecordRunInput` was off for the original run. A store failure is logged and never blocks the agent response, so
recording is best-effort during a storage outage.

### Session workspace limits

| Key | Default |
|---|---:|
| `Workspace:MaxFileBytes` | `262144` (256 KiB) |
| `Workspace:MaxSessionBytes` | `5242880` (5 MiB) |
| `Workspace:MaxTenantBytes` | `524288000` (500 MiB) |

The limits apply to an agent whose definition turns `workspace.enabled` on. A write that would
pass one is refused and the model reads "Workspace quota reached". Each value is at least `1`, a session limit
is not below the file limit, and a tenant limit is not below the session limit; the application refuses
to start otherwise. A file counts 1 KiB more than its content in the limit of a session and of a tenant, for its row. The limits are soft, and a SQL Server store counts two bytes for each ASCII character.
See [Give an agent files in each session](/guides/context-and-memory/#give-an-agent-files-in-each-session).

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
Tracon:Pricing:{provider}:{model}:CacheWrite
Tracon:Pricing:{provider}:{model}:CacheWriteLong
Tracon:Pricing:Voice:{provider}:{model}:PerMillionCharacters
Tracon:Pricing:Voice:{provider}:{model}:PerMinute
Tracon:Pricing:Images:{provider}:{model}:PerImage
Tracon:Pricing:Images:{provider}:{model}:SizeMultipliers:{provider-size}
Tracon:Pricing:Images:{provider}:{model}:OutputCostPerMillionTokens
Tracon:Pricing:HostedTools:{provider}:{kind}:PerCall
```

`HostedTools` prices the calls of a [hosted tool](/guides/hosted-tools/). The `{provider}` is the one that ran
the call (`openai-responses`, `anthropic`, `google`), and `{kind}` is `WebSearch`, `CodeInterpreter`, or
`FileSearch`; a name that is none of them stops the host at start. A kind with no price is listed on the
run and not added to its cost; Tracon never prices it at zero.

Values label and calculate reports only. Tracon performs no currency conversion, so
`Currency` also decides which reported amounts add up: an amount that a workflow function node or a
tool reported beyond tokens joins the run's non-token total only when it is in this
currency (an amount that names no currency is in it, and with no `Currency` set only such
an amount adds); any other is listed and counted as not added. A function node's amounts
are the amounts the node computed, not a price table Tracon applies.

The shorter configuration keys `Input`, `Output`, `CachedInput`, `CacheWrite` and
`CacheWriteLong` bind to the code properties `InputCostPerMillionTokens`,
`OutputCostPerMillionTokens`, `CachedInputCostPerMillionTokens`,
`CacheWriteCostPerMillionTokens` and `CacheWriteLongCostPerMillionTokens`.

`CachedInput` is the rate for input tokens the provider served from its prompt
cache. It is optional, and its absence is not a configuration error — unlike
`Input` and `Output`, where an entry with neither is rejected at startup as a
likely typo.

`CacheWrite` and `CacheWriteLong` are the rates for input tokens the provider
*wrote* into its prompt cache, by how long the entry lives: Anthropic bills a
five-minute entry at 1.25 times the input price and a one-hour entry at 2 times.
Both are optional and independent, and a negative value stops the host at start.
Which class a write belongs to is the provider's report; a response with no per-class
split counts the whole write under `anthropic.promptCaching.ttl` (the short class when it sets none).

Cost is computed by **subtraction**, because a provider counts cached and written
tokens *inside* the input total rather than beside it:

```text
full_price_input = InputTokens - CachedInputTokens
                   - CacheWriteInputTokens - CacheWriteLongInputTokens
cost             = full_price_input           * Input
                 + CachedInputTokens          * CachedInput
                 + CacheWriteInputTokens      * CacheWrite
                 + CacheWriteLongInputTokens  * CacheWriteLong
                 + OutputTokens               * Output
```

:::note
Leave `CachedInput`, `CacheWrite` or `CacheWriteLong` unset and the tokens of that
kind are priced at `Input`, exactly as before the rate existed; the two write classes
are decided one by one. A missing rate never makes a run's `pricingSource`
`Unknown` — that value means the **model** price is missing, which is a different
fault. Set a rate to `0` to state that cache reads, or writes, are free.
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
| `Scheduling:MaxDurationByHandlerKey` | empty; each attempt's time limit per handler key, 1 ms to about 49.7 days. A job's own `maxDuration` wins; a key not listed has no limit. A value out of range fails at startup |
| `Scheduling:MaxItemsPerJob` | `1000` |
| `Scheduling:Lanes` | `null`; leases from every lane |
| `Scheduling:MaxConcurrentJobsPerLane` | empty; a lane not listed shares `MaxConcurrentJobs`. A value that is not a number fails at startup |
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

`RunReconciliation:Enabled` also switches on cross-instance run cancellation, and
`RunReconciliation:HeartbeatInterval` is then its delay. See
[run reconciliation](/guides/background-work/#singleton-services-and-orphaned-run-recovery).

Every interval that drives a background timer must be between 1 millisecond and about
49.7 days, the .NET timer range: `Scheduling:PollInterval`,
`RunReconciliation:HeartbeatInterval`, `RunReconciliation:ScanInterval`,
`Approvals:ScanInterval`, `Canary:ScanInterval`, `Health:BackgroundInterval`,
`Mcp:RefreshInterval`, and the two `Observability` gauge intervals. A lease renews at a
third of its length, so `Scheduling:LeaseDuration` and `SingletonExecution:LeaseDuration`
may each be at most about 149 days. A value outside its range fails at
startup and names the option. Each of these is checked only while the feature that owns
its timer is on. `Retention:BatchDelay` may be zero and is always held to at most about
49.7 days.

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
| `Quotas:ThresholdPercents` | `80`, `100`. A value outside 1–100 or not a number fails at startup |
| `Quotas:AllowOnStoreFailure` | `true` |
| `Quotas:PublishThresholdToRunStream` | `false` |
| `RateLimit:Enabled` | `false` |
| `RateLimit:PermitLimit` | `60` |
| `RateLimit:Window` | 1 minute |
| `RateLimit:QueueLimit` | `0` |
| `RateLimit:Partition` | `Tenant` |
| `RateLimit:Scope` | `Instance`. `Deployment` counts both rate limiters in the database; it needs `QueueLimit` of `0`, which is checked at startup |
| `Idempotency:Enabled` | `true` |
| `Idempotency:MaxKeyLength` | `200`, the width of the narrowest column that stores a key (SQL Server `nvarchar(200)`), so a longer key is a `400` on every store. A higher value is accepted by PostgreSQL and SQLite; SQL Server still refuses a key past 200 |
| `Idempotency:ReservationTimeout` | 1 hour. A reserved key that no response completed (the process died) is taken over by the next request once it is this old; `00:00:00` never takes one over. Keep it longer than your longest request that carries an `Idempotency-Key` |
| `ContentGuard:InspectInput` | `true` |
| `ContentGuard:InspectOutput` | `true` |
| `ContentGuard:BufferStreamingOutput` | `true` |
| `ContentGuard:Pattern:DeniedTerms` | Empty |
| `ContentGuard:Pattern:MaskedPii` | `None`; comma-separated names, empty means none. A name it does not know fails at startup |
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
| `RetryDelays` | 1 minute, 5 minutes, 30 minutes, 2 hours, 6 hours. A delay that is not above zero, is longer than about 68 years (`2147483647000` milliseconds, the longest the job queue accepts), or is not a time span fails at startup |
| `SignatureTolerance` | 5 minutes |

No request is sent while no subscription exists. Private targets, insecure HTTP, and
redirects remain blocked by default.

`AllowedConfigurationPrefix` bounds which configuration key a subscription may name as
its signing secret. Inside it, a tenant's subscription names `{prefix}{tenant}:...`
and a flat name belongs to the default tenant; any other name is refused both when the
subscription is saved and when the secret is resolved. `MaxExtraHeaders` bounds a subscription's own extra
headers — headers whose name Tracon sets itself are always dropped, whatever the
limit is. `AllowPrivateNetworkTargets` here applies to webhook delivery only; the
shared `Tracon:Egress` setting covers this surface too, and either one being on is
enough.

### At-rest content protection

| Key relative to `Tracon:ContentProtection` | Default |
|---|---:|
| `Enabled` | `false` |
| `ActiveKeyId` | (none) |
| `Keys` | Empty. An entry with an empty name fails at startup |
| `Columns` | All twelve protected columns. A name that is not a protected column fails at startup |

Encryption is off until `AddContentProtection(...)` is called and `Enabled` is `true`.
A call with a delegate, `AddContentProtection(options => …)`, sets `Enabled` to `true`
before the delegate runs (the delegate can write `false`); a call with no delegate
leaves `Enabled` to this section. `Keys` maps a key id to the **name** of another configuration key — never to
the key's raw value — the same indirection
[other configuration keys](/getting-started/security/#configuration-keys-are-fenced-too)
use elsewhere. See
[at-rest content protection](/getting-started/security/#at-rest-content-protection)
for the full key setup and its limits.

The `Keys` and `Columns` checks run while `Enabled` is `false` too: rows encrypted
earlier are still read through `Keys` after protection is turned off. A `Columns`
list replaces the default; an empty one keeps every column.

## Retention defaults

<p class="reads-this">Read by <code>Tracon.Core</code>. Deletion needs a persistence package; the in-memory stores have nothing to retain across a restart.</p>

`Tracon:Retention:Enabled` defaults to `false`<!-- claim:option TraconRetentionOptions.Enabled=false -->. `BatchSize` defaults to `5000`
and `BatchDelay` to 100 ms. The following ages become configuration-based defaults
only after retention is enabled. A database policy for a target takes precedence.

| Child key | Default `MaxAgeDays` | `Archive` default |
|---|---:|---:|
| `RunEvents` | 30 days; a waiting workflow's request is kept | `false` |
| `ToolInvocations` | 90 days | `false` |
| `Spans` | 14 days | `false` |
| `Jobs` | 30 days; completed jobs only | `false` |
| `WebhookDeliveries` | 7 days; delivered rows only | `false` |
| `EvalCaseResults` | 180 days | `false` |
| `WorkflowCheckpoints` | 7 days after completion; a waiting workflow's are kept | `false` |
| `SkillScriptGrants` | 30 days after expiry or revocation | `false` |
| `Attachments` | 7 days for orphaned rows: no session, and no waiting, running, or recently finished workflow run with the session id; stored bytes go first | `false` |
| `Sessions` | Disabled, `null` | `false` |
| `Conversations` | Disabled, `null` | `false` |
| `IdempotencyKeys` | 1 day | `false` |
| `RunInputs` | 30 days; a waiting workflow's row is kept | `false` |
| `VoiceSessions` | 30 days | `false` |
| `RunScores` | 180 days | `false` |
| `DocumentEmbeddings` | Disabled, `null` | `false` |
| `WorkflowRequests` | 30 days after the request expired; closed rows only | `false` |

A workflow run waiting for a human answer (`AwaitingInput`) keeps its pending
request event, its checkpoints and its run input row for as long as it waits,
whatever their age: the request lives only in that event, the answer resumes from those
checkpoints, and the parameter values it resumes with live only in that row. A
[request timeout](/concepts/workflow-requests/#giving-a-request-a-time-limit) is what ends
the wait: when it passes, the run is closed and the next retention round deletes those
rows by age like any other.

`MaxAgeDays` takes a whole number of days, and a value below `1` fails at startup.
Configuration cannot switch off a target that has a default age: an empty value or a
JSON `null` leaves the default in place. To switch one off, save a disabled retention
policy for it, or set its `MaxAgeDays` to `null` in
`Configure<TraconRetentionOptions>(...)`.

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
| `StreamKeepAliveInterval` | 15 seconds |
| `MaxJsonBodyBytes` | 8 MiB |
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

`StreamKeepAliveInterval` is the longest a streaming response (an agent or
workflow run, a resume or answer, the OpenAI-compatible endpoints) stays
silent: when nothing else was written for that long, it writes an SSE comment
line, which SSE readers skip. A reader with an idle limit takes a silent
stream for a dead connection and closes it, and a closed connection cancels the
run. Keep the value below the shortest idle timeout between the server and its
readers - a reverse proxy's read timeout - and below 30 seconds while the
console is in use.

`MaxJsonBodyBytes` is the longest JSON request body the endpoints read; a longer one answers `413`
before any endpoint holds it. A body is read whole, parsed, recorded with its run and echoed, which costs about
thirty times its size in memory at once, and the only other limit was the server's own (30 MB on Kestrel). It counts
bytes as they arrive, so a body sent chunked, or with a length that lies, stops at it too. It covers every JSON body
under the endpoints, the definition import included; a file upload keeps its own limit, and the MCP, A2A and AG-UI
endpoints are not under it. Set a reverse proxy's body limit no higher.

Do not enable remote access without a token, API key, or authorization policy: with
none of them configured, an anonymous remote request receives `401`, and an anonymous
local request with a non-loopback `Host` or `Origin` receives `403`. Behind a reverse
proxy, configure forwarded headers and do not treat the proxy's loopback address as a
security boundary; a request that still carries `X-Forwarded-For`, `Forwarded`, or
`X-Real-IP` counts as remote. Set `RequireRolePolicies=true` only after
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
  | `AllowedTenants` | empty | When populated, an allowlist: a resolved tenant outside it is rejected rather than served. An API key is judged by the tenant it is bound to, so taking a tenant off the list stops its keys too |
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
