---
title: Package configuration
description: Defaults for the sections that a package of its own reads - the SQL stores, the model providers, MCP, workflows, and voice.
slug: reference/configuration-packages
---

These sections are read by a package other than `Tracon.Core`. The binding rules, the
section index, and the core defaults are on [Configuration](/reference/configuration/);
everything said there about reading, overriding, and reloading a value applies here
unchanged.

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

Each `Models` entry binds every `ModelDescriptor` field: `Name`, `DisplayName`,
`ContextWindowTokens`, `MaxOutputTokens`, `SupportsStreaming` (`true`),
`SupportsTools` (`true`), `SupportsReasoning` (`false`), `SupportsStructuredOutput`
(`false`), `SupportsSampling` (`null`), `RejectedSamplingFields` (`None`), and the five
`*CostPerMillionTokens` prices.
`SupportsSampling = false` makes a definition that sets `Temperature` or `TopP` fail
to compile. `RejectedSamplingFields` takes a name or a comma-separated list of request
fields (`Temperature`, `TopP`, `Seed`, `TopK`, `StopSequences`, `FrequencyPenalty`, `PresencePenalty`,
`ToolMode`, `AllowMultipleToolCalls`, `ReasoningOutput`), and makes a definition that sets one fail to compile
for that model; a name that does not exist stops the host at start. `MaxOutputTokens` lets the
validation report warn when a binding leaves its own limit to a lower provider default, such as
Anthropic's `DefaultMaxOutputTokens`. See
[the model catalog](/guides/model-providers/#model-catalog-is-metadata-not-permission).

```json
{
  "Tracon": {
    "Providers": {
      "Anthropic": {
        "DefaultMaxOutputTokens": 4096,
        "Models": [
          {
            "Name": "your-current-claude-model",
            "MaxOutputTokens": 128000,
            "SupportsStructuredOutput": true,
            "SupportsSampling": false
          }
        ]
      }
    }
  }
}
```

| `Tracon:TenantProviders` | `AllowedConfigurationPrefix="Tracon:ProviderKeys:"`, `RequireEgressPolicy=false` |

`RequireEgressPolicy` (`TraconTenantProviderOptions`) makes a tenant with no egress policy call no provider. It is off
by default; see [Per-tenant provider credentials and
egress](/concepts/governance/#per-tenant-provider-credentials-and-egress).

A tenant provider binding's configuration key name must start with
`AllowedConfigurationPrefix` followed by the tenant's segment
(`Tracon:ProviderKeys:acme:...`); a flat name belongs to the default tenant. Any other
name is rejected with `400`, both when the binding is saved and again when it is
resolved. See
[Per-tenant credentials](/guides/model-providers/#per-tenant-credentials-byok).

## MCP, workflows, and voice

<p class="reads-this">Read by <code>Tracon.Mcp</code>, <code>Tracon.Workflows</code>, and <code>Tracon.Voice</code>. Live conversation is the exception: it is in <code>Tracon.Core</code>, behind <code>UseVoiceConversation()</code>.</p>

### MCP client

| Key relative to `Tracon:Mcp` | Default |
|---|---:|
| `Enabled` | `true` |
| `RefreshInterval` | 5 minutes |
| `StaleToolRetention` | 15 minutes |
| `ConnectionTimeout` | 30 seconds |
| `MaxToolsPerServer` | `100`; the tool list is read page by page and the read stops at the first tool past the limit. Must be at least 1 |
| `MaxResourcesPerServer` | `1000`; the resource list is read the same way. A resource past the limit is not declared, so it cannot be read |
| `MaxPromptsPerServer` | `100`; the prompt list is read the same way |
| `MaxResponseBytes` | `16777216` (16 MiB); a larger response fails the call. A stream of events is counted per event. Applies when a connection is opened. Must be at least 1 |
| `MaxResourceBytesPerResource` | `65536`. Must be at least 1 |
| `MaxResourceBytesTotal` | `262144`. Must be at least 1 |
| `OAuthCallbackBaseUri` | `null` |
| `OAuthTokenStore` | `Memory` |
| `OAuthTokenSlidingLifetime` | 30 days. Must be greater than zero in both modes |
| `AllowedConfigurationPrefix` | `Tracon:McpSecrets:` |

`AllowedConfigurationPrefix` belongs to `TraconMcpSecurityOptions` rather than
`TraconMcpOptions`, and binds through `AddTracon()` — the rule is enforced
both where a server definition is saved and where its key is resolved, and those two
live in packages that do not reference each other. The section name is the same, so
it stays one section to configure. It bounds both `headerConfigurationKeys` and
`oauthClientSecretConfigurationKey`, and inside it a tenant's server names
`{prefix}{tenant}:...`; a flat name belongs to the default tenant.

`Mcp:OAuthTokenStore` is `Memory` or `DistributedCache`. `DistributedCache` keeps the OAuth tokens of the
servers that use OAuth in your `IDistributedCache`, encrypted, and lets the provider's redirect reach any
instance. The application does not start in that mode without an `IDistributedCache` and content protection.
`OAuthTokenSlidingLifetime` is how long a token stays in the cache without a read or a write. It is used by
`DistributedCache` only, but the host does not start with a value of zero or less in either mode. See
[MCP tools and OAuth across instances](/guides/mcp-across-instances/).

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
| `MaxConcurrentRunsPerTenant` | `null`; no per-tenant limit. When set, from `1` to `MaxConcurrentRuns` |
| `RunTimeout` | 10 minutes |
| `MaxRunTimeout` | `null`; a definition's `runTimeout` may then only shorten `RunTimeout` |
| `MaxSuperSteps` | `100`; a sequential chain that needs more super-steps than this is refused when it is saved |
| `MaxNodeSettingsLength` | `16384` bytes |
| `KeepCheckpointsAfterCompletion` | `true` |
| `ResumeInterruptedRuns` | `false`; requires `EnableCheckpointing` |
| `MaxResumeAttempts` | `1`; from `0` to `100` |
| `RequestExpirationScanInterval` | 1 minute |
| `MaxRequestExpirationsPerScan` | `100` |

`MaxConcurrentRunsPerTenant` limits how many runs one tenant has going at the same time,
in each process and, for queued workflow jobs, in the job queue (a soft count across
processes). See [How many runs go at once](/guides/background-work/#limit-workflow-runs-per-tenant).

The section does not register the execution engine. Call `UseWorkflows()`; that call
binds this section and then applies its optional code override. A change to the section needs a
restart, like every other section: nothing that reads these options follows a reload.

The durations are clock times: `00:30:00` is thirty minutes and `1.00:00:00` is a day. A bare
number (`30`) and an hour above 23 (`24:00:00`) stop the host at startup, because .NET would read
them as 30 and 24 days, and so does any text the key's type cannot read (`MaxConcurrentRunsPerTenant=abc`);
the message names the full key and quotes the text. A key that is empty or blank is not given and
keeps its default.

`MaxRunTimeout` is the longest run timeout a workflow definition may set. A save above
it, or above `RunTimeout` while it is unset, is refused with `400`. A workflow defined in code
sets it with `WorkflowRegistrationOptions.RunTimeout`; a value above the ceiling stops the host at
startup, naming the workflow, while `Enabled` is `true`. It must be at least
`RunTimeout` and fails at startup otherwise, even while `Enabled` is `false`. A
stored definition above a ceiling that was lowered later runs with the ceiling, and a
warning is logged.

`MaxNodeSettingsLength` caps the JSON text of one function node's settings when a
workflow is saved; a definition stored before the cap was lowered still runs.

`RequestExpirationScanInterval` is how often the scan that closes the runs of expired
[workflow requests](/concepts/workflow-requests/#when-the-time-passes) looks; it must be
from one millisecond to about 49.7 days and fails at startup otherwise, while `Enabled` is
`true`. `MaxRequestExpirationsPerScan` must be at least `1`.

### Speech tools

| Key relative to `Tracon:Voice` | Default |
|---|---:|
| `Provider` | `elevenlabs` |
| `ApiKey` | `null` |
| `Endpoint` | `null`; provider public endpoint |
| `DefaultVoiceId` | `null` |
| `SynthesisModelId` | `null` |
| `TranscriptionModelId` | `null` |
| `DialogueModelId` | `null`; the provider's default dialogue model |
| `OutputFormat` | `mp3_44100_128` |
| `MaxCharactersPerRequest` | `5000` |
| `MaxDialogueCharactersPerRequest` | `2000` |
| `MaxConcurrentRequests` | `2` |
| `RequireApproval` | `false` |
| `Timeout` | `null`; implementation uses 100 seconds |

The section is validated when the application starts. `Provider`, `ApiKey` and
`Endpoint` configure only the built-in ElevenLabs client; `elevenlabs` is the only
`Provider` value. When your own `ISpeechSynthesizer` and `ISpeechTranscriber` are
registered before `UseVoice()`, those three are not required. `Provider` is not a
price key: `Tracon:Pricing:Voice` is keyed by the `ProviderName` of the implementation
that did the work. `MaxCharactersPerRequest` is the built-in client's limit; your own
synthesizer reports its limit through `ISpeechSynthesizer.MaxCharactersPerRequest`.

`DialogueModelId` and `MaxDialogueCharactersPerRequest` apply to a dialogue voiced by
the built-in client. The single-voice `SynthesisModelId` is never sent for a
dialogue. `MaxDialogueCharactersPerRequest` must be greater than zero; a longer
dialogue is refused, not split, and the built-in client reports the value through
`ISpeechDialogueSynthesizer.DialogueCapabilities`. Synthesis, dialogue and
transcription share the one `MaxConcurrentRequests` limit.

### Voice conversation

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

## Read next

- [Configuration](/reference/configuration/) — the binding rules, the section index, and the core sections
- [Choosing packages](/packages/) — which package brings which section
