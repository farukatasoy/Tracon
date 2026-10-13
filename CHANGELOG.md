# Changelog

All notable changes to Tracon are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [Unreleased]

### Security

- A refused provider setting no longer repeats its value. The Anthropic choice
  settings (`anthropic.effort`, `anthropic.thinking.mode`, the prompt-caching
  placement and TTL) and thinking budget, and the Google safety thresholds and
  thinking budget, wrote the rejected value into the message, which reaches logs and
  run records; a value can be a credential pasted into the wrong field. The message
  now names the key and the accepted values only.
- The built-in ElevenLabs client refuses a tenant credential with
  `NotSupportedException` on synthesis, streaming synthesis, dialogue and
  transcription. It does not take a tenant key; a caller that passed one directly was
  served on the configured key, so the host's account paid for the tenant's speech.
  The resolver never sends one, so calls made through Tracon are not affected.

- Knowledge base embedding calls now apply the tenant's provider checks before the
  tenant's text leaves the process. A document upload, `POST /api/knowledge/{collection}/search`
  and the `search_knowledge` tool called the registered embedding generator and read
  neither the tenant's egress policy nor its own provider key. Name the provider in the
  new `TraconKnowledgeOptions.EmbeddingProvider` (`Tracon:Knowledge:EmbeddingProvider`);
  without it, the name the generator reports is used. **Behavior change:** a tenant
  with an egress policy that does not name the embedding provider, or with its own key
  for it, gets `403` on upload and search (new in the OpenAPI document and both
  generated clients) and a failed tool call. When no provider name is known, a tenant
  with a policy is refused; a tenant without one is not affected.
- A data subject export now returns protected columns as plaintext. With
  `AddContentProtection(...)` on, the export wrote the stored envelope of session
  state, run inputs and chat history, so the subject received ciphertext.
- Workflow checkpoint state is now a protected column
  (`ProtectedColumn.WorkflowCheckpointState`). It carries a workflow run's messages
  and was stored in the clear even with content protection on. New checkpoints are
  encrypted when the column is in `Columns` (the default takes every column);
  existing rows stay readable. No migration.
- Data subject export and erasure now reach four more copies of a subject's run:
  an eval case promoted from the run, the stored response of an idempotent request
  that started it, the job of a queued run (its id is the run id) and the batch item
  that started it. **Behavior change:** erasing a subject now deletes those rows, so
  an eval suite loses the cases promoted from the subject's runs; a batch job keeps
  its other items. The erasure's audit entry counts each target.
- A refused configuration key name is no longer echoed. An operator who typed a
  value, such as an API key, into a field that takes a configuration key name got
  that value back in the `400` body, and a webhook delivery wrote it to
  `webhook_deliveries.error`. The message now names the field and the allowed key
  space only. This covers every field that takes a key name: MCP server headers and
  OAuth secret, tenant provider bindings, webhook headers and signing secret, and
  inbound trigger secrets.
- A retention archive now hands protected columns to the `IArchiveSink` as
  plaintext. With `AddContentProtection(...)` on, the archive wrote the stored
  envelope of run event text and payload, tool call arguments and results,
  workflow checkpoint state, attachment content, session state and run inputs.
  **Behavior change:** a sink that stored those rows now receives the readable
  content; encrypt it in the sink if the archive must be encrypted at rest.
- A real data subject erasure now refuses with `409` while one of the subject's
  runs is `Queued` or `Running`. Such a run could write its
  session and workflow checkpoint back after the erasure committed. The problem
  carries the run ids in `activeRunIds`; a dry run lists them in the new
  `DataSubjectErasureResult.ActiveRunIds` and erases nothing. Cancel the runs, or
  pass `force=true` to erase anyway. `409` and `force` are new in the OpenAPI
  document and both generated clients.

- Text-to-speech, speech-to-text and the voice list now apply the tenant's
  provider checks before any text or audio leaves the process. The `speak`,
  `transcribe` and `list_voices` tools, `POST /api/voice/speak`,
  `GET /api/voice/voices` and the conversation socket called the setup-wide
  speech provider and read neither the tenant's egress policy nor its own
  provider key. **Behavior change:** a tenant with a saved egress policy that
  does not name its speech provider (`elevenlabs` for the built-in client) can no
  longer synthesize, transcribe or list voices: the endpoints answer `403`, the
  conversation handshake answers `403` before a connection slot is taken (an open
  conversation keeps the providers it was opened with until it closes), and a
  tool call fails. Add the provider name to the policy to keep speech working. A
  tenant with its own binding for the speech provider is refused unless the
  provider declares `SupportsTenantCredentials`; the built-in ElevenLabs client
  does not, so such a tenant is no longer billed to the host's key. A binding
  with an endpoint override, or whose key has no value, is refused too. The
  policy is judged against the provider name the engine reports, never the name
  it is registered under.
- Image generation now applies the tenant's provider checks before the prompt
  leaves the process. `generate_image` and `POST /api/images/generate` called the
  setup-wide generator and read neither the tenant's egress policy nor its own
  provider key: a tenant whose policy did not allow the image provider still had
  its prompts sent there, and a tenant with its own key for that provider was
  billed to the host's account. A policy that does not name `Images:Provider` now
  refuses the call, and so does a tenant binding for that provider, because the
  generator is built with the setup key and cannot use the tenant's. The endpoint
  answers `403` (new in the OpenAPI document and both generated clients); the tool
  call fails with the same message. A run without a tenant scope now fails before
  the provider call instead of after it.
- A root agent run now counts its whole call tree in the tenant's quota counters
  and in its `run.completed`/`run.failed` webhook. A child agent run never
  accounts on its own, and the root counted only its own tokens and cost, so the
  children's spend reached no counter and a token or cost quota was exceeded
  through sub-agents. The root now adds what its child runs recorded; a workflow
  root already counted its tree. The webhook's `inputTokens`, `outputTokens` and
  `cost` of an agent root are now the tree's totals, the same meaning a workflow
  root's event has. Costs in two currencies are not added; the root's own is kept.
- A tenant endpoint on a provider added with `UseOpenAICompatible()` now goes
  through the outbound address guard, as on every other provider. The factory
  built the provider without the guard, so a tenant binding whose endpoint host
  name resolved to a private or metadata address received the tenant's prompts
  and key; the binding save path checks only IP literals.
- One tenant can no longer stop a provider for every other tenant on the shared
  setup-time key. The circuit breaker counted every exception, so a tenant's
  repeated `400`s, an exhausted run budget, a history that names a deleted
  attachment, or a tool failure that the tool-call loop rethrows opened the
  provider's circuit for everyone for the break duration. It now counts only a
  failure the provider client threw that shows the provider is unhealthy: no
  connection, a timeout, a `5xx`, `408` or `429`, or a `401`/`403`, because a
  rejected key fails every caller in the same way. Any other permanent `4xx`
  does not count, and a failure that does not count leaves the counter and a
  half-open circuit as they were. A test or runbook that opens the circuit with
  a `400` or a `404` now needs a `401` or a connection failure.
- Data subject export and erasure now include workflow checkpoints and the
  scores of the runs found through a subject's sessions. Neither table has a
  foreign key to `runs`, so nothing cascaded to them: an erasure left the
  checkpoint state, which can hold the subject's own messages, and the scores
  with their comments, and the export did not show them. A checkpoint's `state`
  is exported as the stored JSON text. Erasure also removes the checkpoints of a
  run that waits for input: retention keeps them, erasure does not.
- A job that resumes an approved tool call (`ApprovalResume`, a built-in key a
  schedule can also enqueue) now fails when its payload names a different agent
  than the run that asked for approval, or when that run no longer exists. A
  hand-written payload could run another agent with the approved tool call.
- A failed workflow node no longer writes its exception's own message to the
  run's events. The run row already carried only the exception's type and a
  correlation id, but the `ExecutorFailed` event of the same failure carried
  the raw message - a host and port, a request detail - to the event table, the
  SSE stream and every run event sink. The event now carries the same
  display-safe text as the row (`<Type> failed. (ref: <id>)`, or a Tracon
  exception's own message), and both name the same log entry.
- A harness agent no longer reads skills from, or writes memory files to, the
  server's working directory. Microsoft Agent Framework's harness falls back
  to the process's working directory when it is not given a source: with no
  skill source it loaded every `SKILL.md` it found there, past the agent's
  `SkillNames`, the skill's enabled flag and the tenant's scope; with no file
  store it wrote what the model chose under `./agent-file-memory`, outside the
  tenant stores, retention and erasure. Every harness agent got both,
  including one saved from the console. Skills are now offered only when the
  definition names them, and file memory only when `Memory.EnableFileMemory`
  is true, on the registered `AgentFileStore`. A definition that names skills
  while `Harness.DisableAgentSkillsProvider` is true is refused when it is
  compiled; its skills were dropped without a word.
- A tenant's egress policy and its own provider key now reach every model an
  agent calls, not only the agent's own model. The model that summarizes the
  conversation during compaction (`Compaction.SummarizationModel`, or
  `UtilityModel` when the agent names none) and the judge of an `aiJudge` loop
  criterion were built without either. A definition that named a provider the
  tenant may not call compiled and ran, and each compaction sent the tenant's
  conversation to that provider. Such a definition is now refused when it is
  compiled or validated, and the refusal names the provider. The summarizer
  uses the tenant's own key when the tenant has one for its provider, so the
  tenant pays for it, and such an agent is not cached: a rotated or deleted key
  takes effect on the next run. The loop judge keeps the setup-time key: it is
  the host's one judge model, the same one the run judge calls.
- A saved egress policy now reaches agents compiled before it. A compiled
  agent kept the policy decision of the moment it was compiled, so after a
  tenant forbade a provider, an agent already in memory kept calling that
  provider until its definition changed or the process restarted. The next run
  is now compiled under the saved policy and refused before any request
  leaves; a run already in progress is not stopped. Each run reads the
  tenant's policy once to build the cache key.
- A standing approval rule for a tool of an MCP server is now bound to the
  server's endpoint. An MCP tool is named `{server}_{tool}`, so when a key
  with the `AgentsAdmin` scope pointed a server at another endpoint (or
  deleted it and added it back under the same name), a rule a security
  administrator wrote for the old endpoint approved the new endpoint's tool.
  A rule now records a fingerprint of the endpoint (`sourceFingerprint`, a
  SHA-256, never the address) when it is written, by an administrator or by
  the "don't ask again" flow, and approves the tool only while the server
  keeps that endpoint. While a server's tools are not discovered (its last
  refresh failed), its tools always ask, and `POST /api/approvals/rules` for
  one of them answers `409`. Rules stored before this release carry no
  fingerprint: each standing rule for an MCP tool asks once more after the
  upgrade. `ToolApprovalRule.SourceFingerprint` is new and part of a rule's
  identity; a custom `IToolApprovalRuleStore` has to store and compare it
  (see Changed).
- Saving an MCP server with `requiresApproval: false` now requires the
  `SecurityAdmin` scope as well as `AgentsAdmin` when the call uses an API
  key: for a new server, for a server that required approval, and for a
  server whose endpoint changes while approval stays off. The flag waives
  approval for every tool of the server, which is the decision only
  `SecurityAdmin` could make for a single tool; an `AgentsAdmin` key alone
  could switch it off and answers `403` now.
- A provider-hosted live voice session now obeys the tenant's egress policy
  and its own provider key. The session is bound to a tenant, but the OpenAI
  live provider used the shared key and read no policy, so a tenant whose
  policy excluded `openai` still had its audio sent there, and a tenant with
  its own key was billed to the installation's. `POST
  /api/voice/live/sessions` now answers `403` when the policy does not allow
  the live provider, when the tenant's binding for it has no value, or when
  the provider cannot use the tenant's key; a tenant with its own binding
  gets its own key on both the session creation and the sideband attach. A
  binding with an endpoint override is refused for a live session (`403`).

### Fixed

- The `Tracon:Knowledge` configuration section is now read. `TraconKnowledgeOptions`
  was documented as bound from it, but every value there was ignored; only code that
  called `Configure<TraconKnowledgeOptions>` changed a setting. A `Dimensions` value in
  configuration now takes effect: it must match the migrated vector column.
- A draining process (`Drain.Enabled`) now refuses every new run it would start
  itself, not only the agent run endpoint. The workflow run, resume and respond
  endpoints and run replay started a run during the drain; they now answer `503`
  (new in the OpenAPI document and both generated clients). `/v1/chat/completions`
  and `/v1/responses` answer `503` in the OpenAI error shape with `type`
  `service_unavailable`, the voice conversation handshake answers `503` before the
  upgrade, an MCP `tools/call` returns an error result and an A2A message fails.
  A trigger, eval, schedule or approval still writes its job, which waits for a
  worker that is not draining.
- A provider error with an empty or non-OpenAI body is now read with its HTTP
  status. System.ClientModel (OpenAI, Azure OpenAI) writes `Status: 404`, the
  Anthropic SDK `Status Code: NotFound` and Google.GenAI `status code 404`; only
  `HTTP 404` was read. Such a `401`, `403` or `404` moved to the next fallback link,
  and such a `400` or `404` counted against the circuit that every tenant on the
  setup key shares. The stored provider error now names the status for these
  failures too, so their error fingerprint changes once. A cancellation that a
  tool or a Tracon ring raised no longer counts against the provider's circuit;
  the provider client's own timeout still does.
- A queued agent run, or the agent run of an inbound trigger, no longer stays
  `Queued` forever when the queue refuses its job. The placeholder row was written
  first and never closed; it is now closed as `Failed`, as the queued workflow run
  already did, and the request still fails.
- A batch item or an eval case whose run stops at a tool approval now fails, and
  its run is closed as `Failed`. Both run without a session, so the approval could
  never be answered: the run stayed `AwaitingApproval` with no inbox entry, and the
  batch reported the item `Completed`. The queued agent run already had this rule.
- A document whose text the content guard masks stays a document in the run
  record. The masked message was rebuilt as plain text, so `RunStarted.Text` held
  the document's text and no `DocumentAttached` event was written. The record now
  takes the name and the text as they read after masking, or the name
  `[masked document]` when the masked text no longer reads as one document. The
  original name and summary are not recorded.
- The model calls of an evaluator added with `AddEvaluatorJudge` are now recorded.
  Each judgment writes one `judge:{name}` run of kind `Eval` with the calls' usage
  and cost, and the cost reaches the judge cost metric, as the built-in model judge
  already did. Before, nothing wrote this spend down. A failing evaluator closes
  its run as `Failed` and still throws.
- The in-memory job store now changes a job item only while it is pending, as the SQL
  stores do. It overwrote a finished item, so a late report from a worker whose lease
  had expired rewrote the result the job already gave.
- `Scheduling:Lanes`, `Scheduling:MaxConcurrentJobsPerLane`,
  `Scheduling:LaneByHandlerKey` and `Scheduling:HttpSchedulableHandlerKeys` are now
  read from configuration. The four keys were documented and bound nowhere, so a
  lane budget in `appsettings.json` was ignored and the worker leased every lane.
  A list that is present replaces the default. A per-lane budget that is not a
  number now fails at startup with the lane named, instead of being skipped.
- A webhook delivery now honors `Egress:AllowPrivateNetworkTargets`. The
  subscription was saved under that setting, but the delivery job checked the
  target against the default egress options and dropped every delivery to a
  private address; only `Webhooks:AllowPrivateNetworkTargets` worked.
- A queued run no longer fails, or runs a second time, when its session cannot
  be saved once after the model has answered. The handlers that end a queued
  turn (a queued run, the continuation of an approved tool call, the
  continuation of an interrupted run) saved the session inside their own
  failure path: one store error failed the job while its run said `Completed`,
  or released the job and called the model again. The save is now tried again
  with a growing pause while the worker holds the lease. If it still fails, the
  job fails with `tracon.job.session-save-failed`, whatever its attempt limit,
  and the model is not called again. A run that asked for approval closes
  `Failed` with the same code, because its approval cannot be answered without
  the session.
- A run that resumes after an approval decision and asks for another approval
  now puts that request in the approval mailbox. Only the queued-run handler
  wrote pending approvals, so the resumed run closed `AwaitingApproval`,
  `GET /api/approvals/pending` answered an empty list, and the chain stopped. A
  run that continues an interrupted run had the same gap. Both now save the
  session first, list the approval second and announce it with
  `approval.pending`, as a queued run does.
- The sub-agents and tools of a queued run now see the run's labels, and those
  of a resumed or continued run see its user and labels. The job payload now
  carries `labels` next to `userId`, and a resumed or continued run runs under
  the user and labels of the run it continues, not the approver's. A workflow or
  agent batch that runs from the queue now carries the user and labels of
  whoever queued it: `IJobDispatcher` captures the attribution
  `IRunAttributionContext` resolves at the time of the call, and an inbound
  trigger keeps the accept request's (an agent run it starts recorded none
  either). A schedule's payload can name one in an envelope,
  `{"items": [...], "userId": "...", "labels": {...}}`; an envelope that breaks a
  label limit is refused with `400`. A context you register yourself sees the
  replayed attribution only if it reports `AmbientRunAttributionScope` outside a
  request; the reference sample's demo context now does.
- An evaluation run whose cases have all run is no longer marked `Failed` when
  the write that closes it fails. The handler now tries the write again for a
  third of the job lease; if the store still refuses it, the next attempt runs
  no case again and only repeats the write.
- A webhook delivery the receiver accepted is no longer sent again because
  Tracon could not record the result. The result writes are now tried again for
  a third of the job lease, a write that still fails is logged with a
  reference, and a retry that finds the delivery already settled sends nothing.
  Delivery stays at-least-once: deduplicate on the `X-Tracon-Delivery` header.
- An eval case's `context` now reaches the agent, through the document channel
  ahead of the query, as a run with `documents` sends it. A case ran as its
  query alone and the context went to the evaluator only. The evaluator still
  receives it, and the case run now records the parameter values it was bound
  with.
- Promoting a run to an eval case now copies the run's recorded input: the whole
  query, the documents it carried (into `context`, one block per document) and
  the parameter values it was bound with (into `parameters`). It read only the
  `RunStarted` event, which is cut at `MaxPayloadLength` (8 KB by default), and
  dropped the documents and the values. Without a recorded input the query
  still comes from that event and keeps its `…[truncated]` marker; a run that
  carried documents with no recorded input, an attachment, or a message besides
  its documents and its request is now refused with `422`. **Requirement:** an
  `IEvalStore` stores the new `EvalCaseDraft.Parameters` in `AddCaseAsync`.
- On a SQL provider, a replay, a continuation or an approval resume of a run
  that carried documents now records them as documents. The stored document
  marker came back as JSON values the recorder did not recognize. The console's
  case editor also keeps the line breaks of a case's context.
- A fallback on the binding of the compaction summarizer or of an `aiJudge` loop
  judge no longer rewrites the run. `runs.model_id` named the judge's fallback
  model, the agent's own tokens were priced at that model, and the run got a
  `ModelFallbackUsed` event its own model never caused. The run now keeps the
  agent's model and gets no event, the side call's tokens are priced at the
  fallback model that answered, and the switch is logged.
- `POST /api/workflows/runs/{runId}/respond` now refuses an answer that does not
  fit the pending request's port with `400` before the event stream opens, as
  its contract said, and opens no run. The answer was translated only inside
  the resumed run, after the `200`, and the new run ended `Failed`. A request
  recorded by 1.0.0-preview.3 or earlier does not carry its response type's
  assembly; for it a `data` value that does not read into the type is still
  reported on the stream.
- Saving a `Sequential` node list now checks that each node takes what the node
  before it passes on, and that the first node takes the run's
  `List<ChatMessage>`. Microsoft Agent Framework drops a message no handler
  takes without an error, so such a chain was saved with `200` and every run
  ended `Completed` with no output. The save answers `400` naming both nodes and
  both types, and a stored chain that does not fit now fails its run before the
  first node. The site, the `Tracon.Workflows` README and the
  `AddWorkflowFunction` examples showed a `string` function after an agent; they
  now take `List<ChatMessage>`.
- Automatic run continuation no longer treats a workflow run or a sub-run as an
  agent turn. An orphaned workflow root was queued as an agent continuation
  under the workflow's name, and a workflow step or a callable agent's run was
  continued on its own, repeating its tools in its root's session. Such a run
  now stays `Failed` with a `RunContinuationBlocked` event; for a workflow run
  the event names `POST /api/workflows/runs/{runId}/resume`. A continuation
  queued before the fix is refused the same way. The refusal event is now
  written for a run of any tenant, and `MaxAttempts` above 1 counts every
  earlier continuation of another tenant's chain.
- A retention policy with `archive: true` no longer deletes rows it never
  archived. Each batch was read in time order for the `IArchiveSink` and then
  deleted as a separate batch in no order; with more matching rows than one
  batch, rows that never reached the sink were deleted (measured on PostgreSQL,
  SQL Server and SQLite). A run now deletes exactly the rows the sink received,
  by key, after the sink returns. A sink can receive a row more than once.
- Documentation now matches the code: a failed workflow node's message rule and
  the causes of a `Canceled` workflow run, the request options a fallback link
  inherits (temperature, `TopP`, `MaxOutputTokens`, `ResponseFormat` and tools
  carry over; only provider settings do not), the five gates and the waiting
  quota of a tool call, the console editor keeping a harness `loop`, and how a
  job handler learns about cancellation (`JobContext.IsCancelledAsync`). The XML
  of `JobHandlerKeys.Workflow`, `TraconOptions.SkillRoots`,
  `MaxOutputBytes` (per stream), `ToolCallUsage`, `ModelFallback` and
  `ModelBinding` was corrected. A resume from a checkpoint whose graph changed
  now says the graph's structure differs instead of naming one cause.
- Every documented webhook event is now published. `workflow.request.pending`,
  `job.completed`, `job.failed` and `eval.completed` were defined, accepted on
  subscribe and listed on the site, but no code published them, and a workflow
  run produced no `run.*` event at all (its agent steps are child runs). A
  workflow root now publishes `run.completed`/`run.failed` with the run tree's
  tokens and cost, and one `workflow.request.pending` per request it waits on
  (ids only, no prompt). A job publishes `job.completed` or `job.failed` when it
  reaches that status (a cancelled job and a webhook delivery job publish
  nothing). An evaluation run publishes `eval.completed` when it completes, is
  cancelled, or fails on its last attempt. The console's subscription form now
  also offers `run.score.low`.
- Retention and data subject erasure now delete an attachment's bytes in a
  registered `IAttachmentStorage`. Retention's `attachments` target never read
  `external_uri`, so the bytes stayed in object storage for ever; it now deletes
  the bytes before the row, and keeps the row (and tries again on the next run)
  when the byte delete fails. Erasure deletes the bytes after the row deletes
  and before the audit write and the commit, and rolls back when a byte delete
  fails. `IAttachmentStore.DeleteAsync` and `DeleteBySessionAsync` also delete
  the bytes first: a storage failure after the row was gone lost the location
  for good. **Requirement:** an `IAttachmentStorage.DeleteAsync` must succeed for
  a location that no longer exists.
- Retention no longer treats a workflow's attachments as orphaned while a run
  of that session waits for input, is still running, or finished after the
  cutoff. A workflow session has no session row, so an image or a speech clip
  an agent step produced was deleted seven days after it was made, even while
  the run waited in `AwaitingInput`. Ownership also requires the same tenant.
- The continuation that a tool-approval decision queues now runs on the agent
  version of the run that asked for approval, and counts in the same experiment
  arm. It ran the current version, so a version saved while the approval waited
  changed the instructions halfway through the turn. Code agents still run their
  current definition. An automatic continuation after a crash now keeps the
  interrupted run's experiment arm, not only its version.
- A queued run (`Prefer: respond-async`) now uses the request's `culture` and
  takes part in a running experiment. The worker resolved the agent with no
  culture and the current version, so a queued run always got the default
  instructions, ignored the experiment and counted in no arm. The arm is now
  assigned when the request is accepted, with the same key as a synchronous run
  (`sessionId`, else the run id), and the worker runs that arm's version. The
  worker honors an arm in the job payload only when it matches a started
  experiment of the job's tenant and agent with that exact version; otherwise
  the job fails and no model call is made.
- A store error after a job's handler has finished no longer counts as a
  handler failure. The worker wrote the outcome inside the handler's own `try`,
  so one failed database call either failed a job whose work was done — a
  queued agent run (one attempt by default) became `Failed` while its run was
  `Completed` — or, with attempts left, ran the handler a second time. The
  worker now retries the write with a growing pause while it can still prove
  its lease, and stops a third of the lease before that lease could run out; a
  write that keeps failing is logged as an error and the job takes the
  lease-expiry path. A store error while releasing a failed attempt, or while
  failing its last one, no longer leaves the job `Running` until its lease
  expires. In a batch, a store error while recording a finished item no longer
  records that item as `Failed`.
- A workflow run now carries its attribution the way an agent run does. The
  root built its run scope before it read `IRunAttributionContext`, so every
  node and tool of a workflow read `TraconRunContext.Current.UserId` and
  `Labels` as `null` while the run row carried them, and its quota record named
  no run and no user, so a `quota.threshold` webhook for a workflow carried
  `runId` and `userId` as `null`.
- A harness loop with an `aiJudge` criterion now records the judge's tokens in
  the run's `usage`, cost and quota consumption. The judge calls its own model
  on every iteration it reaches, and the framework drops that response: the
  tree budget counted the judge, but the run record, the run's cost and the
  tenant's quota did not.
- The tokens of the context compaction summarizer are now priced at the
  summarizer's own model. They were priced at the agent's model, while the tree
  budget priced them at the summarizer's.
- The tree budget now charges a streamed model call with the sum of its usage
  reports. It kept only the last report, while the run record sums them, so a
  call that reports usage in more than one part was undercounted against
  `MaxTotalTokens` and `MaxTotalCost`.
- A consumer's own speech provider now works with `UseVoice()` as documented.
  With `ISpeechSynthesizer` and `ISpeechTranscriber` registered before
  `UseVoice()` and no voice key, the first read of the voice settings threw:
  `POST /api/voice/speak` called the provider, saved the attachment, then
  answered `500` and left the attachment behind. `Provider`, `ApiKey` and
  `Endpoint` are now validated only while the built-in ElevenLabs client serves
  at least one of the two contracts; a registration added after `UseVoice()`
  fails at startup with a message that says to register first. Three behaviors
  now follow the bound implementation instead of the ElevenLabs settings:
  prices are looked up under its `ProviderName`
  (`Tracon:Pricing:Voice:{ProviderName}:{model}`, and only the built-in client
  falls back to its default model), the `speak` tool applies
  `ISpeechSynthesizer.MaxCharactersPerRequest` as the HTTP endpoint already did,
  and `GET /api/voice/health` uses the synthesizer's own `IVoiceHealthCheck` or a
  voice-list call that reports its `ProviderName`.
- Speech audio saved by `POST /api/voice/speak` is now named after its detected
  type (`.ogg`, `.wav`, `.mp3`) instead of always `.mp3`, and audio a live
  conversation saves with `PersistAudio` is no longer named `.bin`. Speech,
  conversation and image attachments now share one naming rule, and their
  timestamps carry ticks.
- The console can now answer a workflow request that it did not start. The
  run page sent the operator to "the workflow screen", but that screen showed a
  request card only for a run it had started itself, so a run started over
  HTTP, by a job or in another tab could not be answered from the console at
  all. The workflow screen now opens on the run named by `?runId=`. Both links
  on a run page carry it, and the screen writes its own run there, so a reload
  or a copied link keeps the run. A `runId` of another workflow, or of a run
  that is not waiting, shows a note instead of a card. The run page now also
  says that answering an already answered run opens a separate branch.
- A workflow node retry, the model fallback chain and an online evaluation
  judge no longer repeat a permanent client error. `RunErrorClass.ProviderError`
  covers every provider HTTP error, `4xx` included, and the node retry read only
  that class: a node that got a `401` from `EnsureSuccessStatusCode()`, or a
  model call that got a `404 (model_not_found)`, ran
  `WorkflowNodeRetryPolicy.MaxAttempts` times, and a retried `409` on a
  non-idempotent call could repeat its side effect. The fallback chain moved to
  the next provider on any `4xx` other than `401`/`403` that an SDK exception
  type carried, so the next provider got the same bad request, and an online
  evaluation job retried a judge whose model answered `401` or `404`. A failure
  that carries a permanent HTTP status (every `4xx` except `408` and `429`,
  read from `HttpRequestException.StatusCode` or from `HTTP nnn` text in the
  exception chain) is now thrown on the first attempt, never moves to the next
  link, and fails the judge's item without a retry. **Behavior change:** a
  registered `IRunErrorClassifier` can no longer make such a failure retry a
  workflow node; an `IProviderRetryClassifier` that returns `Retry` still
  moves to the next link. `runs.error_class` values are unchanged.
- A harness agent on the OpenAI Chat Completions provider (`openai`,
  `azure-openai` and a compatible registration) no longer fails with `400`.
  The harness sent the provider's hosted web search on every request
  (`unknown_parameter: web_search_options` for a model that does not search;
  it is now off by default, see Changed), and a streamed run lost the
  conversation after its first tool call: the next request carried only the
  system message and the tool result (`messages.[1].role`). The harness's
  history layer wrote a marker onto each streamed update, and the tool loop
  under it, holding the same object, took the conversation for one the
  service stores. Each update now reaches the harness as a copy.
- `HarnessSettings.MaximumIterationsPerRequest` now bounds the tool-call loop.
  It reached the harness's own loop, which never sees a tool call: the loop
  that runs the tools kept its default of 40, so a limit of 2 still made 7
  model calls. The limit now reaches that loop and the loop of every fallback
  model, and at the limit the last call offers the model no function tools.
  A value below 1 is refused when the agent is compiled.
- A fallback model runs the agent's tools with the agent's
  `ModelBinding.AllowConcurrentToolCalls`. The fallback link built its client
  from the provider and model alone, so concurrent tool calls turned off when
  the chain moved to a fallback.
- A workflow run whose node works quietly for a while is no longer cancelled
  by its own silence. The workflow stream wrote nothing between events, the
  console stops reading a stream after 30 seconds of silence, and a closed
  connection cancels the run - so any node that took longer ended the run
  `Canceled`. Every streaming endpoint (agent and workflow runs, resumes and
  answers, and the OpenAI-compatible endpoints) now writes an SSE comment
  whenever it has written nothing for `TraconEndpointOptions.StreamKeepAliveInterval`
  (15 seconds by default; see Added). A workflow run that waits for a
  `MaxConcurrentRuns` slot now has its run record from the start: a run whose
  `RunTimeout` passed while it waited closed the stream with no closing frame
  and left no record behind; it now ends `Canceled` with the timeout error
  like any other timed-out run. Any other error on the workflow stream now
  ends it with an `error` frame instead of a dropped connection.
- The job queue now keeps its lease, attempt and cancel promises. A failed
  lease renewal stopped renewal for the rest of the job, so the lease ran out
  under a working handler and a second worker ran the job alongside it;
  renewal now retries on the next tick and runs at a third of the lease
  instead of half. A job whose worker died was leased again without checking
  its attempt limit, so a queued agent run (one attempt by default) ran twice;
  a job past its limit now fails without running, with the error code
  `tracon.job.attempts-exhausted`. A cancelled job whose handler then threw
  went back to `Pending` and ran again, or became `Failed`; a job that reached
  a final status no longer changes, and a job cancelled between its lease and
  its start does not start.
- A workflow step whose agent cannot answer now fails the workflow. A step
  that ran past `AgentGraph:ChildDeadline`, was refused by the tree budget
  (`MaxTotalRuns`, tokens, cost or time), asked for a tool approval, or whose
  provider never answered returned the refusal sentence as its answer: the
  next node received that sentence as its input and the run ended
  `Completed`. The run now ends `Failed`: a wait limit with the new
  `TraconChildRunTimeoutException` (class `Timeout`), the budget with
  `TraconRunBudgetExceededException` (class `QuotaExceeded`), a provider fault
  with the provider's own error. An agent called as a tool by another agent
  still gets the refusal as text, so its model can react.
- A pending human request of a workflow run can no longer become impossible to
  answer. A long prompt - about 1,400 non-ASCII characters is enough - was cut
  at the event payload limit, the request disappeared from the pending list
  and `respond` refused it; the request is now listed with its prompt cut and
  marked, and it can be answered. Retention deleted the request event and the
  checkpoints of a run waiting for an answer; it now keeps them for as long as
  the run waits. With `KeepCheckpointsAfterCompletion = false`, an answer -
  even one the port rejected - deleted the checkpoints the waiting run needs;
  the cleanup now leaves a session's checkpoints in place while another run of
  the session waits or is running.
- A harness loop run now records the tokens and cost of every iteration. A
  non-streaming loop returned and recorded only its last iteration's usage, and
  a streaming run of any agent recorded only its last model call - a loop, or
  a run with a tool round, undercounted its cost and the tenant's quota. The
  usage of a streamed run is now the sum of its model calls.
- Structured-response validation now checks the agent's answer, not
  everything the response holds. With a loop, or with text the model wrote
  before a tool call, the validator received the joined text and rejected a
  valid final JSON as "not valid JSON". It now validates the last assistant
  message that carries text.
- A parameterized agent no longer runs with its placeholders unfilled. Only
  `POST /api/agents/{name}/run` and evaluations bound parameter values; a
  queued run, a batch, a replay, the OpenAI-compatible endpoints, MCP, A2A, a
  workflow step, an agent called as a tool and voice sent the model the
  literal `{{name}}`, without even the defaults. Every path now binds the
  defaults. An agent with a required parameter that has no default is refused
  where the request enters - `400` with `missingParameters` for a queued run,
  `400` on the OpenAI-compatible endpoints, an error for an MCP or A2A call, a
  failed job for a batch - and fails before any provider call on a path with no
  request to refuse. A replay and an automatic continuation are refused for any
  agent that declares parameters: the recorded input has no parameter values,
  and the defaults would change the instructions. An evaluation of a
  parameterized definition registered in code now binds each case's values. A parameterized definition registered in
  code (`AddAgent(AgentDefinition)`) is now checked and bound on
  `POST /api/agents/{name}/run`; its parameters were refused there as unknown.
- A run pinned to an earlier definition version (an evaluation or an
  experiment) is now validated, priced and recorded as that version. The
  catalog compiled the pinned version but described it with the current one,
  so its response was validated against the current response format, and the
  run recorded the current model, cost and - for an evaluation case - version.
  An evaluation run now records the pinned version's model.
- `GET /api/evals/runs/{id}` no longer fails with `500` on the in-memory store
  when a case's run threw. A free-form JSON field left unset (`Scores`,
  `Checks`, a job's `Payload`) held no JSON at all and could not be written;
  it is now the empty array, as when it is set to nothing.

- A dependency that times out on its own no longer counts as the caller
  cancelling. An HTTP-backed store, sink, catalog, cache or identity seam
  reports `HttpClient.Timeout` as `TaskCanceledException`; 58 catch sites let
  that exception through as if the request had been cancelled, which broke
  each site's own promise. Among the effects: the MCP and A2A startup check
  stopped the host when the agent catalog timed out, a run failed because a
  run store, event sink, response cache or attribution lookup timed out, an
  approved turn never ran because its "don't ask again" rule could not be
  written, a trigger delivery whose queueing timed out could never be retried
  (the retry was refused as a replay), queued runs and evaluation runs stayed
  `Queued`/`Running` forever, and image generation and replay answered `500`
  instead of their documented `502`. Every site now tells the two apart by
  the operation's own token.
- An `IRunEventSink` now receives a run's events in sequence order, as its
  contract states. A run writes from several places at once (concurrent tool
  calls, a child run, a workflow's steps), and an event numbered later could
  reach the store and the sinks before an earlier one. Numbering, the store
  write and the sink dispatch of one event are now a single step. A sink must
  not append to the run it is handling: such an append throws
  `InvalidOperationException` and the sink is disabled for the run, instead
  of waiting for itself. The in-memory run store also returns events in
  sequence order.

- Editing a workflow that uses function nodes in the console no longer erases
  them. The editor opened such a workflow with an empty participant list, and
  saving it after adding an agent replaced the whole chain with that agent. The
  editor now lists the chain read-only, keeps the kind `Sequential`, and sends
  the chain back unchanged.
- Editing a quota in the console keeps a disabled rule disabled. The form sent
  `enabled: true` on every save. The agent and the period of a rule are now
  locked while you edit it: they identify the rule, and changing them wrote a
  second rule.
- Editing a draft experiment no longer removes its canary policy when the
  in-memory store is in use. The SQL stores already kept it.
- Saving a webhook subscription through `PUT /api/webhooks/{name}` now resets
  its consecutive failure count with the SQL stores too, as the in-memory store
  already did. Before, a subscription that was disabled for failing and then
  enabled again went back to disabled on its next single failure.
- A live voice session, a voice conversation and an agent batch job now pick up
  an MCP server change on their next delegation, turn or item. Each resolved
  its agent once and ran with it until it ended, so after a server was edited
  or deleted mid-session every later call went to the closed connection (the
  model heard "Error: Function failed."), and a server switched back to
  requiring approval ran its tools without asking. They now resolve the agent
  again each time; nothing is recompiled while nothing changed. A batch item
  whose agent was deleted meanwhile fails on its own.
- A cached agent now sees an MCP tool's description and input schema as the
  server last listed them. The compiled agent kept the tool definition of the
  moment it was compiled, so after a server changed a tool on the same
  connection, the model kept getting the old description and schema until the
  agent's definition changed or the process restarted.
- One failed `tools/list` call no longer takes an MCP server's tools away. A
  server whose listing failed once lost all its tools at that refresh, and every
  agent that used one was refused as "not registered" until the next good
  refresh (five minutes by default). The last good list is now kept for
  `Tracon:Mcp:StaleToolRetention` (default 15 minutes) after it was read, while
  the server stays registered with the same definition. `TimeSpan.Zero` keeps
  the old behavior.
- A timeout set beyond what .NET timers accept now fails at startup with the
  option's name. `CancelAfter` refuses more than about 49.7 days, and the
  validators refused only zero and negative values, so such a value threw
  where the timeout was armed: on every run of a workflow
  (`Workflows:RunTimeout`), every webhook delivery (`Webhooks:Timeout`), every
  live voice delegation (`Voice:Live:DelegationTimeout`), every MCP connect
  (`Mcp:ConnectionTimeout`, which aborted discovery for every tenant), at
  shutdown (`Drain:Timeout`), and after a skill script had started, which left
  the script running (`Skills:Scripts:Timeout`). The same check now covers
  `Tools:DefaultTimeout`, `Tools:ApprovalPresentationTimeout`,
  `Validation:McpTimeout`, `AgentGraph:ChildDeadline` and `WaitTimeout` (and an
  agent definition's `subAgents` values, at compile time), `Images:Timeout`,
  `Voice:Conversation:IdleTimeout` and `MaxConnectionDuration`, a tool's own
  timeout, and `RunEventPollInterval`. A provider or voice `Timeout`, which
  becomes an HTTP request timeout, must be at most about 24.8 days
  (`int.MaxValue` milliseconds); `TraconClientOptions.Timeout` likewise. A
  `WorkflowNodeRetryPolicy` with a negative delay or multiplier is refused when
  the function is registered, and its growing delay is capped. `tracon eval`
  refuses a `--timeout` or `--poll-interval` it cannot wait for. Values below
  one millisecond are refused too.
- On SQL Server, two requests that save the same new record at the same time
  no longer fail with a unique-key violation. Twenty stores wrote with an
  update-then-insert pair outside a transaction, so both requests could take
  the insert branch: among them sessions, runs, quotas, script grants,
  approval rules, MCP servers, tenants and the singleton lease. Each pair now
  runs inside a transaction, which costs two round trips on SQL Server and
  nothing on PostgreSQL or SQLite. A script grant, quota or approval rule saved
  with an empty string in a nullable key is no longer matched to the record
  saved with `null` there; the HTTP surfaces already store an empty value as
  `null`.
- A provider setting value of the wrong JSON type (for example text where
  `anthropic.thinking.budgetTokens` expects an integer) is now reported with the
  key it belongs to. Through the model provider registry the message was replaced
  with "The model provider request failed.", so `POST /api/agents/validate` and a
  compilation error did not name the key.
- A run that failed because the provider filtered its response
  (`ContentFiltered`) now records the tokens the call spent in its usage, cost and
  quota entry. The run recorder writes no usage for a run that ends in an
  exception, so a filtered run said it cost nothing although its input was billed.

### Added

- Skill scripts can run outside the Tracon host. Register an `ISkillScriptExecutor`
  (new in `Tracon.Abstractions`, with `SkillScriptExecutionRequest`,
  `SkillScriptOrigin` and `SkillScriptExecutionResult`, which moved there from an
  internal Core type without its `Duration`) to run an approved script in a container,
  a micro-VM or a remote runner. The gates, the content pin, the `script.run` audit
  entry, the concurrency quota, a wait limit of `Timeout` plus five seconds and the
  output cap stay with Tracon; the executor decides only where the script runs.
  `UseSkillScripts` registers the built-in executor, a local process as before, as a
  default that a host's own registration replaces in either order;
  `RequireCustomBinding<ISkillScriptExecutor>()` stops the host while the built-in one
  resolves. `Tracon.Testing.Contracts.Xunit` adds `SkillScriptExecutorContract` and
  `ContractCoverage.ScriptContracts`. See the new guide
  [Write your own script executor](https://tracon.dev/guides/write-your-own-script-executor/).
- Multi-speaker dialogue synthesis. `ISpeechDialogueSynthesizer` voices an ordered
  list of turns, each with its own voice, in one request (`SpeechDialogueRequest`,
  `SpeechDialogueTurn`) and returns one audio result with each turn's time range
  (`SpeechDialogueAudio`, `SpeechDialogueSegment`) and the provider's limits
  (`SpeechDialogueCapabilities`). Code takes it from the new
  `ISpeechProviderResolver.GetDialogueSynthesizerAsync(name)`, which reads the
  capability from the registered synthesizer and applies the tenant's egress policy
  and own-key rules; nothing registers the capability in the container. The built-in
  ElevenLabs client implements it with `text-to-dialogue` and its timestamped
  variant, refuses a request over 10 voices or `VoiceOptions.MaxDialogueCharactersPerRequest`
  (new, default 2,000) before the call, and uses `VoiceOptions.DialogueModelId` (new)
  or the provider's default model, never `SynthesisModelId`.
- Provider request settings for speech. `SpeechRequest.ProviderSettings` and
  `SpeechDialogueRequest.ProviderSettings` carry keys under the provider's prefix; the
  built-in client reads `elevenlabs.stability`, `similarityBoost`, `style`,
  `useSpeakerBoost`, `speed`, `seed`, `previousText`, `nextText` and
  `previousRequestIds` (`ElevenLabsProviderNames`) and refuses an unknown key, a
  wrong type, a value out of range, and on a dialogue a key the dialogue service does
  not take. `ModelProviderSettings.Validate` gains an overload that takes the settings
  dictionary, for a speech provider of your own.
- `SpeechAudio.ProviderRequestId` carries the provider's id for a generation (the
  ElevenLabs `request-id` header), to pass in `elevenlabs.previousRequestIds` for the
  next part of a long script.
- A workflow function node can be bounded by a timeout. `AddWorkflowFunction<TInput, TOutput>`
  gains an overload that takes `Action<WorkflowFunctionOptions>`, and
  `WorkflowFunctionOptions.Timeout` sets the registration's timeout; a node's own
  `WorkflowNodeReference.Timeout` (`timeout` in `PUT /api/workflows/{name}`) wins over it.
  When it passes, the handler's token is cancelled and the run ends `Failed` with the new
  `TraconWorkflowFunctionTimeoutException` (error type `workflow_function_timeout`, class
  `Timeout`). The run stops waiting even for a handler that ignores its token; its late
  end is logged and its result is discarded. With a retry policy the timeout applies to
  each attempt. `GET /api/workflows/functions` shows the registered timeout
  (`WorkflowFunctionResponse.Timeout`).
- An agent node of a `Sequential` node list can carry its own `timeout`, which replaces
  `Tracon:AgentGraph:ChildDeadline` for that step; the hard cutoff stays 30 seconds later.
  Past it the run ends `Failed` with `child_run_timeout`, and the message names the node.
- `WorkflowDefinition.RunTimeout` (`runTimeout` in `PUT /api/workflows/{name}`): a
  definition's own run timeout. It may shorten `Tracon:Workflows:RunTimeout`, and may be
  longer only up to the new `TraconWorkflowOptions.MaxRunTimeout`
  (`Tracon:Workflows:MaxRunTimeout`, unset by default); a longer one is refused with `400`
  on save, and a stored one above a ceiling lowered later is cut to it with a warning.
- A job's time limit: `JobRequest.MaxDuration` and `JobRecord.MaxDuration`, and
  `TraconSchedulingOptions.MaxDurationByHandlerKey`
  (`Tracon:Scheduling:MaxDurationByHandlerKey`) for every job of a handler key; the job's
  own value wins. Past it the worker cancels the handler's token and the attempt fails
  with `JobErrorCodes.MaxDurationExceeded` (`tracon.job.max-duration-exceeded`); the job is
  leased again with the full limit while attempts are left, and a handler that returns
  normally after the limit completes the job. The built-in workflow handlers end the work
  instead: the run closes `Canceled` with the error type `job_max_duration_exceeded` and
  is never continued, a queued run's job fails at once, and a batch fails the item. New
  column `jobs.max_duration_ms` (migrations PostgreSQL `0057`, SQL Server `0045`, SQLite
  `0044`). `JobStoreContract` adds
  `A_jobs_own_time_limit_survives_the_round_trip_and_the_lease` and
  `A_job_without_a_time_limit_reads_back_none`; `WorkflowDefinitionStoreContract` adds
  `Node_timeouts_and_the_run_timeout_survive_the_round_trip` and
  `Saving_again_without_timeouts_clears_them`. A third-party store must keep both fields.
- The console's workflow editor has a timeout field on each step of a chain with a
  function step, and a run timeout field.

- A workflow run, resume or answer can be queued: `POST /api/workflows/{name}/run`,
  `POST /api/workflows/runs/{runId}/resume` and `POST /api/workflows/runs/{runId}/respond`
  accept `Prefer: respond-async` and answer `202 Accepted` with the run id
  (`AcceptedRunResponse`, now in the OpenAPI document and both generated clients). The
  caller is checked before the `202` as on the streamed call, the run's row is
  `Queued` until a worker starts it, and `POST /api/runs/{id}/cancel` cancels it from
  the queue. A queued run no longer depends on the connection, so a client that stops
  reading does not cancel it. The job runs under the new built-in key
  `JobHandlerKeys.WorkflowRun` (`tracon.workflow-run`), which no schedule can use.
- `Idempotency-Key` on the three workflow run endpoints, accepted only together with
  `Prefer: respond-async`; a key on a streamed call is refused with `400`.
- `TraconWorkflowOptions.ResumeInterruptedRuns` (off by default) and
  `MaxResumeAttempts` (default 1): a queued workflow run or a `tracon.workflow` batch
  item interrupted by a crash, a deploy or a graceful stop continues from the latest
  checkpoint in its chain when its job is leased again, as a new run whose
  `ContinuedFromRunId` names the interrupted one. The step that was running repeats.
  Turning it on without `EnableCheckpointing` fails at startup. A queued run's job gets
  `2 + MaxResumeAttempts` attempts while it is on: the job's last attempt never starts
  a continuation, because the worker fails a lease past the limit before the handler
  runs; it closes the chain instead. A batch job keeps its own attempt limit and needs
  `2 + MaxResumeAttempts` attempts to get every continuation.
- `RunQuery.ContinuedFromRunId` and `GET /api/runs?continuedFromRunId=` walk a
  continuation chain forward.
- A structured `tracon.workflow` batch item may name a `sessionId`: the run's execution
  session, which its `run.completed` webhook and its continuations carry. An agent batch
  item that names one fails alone.
- `RunStoreContract` adds
  `A_second_start_without_a_continuation_link_keeps_the_first_one`,
  `A_queued_workflow_row_keeps_its_workflow_session_and_attribution_when_it_starts`,
  `Query_filters_by_the_run_a_continuation_continues` and
  `Query_by_continuation_does_not_return_another_tenants_run`. `JobStoreContract` adds
  `A_pending_report_marks_the_item_and_counts_nothing` and
  `A_report_for_a_finished_item_changes_nothing`. A third-party store needs the first
  and the last of these: the second start of a run keeps a stored
  `ContinuedFromRunId`, and a report for a finished item is ignored.
- Named speech providers: `AddSpeechSynthesizer(name, factory)` and
  `AddSpeechTranscriber(name, factory)` register more text-to-speech and
  speech-to-text engines next to the default one, and
  `ISpeechProviderResolver` (registered by `AddTracon()`) returns one by name
  after the tenant's checks — for a workflow function node, for example.
  `SpeechProviderDescriptor` and `SpeechProviderKind` describe a registration. A
  name is 1 to 64 characters of `a-z`, `0-9`, `.`, `_` and `-`; a repeated name
  throws at setup.
- `GET /api/voice/providers` lists the registered speech providers (new in the
  OpenAPI document and both generated clients). `POST /api/voice/speak` takes an
  optional `provider` and `GET /api/voice/voices` an optional `?provider=`; an
  unknown name answers `400`, a request without a name when no default is
  registered `501`.
- Tenant credentials for speech providers: `ISpeechSynthesizer` and
  `ISpeechTranscriber` gain `SupportsTenantCredentials` (default `false`),
  `SpeechRequest` and `SpeechTranscriptionOptions` gain `Credential`, and
  `ISpeechSynthesizer` gains `ListVoicesWithCredentialAsync`, whose default
  refuses a credential. All are default interface members or new init
  properties, so an existing implementation keeps compiling.
- `SpeechAudio.ModelId` and `SpeechTranscript.ModelId` carry the model that did
  the work, and `IVoicePricingReader` gains `ForSynthesis(providerName, modelId,
  characters)` and `ForTranscription(providerName, modelId, duration)` (default
  `null`). A speech call is priced by the model the provider reports, then the
  one the request named; the built-in client reports its model.
- `IWorkflowRunner.ValidateResponseAsync`: checks an answer against the pending
  request before the stream opens. A default interface member, so an existing
  implementation keeps compiling.
- `IRetentionStore.ArchiveBatchAsync` reads one batch, hands it to the archive,
  and deletes exactly that batch. Its default implementation keeps the old
  two-call behavior so an existing store compiles, and can delete rows it did not
  archive — override it. `RetentionStoreContract` adds
  `Archive_batches_delete_only_the_rows_they_archived`,
  `A_failed_archive_write_deletes_nothing` and a
  `SeedOldRowAsync(tenantId, startedAt)` hook.
- `EvalCaseDraft.Parameters`: the parameter values a promoted run was bound
  with. `EvalStoreContract` adds
  `AddCaseAsync_stores_the_parameter_values_of_a_promoted_run`.
- `RunStoreContract.ClaimOrphanedRunsAsync_reports_the_kind_and_the_tree_position_on_the_claimed_record`:
  a custom `IRunStore` returns `Kind`, `ParentRunId` and `Depth` on a claimed
  record, because continuation decides on them.
- `WebhookEvalSummary` and `WebhookEventPayload.Eval`: the payload of the
  `eval.completed` webhook.
- `WebhookRunSummary.Kind`: `Agent` or `Workflow`, on every `run.*` webhook.
- `OpenAIProviderNames.SettingsPrefix` (`openai`) and
  `OpenAIProviderNames.SupportedSettings` (empty).
- `HarnessSettings.EnableWebSearch`: turns on the model provider's hosted web
  search for a harness agent.
- `TraconEndpointOptions.StreamKeepAliveInterval`: the longest a streaming
  endpoint stays silent before it writes an SSE comment. Default 15 seconds;
  keep it below the shortest idle timeout between the server and its readers,
  and below 30 seconds while the console is in use.
- `JobErrorCodes.AttemptsExhausted` (`tracon.job.attempts-exhausted`).
- `TraconChildRunTimeoutException` (error type `child_run_timeout`, class
  `Timeout`), with `AgentName` and `Timeout`.

- A harness loop can fail the run instead of returning a response no criterion
  accepted. `LoopSettings.OnExhausted` (the ceiling is reached while a
  criterion still asks for another iteration) and
  `LoopSettings.OnCriterionError` (a criterion throws) take a `LoopOutcome`:
  `ReturnLastResponse`, the default and today's behavior, or `FailRun`.
  Exhaustion fails with `TraconLoopUnsatisfiedException` (error type
  `loop_unsatisfied`, new class `RunErrorClass.LoopUnsatisfied = 15`); a
  criterion error fails with the criterion's own error. A workflow stops at
  that agent. With `OnExhausted = FailRun` the turn at the ceiling is
  evaluated before the run is failed - one more evaluation, which for
  `aiJudge` is one more judge call - and the `LoopIterationCompleted` event
  gains `failsRun`. JSON takes the names (`"onExhausted": "FailRun"`); an
  unknown name is refused with `400`. A run the loop fails keeps the usage
  and cost of every turn it ran.
- `TraconMcpOptions.StaleToolRetention`.
- `ILiveVoiceProvider.SupportsTenantCredentials` and
  `ILiveVoiceProvider.AttachSessionAsync(LiveVoiceAttachRequest)`, both with
  default implementations, plus `LiveVoiceCreateRequest.Credential` and
  `LiveVoiceAttachRequest`. A live voice provider that sets
  `SupportsTenantCredentials` receives the tenant's own key on both calls of
  a session; one that does not is refused for a tenant with its own binding.
- `ToolApprovalRule.SourceFingerprint`.
- A workflow can declare run parameters, and a run carries values to every
  agent step. `WorkflowDefinition.Parameters`, `WorkflowDescriptor.Parameters`,
  `WorkflowSaveRequest.Parameters`, `WorkflowRunRequest.Parameters`,
  `WorkflowRunHttpRequest.Parameters` and `GetRunParameters()` (an extension on
  `IWorkflowContext`, for a function node). A step receives the values its own
  schema names; an invalid value is a `400` before the stream opens, and a
  participant whose required parameter nothing supplies fails the run before
  its first model call. A resume or an answer to a waiting run reads the values
  back, and is refused when the workflow now declares parameters and the run
  recorded none.
- A queued agent run (`Prefer: respond-async`) accepts `parameters` and
  `documents`, and a batch item can be a structured object
  (`{"message", "parameters", "documents"}`) for `tracon.agent-batch` and
  `tracon.workflow`. `jobs.payload` and the item inputs are not
  content-protected and now hold these values in plain text.
- `RunInputRecord.Parameters`, `ProtectedColumn.RunInputParameters` (`10`) and
  a nullable `run_inputs.parameters` column on all three storage providers
  (PostgreSQL `0056`, SQL Server `0044`, SQLite `0043`). `null` means nothing
  was recorded and `{}` means the run had no values; the retention policy keeps
  the row of a run waiting for a human.
- `RunReplayRequest.Parameters`: values laid over the ones the source run was
  bound with.
- A workflow function can take settings, so one function serves several
  workflows with a different configuration in each.
  `AddWorkflowFunction<TInput, TOutput, TSettings>` (two overloads: a plain one
  and one that takes an `Action<WorkflowFunctionOptions>` for the description
  and the retry policy), `WorkflowNodeReference.Settings` (a JSON object, or
  `null`), `WorkflowFunctionDescriptor.SettingsType` and `SettingsSchema`,
  `WorkflowFunctionResponse.SettingsSchema` (`GET /api/workflows/functions`) and
  `TraconWorkflowOptions.MaxNodeSettingsLength` (16 KiB). A save reads the
  settings into `TSettings`: an unknown field, a missing `required` member, a
  value of the wrong type and a `null` where the member does not allow one are a
  `400` that names the node and the member, as are settings on an agent node, on
  a function that declares none, or that are not an object. The read repeats when
  a run starts, so settings saved against an older shape fail the run by name
  before any node runs. A change applies to the next run, and an older
  checkpoint stays usable. Settings are stored in the definition and the audit
  trail: never put a secret in them. The `settings` member of a stored node is
  also part of the workflow list and the generated clients.
- The console builds the chain of a `Sequential` workflow: add an agent or a
  function (from `GET /api/workflows/functions`), reorder, remove, and edit a
  function step's settings as JSON with the function's fields listed under it.
  The editor writes `nodes` when the chain holds a function and `agentNames`
  otherwise, tells you when a save changes that, and warns that a run waiting
  for input cannot be resumed after a change of pattern, steps or order.
  The server accepts both forms, so a definition saved over HTTP in the other
  form keeps loading.
- `AddLoopEvaluator(kind, Func<IServiceProvider, LoopEvaluator> factory)`: a loop
  stop criterion created from the application's root services. The factory runs
  once, on the first compilation of an agent that names the kind, so it can take
  `IAgentCatalog`; a factory that throws is not cached and fails that compilation
  with the kind named. Each `AddLoopEvaluator` overload has a variant with a
  `description`, which the console shows next to the kind.
- `GET /api/loop-criteria` (`LoopCriterionCatalogResponse`,
  `LoopCriterionKindResponse`, `LoopJudgeModelResponse`, in the OpenAPI document
  and both generated clients): the built-in and code-registered loop criterion
  kinds with their descriptions, and the `aiJudge` judge model's provider and
  model, or `null`. Reader role, `AgentsRead` scope. Listing runs no factory.
- The console's agent editor edits the harness loop: criteria (add, remove,
  reorder, per-kind fields), the iteration ceiling, the fresh-context switch and
  both outcomes. A kind the host does not list is kept and marked. The version
  diff shows the loop.
- A response the model stopped at its output token limit is now its own
  outcome. A structured (`Json` or `JsonSchema`) response fails the run with error
  type `output_truncated` and the new `RunErrorClass.OutputTruncated`
  (`TraconOutputTruncatedException` in-process); no structured-response repair
  turn runs, and the `StructuredResponseRejected` event carries
  `repairSkippedTruncated` when one was left. A plain-text response completes and
  the run writes a `tracon.output.truncated` event
  (`RunEventCustomTypes.OutputTruncated`). Only the turn that ends the run decides;
  a tool call cut off in an intermediate turn does not. The tokens of the cut-off
  call count in the run's usage and cost. The new enum member is in the OpenAPI
  document and both generated clients.
- Anthropic provider settings `anthropic.effort` (`output_config.effort`:
  `low`, `medium`, `high`, `xhigh`, `max`), `anthropic.thinking.mode` (`enabled`
  or `adaptive`), `anthropic.promptCaching.ttl` (`5m` or `1h`) and
  `anthropic.promptCaching.placement` (`request` or `system`, which marks the end
  of the agent's instructions so requests with different documents share the
  cached prefix). A wrong value, a cache setting without
  `anthropic.promptCaching = true`, and a thinking budget with `adaptive` mode are
  errors. Nothing changes for an agent that sets none of them.
- `ModelDescriptor.SupportsSampling`, read from a provider's `Models` catalog
  entry. When it is `false`, a definition that sets `Temperature` or `TopP` (on
  the binding or a fallback link's model) fails to compile, and
  `POST /api/agents/validate` reports `sampling_unsupported` at
  `model.temperature` or `model.topP`. `GET /api/models` returns
  `supportsSampling`; `null` means unknown and checks nothing.
- `POST /api/agents/validate` warns with `output_truncation_risk` when a binding
  leaves `MaxOutputTokens` empty, the provider sends a lower default limit, and the
  agent asks for a structured response or high reasoning effort. A provider tells
  its default through `ModelOutputLimit`, returned from the chat client's
  `GetService`; the Anthropic provider does.
- `ModelProviderSettingsContract.InvalidSettingValue` and the scenario
  `A_supported_setting_with_an_invalid_value_is_rejected`: a provider that
  declares a key with a closed set of values proves that a wrong value is
  rejected and named. It is skipped when the property is left `null`.

### Changed

- **Behavior change:** skill script results read differently. A timed-out script now
  reports `The script timed out and was stopped.` (was `… and the process tree was
  terminated.`); a script whose executor did not return within `Timeout` plus five
  seconds reports that it may still be running, and its tool call error is `Timed out;
  the script executor did not return in time.` The truncation notice is now one line at
  the end of the result, `[Tracon: output truncated at the N-byte limit per stream.]`,
  added only by the runner, which also caps each stream again. An executor that throws
  fails the call with only the exception type in the tool call record and the model's
  view; the message goes to the server log.
- **Behavior change:** `TraconSkillScriptOptions.MaxScriptContentLength` now limits a
  script file under `SkillRoots` too. A file larger than the limit, an empty file, or
  anything that is not a regular file (a FIFO, a link to a device) is refused from its
  metadata before it is opened and is written to the audit trail as `script.denied`.
  Before, a FIFO blocked the call while it held a concurrency slot, and a link to
  `/dev/zero` was read without end. A stored script whose name is not a plain file name
  is now refused the same way, before the `script.run` entry.
- **Behavior change:** `TraconSkillScriptOptions.Timeout` accepts at most about 49.7 days
  minus five seconds; a larger value fails at startup, because the runner's wait limit
  adds five seconds to it.
- `Tracon.Testing.Contracts.Xunit`: `ModelProviderSettingsContract`'s invalid-value
  case now also fails when the refusal message repeats a text `InvalidSettingValue`.
  **Behavior change:** a provider that echoes the rejected value fails the case;
  name the key and the accepted values instead.
- `ISpeechProviderResolver` gains `GetDialogueSynthesizerAsync`. The member has no
  default implementation: a resolver of your own must implement the dialogue gate.
- **Behavior change:** `POST /api/jobs/{id}/cancel` now reaches a running handler. The
  worker reads each running job's status every `Tracon:Scheduling:PollInterval` and
  cancels the handler's token when it finds the job cancelled or gone; before, a running
  job ran on until its handler called `JobContext.IsCancelledAsync`. This costs one store
  read per running job per poll interval. A job cancelled this way stays `Cancelled`.
- **Source break:** `builder.AddWorkflowFunction<TInput, TOutput>(name, factory, null)`
  no longer compiles (`CS0121`): a `null` third argument fits both the description
  parameter and the new `Action<WorkflowFunctionOptions>` overload. Name the argument
  (`description: null`) or leave it out.

- The generated C# client `TraconApiClient` changes three method signatures.
  `TraconListRunsAsync` gains a `continuedFromRunId` parameter before `skip` and
  `take`, `TraconVoiceListAsync` gains a `provider` parameter and
  `TraconEraseDataSubjectAsync` a `force` parameter before the cancellation token.
  The new parameters are optional, so a call that names its arguments compiles
  unchanged. A call that passes `skip`, `take` or the
  cancellation token by position no longer compiles; name the argument. Code
  compiled against the previous package must be rebuilt.
- **Behavior change:** the streamed workflow endpoints now check their input
  before the stream opens. `POST /api/workflows/{name}/run` with an invalid
  `sessionId`, `resume` of an unknown run or checkpoint, and `respond` to a run
  that does not wait for input answered `200` and reported the error inside the
  event stream; an unknown checkpoint also opened a new `Failed` run row. They
  now answer `400` (invalid session id, unknown checkpoint, a run that does not
  wait) or `404` (unknown run, or a run of another tenant) as problem details, and
  open no run.
- **Behavior change:** the names `functions` (workflows) and `handler-keys`
  (schedules) are reserved. `GET /api/workflows/functions` and
  `GET /api/schedules/handler-keys` are matched before `{name}`, so a definition
  saved under one of these names could never be read back. Saving one now answers
  `400`, and `AddWorkflow` throws `ArgumentException`. The check ignores case.
- The console's tenant provider panel offers speech providers next to the model
  providers, by the provider name a policy and a binding use. A name saved in a
  policy that no provider reports any more stays listed and can be removed;
  before, it was invisible and sent back on every save.
- `IWorkflowFunctionCatalog` gains `ValidateSettings(string name, JsonElement? settings)`,
  which returns a message that names the function and the problem, or `null`. An
  implementation of the interface outside Tracon must add it; the registry
  `AddWorkflowFunction` builds is the only implementation Tracon ships.
- `Tracon.Testing.Contracts.Xunit` adds two `WorkflowDefinitionStoreContract`
  cases: a function node's `Settings` survive the round trip, and saving the
  definition again without them clears them. A custom `IWorkflowDefinitionStore`
  that rebuilds a node from its own columns and drops the field now fails them.
- The generated C# client documents the 41 nullable object properties whose
  description was lost on the way from the OpenAPI document (for example
  `AgentDefinition.Harness`).
- Every endpoint that starts a run now refuses an attribution that breaks a
  label limit with `400` before the run starts, as the agent run endpoint
  already did: workflow run, resume and respond, run replay, the inbound
  trigger endpoint, and `/v1/chat/completions` and `/v1/responses` (in the
  OpenAI error shape, `type: invalid_run_attribution`). These endpoints
  started the run with no user and no labels, and answered `200`.
- A run that spends tokens on more than one model (a compaction summarizer or
  an `aiJudge` judge on a model other than the agent's) prices each model's
  share at that model's price, and `cost` is the sum. The three
  `*PricePerMillionTokens` fields of such a run are `null`: one rate cannot
  describe the total. A model with no price adds nothing to the cost and is not
  priced at the agent's rate. The cost is `Unknown` only when no model of the
  run has a price. `POST /api/stats/recalculate-costs` does not re-price such a
  run when any of its models had a price. A run whose summarizer or judge runs
  on the agent's own model is unchanged.
- `UseVoice()` and `UseOpenAILive()` now validate their settings when the
  application starts. Both validators already said so, but neither registration
  asked for it, so an invalid value — a missing ElevenLabs key, a headerless
  `OutputFormat`, a zero `MaxAppendCharacters`, a missing OpenAI key — surfaced at
  the first voice request or live session. A host registered with such a value
  now fails at startup, and the message names the setting.
- The OpenAI provider now rejects every `ModelBinding.ProviderSettings` key, as
  Azure OpenAI already did. The rule covers `openai`, `openai-responses` and
  every `UseOpenAICompatible()` registration; their settings prefix is `openai`
  (`OpenAIProviderNames.SettingsPrefix`) and `OpenAIProviderNames.SupportedSettings`
  is empty. Before, the provider never read the dictionary, so a key, for example
  `anthropic.promptCaching` left over after a switch from Anthropic, was ignored
  silently: the agent saved and ran without the behavior its author wrote. Now
  `POST /api/agents/validate` reports `invalid_setting` at
  `model.providerSettings`, saving the definition returns `400`, and a stored or
  code-defined agent with such a key fails compilation. Remove the key with
  `PUT /api/agents/{name}` or in code. The console does not edit
  `providerSettings` and keeps the stored value, so a console save of such an
  agent fails until the key is removed through the API.
- `ModelProviderSettingsContract.SupportedSetting` (Tracon.Testing.Contracts.Xunit)
  is now `virtual` and returns `KeyValuePair<string, JsonElement>?`, default
  `null`. Derive the contract for every provider; a provider that supports no
  key leaves it `null`, and the two scenarios that need an accepted key report as
  skipped. An existing override fails with `CS0508`; add `?` to its return type.
- A harness agent no longer gets the provider's hosted web search by default.
  Set `HarnessSettings.EnableWebSearch` to turn it on; the search runs at the
  provider and is billed by it, and the Chat Completions surface accepts it
  only for search models. A definition that sets both `EnableWebSearch` and
  `DisableWebSearch` is refused when it is compiled. The console's harness
  section shows "Enable web search" in place of "Disable web search".
- A failed workflow run's `error.type` is the same stable identity an agent
  run records - `loop_unsatisfied`, `run_budget_exceeded`, `upstream_error` -
  instead of the exception's CLR name, and the run now carries an error class
  and fingerprint. A consumer that matched a CLR name such as
  `Tracon.TraconLoopUnsatisfiedException` on a workflow row matches the
  identity instead.
- A non-streaming harness loop returns the final iteration's answer; its usage
  stays the sum of every iteration. A streaming loop still streams every
  iteration. Each iteration remains on the run as a `LoopIterationCompleted`
  event.
- A code workflow built with `GetWorkflowAgent` fails at a step that cannot
  answer, as the defined workflows do (see Fixed).
- A job's lease is renewed at a third of `Scheduling:LeaseDuration` instead of
  half, so the longest accepted lease is about 149 days.
- `IAgentDecorator.Decorate` receives the descriptor of the definition version
  the agent was compiled from - the pinned version on a pinned resolve.
- `Tracon.Testing.Contracts.Xunit` adds two `JobStoreContract` cases:
  `CompleteAsync` and `ReleaseForRetryAsync` leave a cancelled or finished job
  unchanged. A custom `IJobStore` that overwrites a final status now fails
  them.

- `CompiledAgentCache` holds one entry per tenant, agent name, definition and
  culture. A compile under a new dependency fingerprint (skills, egress policy,
  MCP tool bindings) replaces the entry instead of adding one, so the cache no
  longer grows when a remote server edits its tools; `Count` counts these
  entries.
- The `AddGeneratedTools()` extension that the `[TraconTool]` generator writes
  into your assembly is now `internal`. It was `public`, so a host that
  referenced a class library with tools saw the library's copy next to its
  own, and `AddGeneratedTools()` failed with `CS0121` (or with `TRC0005` in a
  host without tools). A library hands its tools to a host through its own
  public extension that calls `AddGeneratedTools()` from inside; a host that
  called a library's generated extension directly calls that library
  extension instead. The `TRC0005` message now says so.
- `Tracon.Testing.Contracts.Xunit` adds two `ToolApprovalRuleStoreContract`
  cases: `SourceFingerprint` round-trips, and two rules that differ only in it
  are two rules. A custom `IToolApprovalRuleStore` that drops the field or
  leaves it out of the rule's identity now fails them.
- `Tracon.Testing.Contracts.Xunit` adds three store contract cases.
  `ExperimentStoreContract` requires that `SaveAsync` keep the canary policy of
  a draft and never write one itself. `WebhookStoreContract` requires that
  `SaveSubscriptionAsync` write the failure count the subscription carries. A
  custom store that does otherwise now fails them.
- **A replay, an automatic continuation and a resume after a tool approval no
  longer refuse an agent that declares parameters.** They bind the values the
  source run recorded (a replay request can change some of them) and refuse a
  source that recorded none. Before, they refused every such agent. If you
  upgrade with an approval pending on a queued run of a parameterized agent
  that predates parameter recording, the decision cannot be applied and says so.
- The batch item rule changed: an item that is a JSON object with a string
  `message` member is now read as a structured item instead of as plain text.
  A text item whose content is such an object is the one ambiguity; the stored
  item keeps no type. A malformed structured item fails alone, and the schedule
  endpoints refuse it when it is saved or triggered. A schedule you saved
  before this release that holds such an item with a member the shape does not
  name (`{"message": "...", "user": "bob"}`) is not checked again; its item
  fails on the next run instead of running as text.
- An inbound trigger that targets a workflow now stores the sender's text as
  `{"message": "<text>"}`. The body of a webhook is not yours: read as a batch
  item, a body such as `{"message": "...", "parameters": {...}}` would have let
  the sender choose the workflow's parameter values. The job item's input shows
  the wrapped text, and the run still gets the body as its message.
- Online evaluation and `POST /api/runs/{id}/judge` skip a run whose recorded
  input has no messages, as they skip a run with no recorded input. A workflow
  run that declares parameters now has such a row (it holds the values).
- `GET /api/runs/{id}/input` answers `200` with an empty message list for a
  workflow run that recorded parameters; it answered `404` before. The values
  are not returned there. A replay of a workflow run answers `400` and points
  to resume.
- `POST /api/agents/validate` reports every loop problem with its own code
  (`loop_criteria_empty`, `loop_max_iterations_invalid`, `loop_outcome_invalid`,
  `loop_criterion_kind_missing`, `loop_criterion_unknown`, `loop_marker_missing`,
  `loop_judge_criteria_missing`, `loop_judge_unconfigured`,
  `loop_judge_unavailable`) and the field's path, for example
  `harness.loop.criteria[1].marker`. Before, it reported the first loop problem
  only, as a path-less `compilation_error`. Saving such a definition returns `400`
  with every loop message, and a compile error lists them all.
- An `aiJudge` loop criterion with a blank entry in `judgeCriteria` is now
  refused, with `loop_judge_criteria_missing` at the entry's path
  (`harness.loop.criteria[i].judgeCriteria[j]`). Before, such a definition
  compiled, and the judge received a criterion with no text to check. Remove the
  blank entries; the console drops them when you leave the field.
- A structured response cut off at the output limit no longer completes with a
  partial JSON document (validation off) or fails as `StructuredResponseInvalid`
  after spending every repair turn on the same limit (validation on); it fails
  once as `OutputTruncated`. A dashboard or alert rule that counted these runs
  under `StructuredResponseInvalid` needs the new class.
- Online evaluation does not retry a judge whose model answer was cut off at
  the output limit. The item fails with `judge_failed` on the first attempt, as
  for a permanent `4xx`; before, the job sent the same request with the same
  limit until its attempts ran out. Raise the judge binding's `MaxOutputTokens`.

## [1.0.0-preview.3] - 2026-09-24

The first security release. It closes the tenant-boundary and
credential-exposure defects of the earlier previews listed under Security,
and it changes the public API: every removed or changed type is named below.
Read Security and Changed before you upgrade.

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
- A script grant now pins the content it was given for. A stored script, and a
  script of a skill registered in code, runs only while its content still has
  the hash the grant carries; a change stops it until it is granted again. A
  grant without a script name pins the whole script set, so adding, removing or
  changing any script stops them all. **An existing grant pins nothing and no
  longer authorizes a stored or code script: grant each one again.** Read
  `scripts[].contentHash` or `scriptSetHash` from `GET /api/skills/{name}` and
  send it as `expectedContentHash` to `POST /api/skill-script-grants` (missing
  or malformed `400`, unknown script `404`, changed content `409`). Scripts
  read from disk are not pinned and keep working; keep their directories
  read-only. Before, an administrator who could write skills could replace a
  script's content after a security administrator had approved it, and the old
  grant kept authorizing the new code.
- In a multi-tenant host, granting a stored script needs platform authority
  (an API key that also carries `PlatformAdmin`, the static `AuthToken`, or the
  `TraconPolicies.PlatformAdmin` policy; `403` otherwise). The script runs
  under the server's own operating-system identity and can read other tenants'
  data, so the grant is an installation decision, not a tenant's.
- A store of your own must keep `SkillScriptGrant.ContentHash`. A store that
  drops it fails the new `SkillScriptGrantContract` cases, and its grants
  refuse every stored script.
- A credential header of an MCP server or a webhook subscription is declared
  by configuration key name in the new `headerConfigurationKeys`, and a save
  whose plain `headers` carry a credential-looking name (`Authorization`,
  `Cookie`, a name containing `api-key`, `token`, `password` or `secret`, or a
  name ending in `-key`) returns `400`. The detail names the header and the new
  field and never repeats the value. Before, `Authorization` was the only
  header with a key reference, and `X-Api-Key` or `Ocp-Apim-Subscription-Key`
  was stored in the database as plain text. A stored row keeps working and is
  still sent; each connection or delivery logs a warning that names the header.
  Every key name follows the prefix and tenant rules above, on save (`400`) and
  again before each connection or delivery: an MCP server whose map names
  another tenant's key does not connect, and such a webhook delivery is dropped.
- A header name may appear only once across `headers` and
  `headerConfigurationKeys`, compared without regard to case, and must be a
  valid HTTP field name (`400` otherwise). Turning OAuth on for an MCP server
  whose kept plain `headers` still carry `Authorization` returns `400`: the MCP
  client skips its bearer token while that header is present. A webhook's two maps together may
  carry at most `Tracon:Webhooks:MaxExtraHeaders` entries (`400` instead of
  being cut at delivery), and `headerConfigurationKeys` may not name an
  `X-Tracon-*` header.
- `PUT /api/mcp-servers/{name}` and `PUT /api/webhooks/{name}` keep a stored
  header map when the request leaves it out or sends `null`; `{}` removes it.
  Saving from the admin UI therefore keeps the headers. The side effect: a save
  that changes only the address sends the kept headers, and the credential
  values they resolve, to the new address; the server logs a warning with the
  header names. Send the maps, or `{}`, in the same save when an address moves.
- An `OperationCanceledException` that an `IRunAuthorizationHandler` throws,
  but that the request itself did not cause (for example, an `HttpClient`
  timeout inside the handler), is now a failed check. A single resource answers
  the same `404` as a missing one. Before, it answered `500`, which revealed
  that the resource exists.

### Added

- `HeaderConfigurationKeys` on `McpServerDefinition`, `McpServerRequest`,
  `WebhookSubscription` and `WebhookSaveRequest`: a map from a header name to
  the configuration key its value is read from. The value is resolved on each
  connection or delivery and never stored; a response returns the map as
  stored. The `mcp_servers` and `webhook_subscriptions` tables gain a
  `header_configuration_keys` column (migration `0054` PostgreSQL, `0042` SQL
  Server, `0041` SQLite); existing rows read an empty map.
- `McpServerStoreContract.Header_configuration_keys_are_preserved`,
  `WebhookStoreContract.Header_configuration_keys_are_preserved` and
  `WebhookStoreContract.Header_names_that_differ_only_in_case_do_not_fail_the_save`.
  A store of your own must persist the new field and return it from the save
  as well as from a read.

### Fixed

- `Tracon.UI` now carries the notices of the third-party code in its embedded
  console: React, React DOM, `scheduler`, TanStack Query, `openapi-fetch`, and
  Tailwind CSS, all MIT. `THIRD-PARTY-NOTICES.txt` ships at the package root and
  inside `Tracon.UI.dll`. Before, the package redistributed that code without
  them.
- A webhook subscription whose headers differ only in case (`X-A` and `x-a`)
  no longer fails the SQL save with `500`, and such an MCP server no longer
  fails to connect.
- The MCP OAuth authorization flow now sends the same headers as every other
  connection to the server, including the ones resolved from configuration.
- A header that the MCP transport cannot add (a content header such as
  `Content-Type`, or a value with a line break) is left out with a warning.
  Before, the transport's exception carried the header value into the log.
- The `TRC0201` analyzer names the header for a literal secret in a header
  dictionary (`McpServerDefinition.Headers["X-Api-Key"]`) and points at
  `HeaderConfigurationKeys`.
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
- `Tracon.Anthropic`, `Tracon.Azure`, `Tracon.Google`, `Tracon.OpenAI`:
  `AnthropicChatClientFactory`, `AnthropicModelCatalog`,
  `AzureOpenAIChatClientFactory`, `AzureOpenAIModelCatalog`,
  `GoogleChatClientFactory`, `GoogleModelCatalog`, `OpenAIChatClientFactory`,
  `OpenAIModelCatalog`, and `OpenAIApiSurface`. The `Use*` registration methods and the `*ProviderOptions` types are the
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
- `Tracon.Abstractions` (3 more): `QuotaDecision`, `QuotaThresholdCrossing`,
  and `SqlPersistenceRegistrationMarker`. Only the SQL provider packages
  register the marker; a marker registered by other code made background
  services and approval requests wait for a migration that never ran.
- `Tracon.Core` (16 more): `AgentSkillCatalog`, `CallableAgentResolver`,
  `ContentGuardPipeline`, `ModelProviderCircuitBreaker`,
  `ModelProviderHealthCache`, `ProviderConcurrencyLimiter`, `QuotaEnforcer`,
  `RunSampleRequest`, `RunSampler`, `RunTraceCollector`,
  `SandboxedSkillScriptRunner`, `SkillScriptSupport`,
  `TenantProviderCredentialResolver`, `ToolApprovalPresenterRunner`,
  `TraconLoopEvaluatorRegistration`, and `TraconMetrics`. Metrics stay
  available through the `TraconDiagnostics.MeterName` meter, loop evaluators
  through `AddLoopEvaluator`, and quotas through `TraconQuotaOptions` and the
  quota endpoints.
- The public constructors of the service types that the container or the run
  pipeline builds are now `internal`: `RunRecordingAgent`,
  `AgentDefinitionCompiler`, `TraconDiagnosticsCollector`,
  `ModelProviderRegistry`, `SandboxedSkillScriptRunner`, `ChildAgentInvoker`,
  `ContentGuardPipeline`, `RunEventWriter`, `AgentSessionManager`,
  `QuotaEnforcer`, `ModelProviderHealthCache`, `RunSampler`,
  `SkillScriptSupport`, `ModelProviderCircuitBreaker`, and `TraconMetrics`.
  Each took optional parameters, so a new dependency after `1.0.0` would have
  needed one more overload. Resolve the service that `AddTracon()` registers:

  ```csharp
  // Before (1.0.0-preview.1 / preview.2)
  var registry = new ModelProviderRegistry([provider]);

  // After: resolve the service Tracon registers
  var services = new ServiceCollection();
  services.AddTracon().AddModelProvider(provider);
  using var serviceProvider = services.BuildServiceProvider();
  var registry = serviceProvider.GetRequiredService<IModelProviderRegistry>();
  ```

  `RunRecordingAgent` wraps every agent that `IAgentCatalog.ResolveAsync`
  returns, and `ChildAgentInvoker` wraps each callable agent of a compiled
  definition. The run writer's lifecycle methods
  (`RunEventWriter.StartAsync`, `RunEventWriter.CompleteAsync`,
  `RunEventWriter.RecordToolInvocationAsync` and
  `RunEventWriter.CompleteLateToolInvocationAsync`) are now `internal` too:
  code inside a run reads the writer from `TraconRunContext.Current` and keeps
  `AppendAsync` and the read-only properties.

### Changed

- `Tracon.AspNetCore` now depends on exact versions of its five pre-release
  packages: `Microsoft.Agents.AI.Hosting`, `.Hosting.A2A`, `.Hosting.AspNetCore`,
  `.Hosting.OpenAI`, and `A2A.AspNetCore`. Before, each was a lower bound, so a
  newer Hosting preview restored without a warning and failed at run time: in
  `1.22.0-preview`, `Microsoft.Agents.AI.Hosting.AgentSessionStore` moved out of
  the Hosting assembly (`TypeLoadException`). A direct reference to another
  version now reports `NU1608` at restore, and a transitive one `NU1107`. Move
  to a newer Hosting preview together with the Tracon release built against it.
- `TraconToolRegistration` takes only the tool in its constructor. Every other
  setting is an `init` property with the same default as before, so a setting
  added later does not change the constructor again:

  ```csharp
  // Before (1.0.0-preview.1 / preview.2)
  new TraconToolRegistration(fn, requiresApproval: true, maxOutputBytes: 4096);

  // After
  new TraconToolRegistration(fn) { RequiresApproval = true, MaxOutputBytes = 4096 };
  ```

  The old named arguments fail to compile with `CS1739`, which names the
  argument. `AddTool` and `AddScopedTool` with `ToolRegistrationOptions` do not
  change.
- `ITraconBuilder` now declares only `Services`. Its 27 registration methods
  (`Configure`, `AddTool`, `AddScopedTool`, `AddToolsFrom`, `AddAgent`,
  `AddSkill`, `AddAgentSource`, `AddAgentDecorator`, `AddRunJudge`,
  `AddModelProvider`, `AddEvalCheck`, `AddLoopEvaluator`,
  `RequireCustomBinding` and `RequireProductionProfile`) are extension methods
  on the new `TraconBuilderExtensions` class, in the `Tracon` namespace next to
  `AddTracon()`. A new registration method therefore never changes the
  interface. Calling code does not change; `using Tracon;` is enough.
  - An assembly compiled against an earlier `1.0.0-preview.N` release
    (`1.0.0-preview.1`, `1.0.0-preview.2`) that creates a
    `TraconToolRegistration` or calls an `ITraconBuilder` method throws
    `MissingMethodException` until it is compiled again. This includes the code
    the `[TraconTool]` source generator wrote into your assembly: the exception
    comes from the `AddGeneratedTools()` call.
  - Upgrade every Tracon package together. A provider, MCP, Voice or Testing
    package from an earlier preview next to the new `Tracon.Core` fails while
    it registers, for example with `MissingMethodException` inside
    `UseOpenAI()`, before the start-up version check can run.
  - An `ITraconBuilder` implementation of your own needs only `Services`. An
    explicit implementation of a removed method (`ITraconBuilder
    ITraconBuilder.AddTool(...)`) fails to compile with `CS0539`; delete it. A
    public method with the same name still compiles, but it runs only when the
    caller holds your concrete type. Through `ITraconBuilder`, Tracon's
    extension method runs and writes to your `Services`, so a test double must
    return a real service collection from it.
  - Every registration method now throws `ArgumentNullException` for a `null`
    chain (`NullReferenceException` before).
  - An extension method of your own with the same signature as one of these
    no longer loses to the interface member. Declared in a namespace nearer to
    the call than `Tracon` (for example in your own `namespace MyApp`), it now
    runs instead of Tracon's method, with no warning. Imported at the same
    level as `Tracon`, the call is ambiguous (`CS0121`). Rename or remove such
    an extension.
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
- `SandboxedSkillScriptRunner.RunStoredScriptAsync` takes the skill and the
  script name (`RunStoredScriptAsync(AgentSkillDefinition skill, string
  scriptName, JsonElement? arguments, CancellationToken cancellationToken)`),
  so the runner compares the grant with the content it is about to run. The
  `(string skillName, AgentSkillScriptDefinition script, ...)` overload is gone.
- `GET /api/skills/{name}` resolves the name the way the runtime does: a skill
  registered in code wins over a stored one with the same name, and the new
  `origin` field says which one was returned. A name that exists only in code
  now returns the skill instead of `404`. Each script carries the new
  `contentHash` and the skill the new `scriptSetHash`; both are computed, and a
  value sent in a request is ignored.
- `PUT /api/skills/{name}` answers `409` for a name registered in code with
  `AddSkill`, the same rule as a code-defined agent: the code skill wins the
  name, so a stored skill under it never ran. Before, the save succeeded and
  wrote a record that never ran. `DELETE` still removes such a stored copy and
  answers `409` when the name has none. The console opens a code skill read
  only.
- The in-memory script grant store keeps a revoked grant, with `revokedAt`
  set, the same as the SQL stores. Before, it deleted the row, so
  `GET /api/skill-script-grants` lost the record of who granted what.
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

### Deprecated

- `McpServerDefinition.AuthorizationConfigurationKey` and
  `McpServerRequest.AuthorizationConfigurationKey`
  (`authorizationConfigurationKey` on the wire). They still save, resolve and
  read back, and are removed in `1.0.0`. Use
  `HeaderConfigurationKeys["Authorization"]`; a record that names
  `Authorization` in both, or in the new map while OAuth is on, returns `400`.
  Code that sets the field compiles with warning `CS0618`.
- `QuotaUsageQuery.AsOf`. No store ever applied it, and a store cannot turn an
  instant into a period without the configured time zone. Use
  `QuotaUsageQuery.PeriodStarts`.
- The `net8.0` and `net9.0` targets. Microsoft support for .NET 8 and .NET 9
  ends on 2026-11-10, and the first Tracon release after that date drops both
  targets from every package; `net10.0` stays. An urgent security release may
  still carry them. Move an application that targets `net8.0` or `net9.0` to
  `net10.0` before you take that release.

A version section is not written ahead of time. At tag time this heading is
renamed to the version and the date it shipped on, and a fresh empty
`## [Unreleased]` is opened above it, so a section always names artifacts that
actually exist. Until then the release rehearsal and the GitHub release body
both read the notes from here.

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
