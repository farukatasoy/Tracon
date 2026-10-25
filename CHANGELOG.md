# Changelog

All notable changes to Tracon are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [Unreleased]

### Added

- **A front end that speaks the AG-UI protocol can run a Tracon agent.** The new package `Tracon.AGUI` adds
  `MapTraconAGUI()` (`POST /tracon/agui/{agentName}`, after `MapTracon()`): the body is an AG-UI `RunAgentInput`, the
  answer is the AG-UI event stream. It is a Tracon run: recorded, tenant-scoped, role-, key-scope- and
  session-owner-checked, counted against the quota and priced, with the same protection as `MapTracon`. The thread is the
  session, and only the messages after the last assistant message are new input. A tool the front end declares never
  becomes a tool the model can call: a client-side tool registered with `AddClientTool` and named in the agent runs on the
  front end with the schema from the code, a carried client-side tool the request does not declare is `400`, and a
  declared tool the agent does not carry is ignored and recorded on the run (`RunEventCustomTypes.AGUIToolsIgnored`). A
  tool that waits for approval, and an MCP server's request for input, end the run with an interrupt; the answer is the
  next request's `resume`. The approval is audited as `approval.decision` with `"surface": "ag-ui"`, before it is
  applied. The first event after `RUN_STARTED` is a `CUSTOM` event named `tracon.run` that carries the Tracon run id. The
  package takes the AG-UI SDK (`AGUI.Server`, `AGUI.Abstractions`) and is not part of the `Tracon` meta package. See
  [AG-UI front ends](https://tracon.dev/guides/ag-ui/).

- **A tool can ask the user for information in the middle of a call (MCP elicitation).** Tracon now tells
  every MCP server it connects to that it can answer such a request, with form fields only. In a run started with
  `POST /api/agents/{name}/run` (or over the AG-UI endpoint) and a `sessionId`, the request stops the run as `AwaitingInput`: the stream sends an
  `inputs` event right before `done` (a response with `Idempotency-Key` carries `inputs` in its body), one entry per
  request with `requestId`, `toolName`, `serverName`, `message` and `requestedSchema`. Answer in the next request of the
  session with `AgentRunRequest.InputResponses` (`ToolInputResponse`: `RequestId`, `Action` of `ToolInputAction`
  `Accept`, `Decline` or `Cancel`, and `Content` for `Accept`). The tool call then runs again from its start with the
  answer, so a server must take the same call twice; the replay asks no second approval. Every other request gets
  an explicit `decline` and, inside a run, a `tracon.tool.input.declined` run event (`RunEventCustomTypes.ToolInputDeclined`)
  that names the reason: a run without a session, an OpenAI-compatible call, an A2A task, an MCP tool call, a queued run, an
  agent called by another agent, a request to open an address (`url` mode), and a server on the earlier protocol. A tool
  call stops for input at most once: if the server asks something else the second time, it gets `decline`, never the
  old answer. A wrong answer is `400` (unknown or repeated `requestId`, a field the server did not ask for, a required
  field left out, a value of the wrong type) or `409` (already answered); another user's session is `403`, as for every
  run. The values go to the server and nowhere else. The audit entry `tool_input.answer` names the request, the server,
  the tool, the action and the names of the fields, never a value, and it is written before the call runs again: it is the
  **eighth fail-closed audit operation** (the published set of seven grows by one), so an answer that cannot be recorded
  is not applied. The playground shows a form made from the requested schema and says which server the answer goes to.
  `RunStatus.AwaitingInput` now also appears on the root agent run of a suspended call, and its `RunAwaitingInput` event
  carries `requestId`, `toolName` and `serverName`.

- **An agent can keep files in a private workspace for each session.** Set `AgentDefinition.Workspace`
  (`WorkspaceSettings`: `Enabled`, `ReadOnly`, `DisableReadApproval`; also `AgentDefinitionRequest.Workspace`,
  `AgentDescriptor.Workspace` and `workspace` in the HTTP body). The agent then lists, reads, searches, writes,
  edits and deletes files with the eight `file_access_*` tools of the Microsoft Agent Framework, in an area that
  belongs to one session of one agent: another session does not see the files, and they are not the agent's file
  memory. Tracon runs each tool through the same chain as a tool registered in code: your
  `IToolAuthorizationHandler`, argument validation, the tool timeout and the approval step. The tools that change a
  file (write, replace, replace lines, delete) always ask for approval; `DisableReadApproval` only reaches the
  tools that read, and `ReadOnly` offers the tools that read and nothing else. `Tracon:Workspace`
  (`TraconWorkspaceOptions`) limits a file (256 KiB), the files of a session (5 MiB) and the workspaces of a tenant
  (500 MiB), and a file also counts 1 KiB for its row; a write past a limit is refused, the model reads "Workspace quota reached", and the run goes on. The
  files go with their session: `DELETE /api/sessions/{id}`, a data subject erasure (its preview, export and delete
  include them under `session_workspace`) and the `sessions` retention target delete them. A run with no session,
  a workflow run and a replay have no workspace. A host that exposes an agent over A2A or MCP must make its
  workspace read-only with `DisableReadApproval`, or the host refuses to open the agent. The console has three
  boxes for it, and the sample has a `workspace-drafter` agent. A tool of your own cannot take the name of one of the eight file tools,
  an agent that has a workspace cannot have a `/` in its name, and an agent with file memory cannot be named
  `~workspace…`. The automatic continuation of an interrupted run refuses an agent that has a workspace. The
  default file store is in memory and loses the files at a restart: use a SQL provider. SQL Server counts the
  UTF-16 bytes it stores, so a limit there holds half as many ASCII characters. Migration `0064` (PostgreSQL),
  `0052` (SQL Server) and `0051` (SQLite) add an index on `agent_files (tenant_id, path)`; the queries behind
  the limits and the deletion would otherwise read the whole table.

- **An agent remembers what its user said in earlier sessions.** Set `MemorySettings.EnableConversationMemory`
  on a definition, call `UseConversationMemory(options => options.VectorDimensions = …)` once, and register a
  `Microsoft.Extensions.VectorData.VectorStore`. The agent then finds what the same user told it in an earlier
  session and gets it as context, before each turn (`ConversationMemorySearch.BeforeEachTurn`, the default) or
  through a `search_conversation_memory` tool the model calls (`OnDemand`). A record carries the tenant, the
  agent, the user and the session; a search looks at tenant, agent and user, so a user is remembered across
  sessions and never seen by another user, agent or tenant. A run with no user or no session neither reads nor
  writes. Only user and assistant text is stored, not tool results. The records go with their session:
  `DELETE /api/sessions/{id}`, a data subject erasure (its preview, export and delete include them under
  `conversation_memory`) and the `sessions` retention target delete them first. The store embeds the text, so
  that model call is not on the run's usage. The console has one box and one choice for it.
  Check the connector first: the `Microsoft.SemanticKernel.Connectors.*` packages (1.74 preview) call a member that
  `Microsoft.Extensions.VectorData.Abstractions` 10.7 removed, and fail at search time without a build warning;
  `CommunityToolkit.VectorData.*` 1.0 works. A workflow run, and the agent steps below it, remember nothing: the
  workflow's session id never gets a session record, so nothing could delete what they kept. The records hold the
  message text in plain text, in your store, outside content protection.

- **A definition can turn the approval of the two skill read tools off.** Microsoft Agent Framework asks for
  approval before `load_skill`, `read_skill_resource` and `run_skill_script`, so an agent with skills could
  not run where nobody can answer: an A2A or MCP call, a run with no session, a scheduled run. The new
  `SkillSettings` record (`AgentDefinition.Skills`, `AgentDefinitionRequest.Skills`, `AgentDescriptor.Skills`,
  and `skills` in the HTTP body) has one member, `DisableReadApproval`. With `true`, `load_skill` and
  `read_skill_resource` run without approval. `run_skill_script` keeps its approval: it runs code, so no member
  of the definition turns that off (a standing approval rule can still approve it). The default is unchanged, a definition stored before this release reads back with all three
  tools asking, and the console's skills section has one box for it. A definition that sets `Skills` but
  names no skill gets the validation warning `skill_settings_without_skills`. Skill text reaches the model as
  instructions, so turn the box on only for skills that a tenant administrator writes.

- **A workflow waits for a tool that needs approval, and a person answers it.** Before, an agent step
  whose tool needs approval failed the whole workflow unless an auto-approval rule existed. Where a
  Microsoft Agent Framework pattern hosts the agent (a stored `Sequential`, `Concurrent`, `Handoff`, `GroupChat` or `Magentic` workflow
  defined with `agentNames`, and a code graph that binds the agent with `GetWorkflowAgent`), the workflow now stops: the step's run
  closes `AwaitingApproval`, the workflow run closes `AwaitingInput`, and the request has the new form
  `WorkflowRequestForm.ToolApproval`, whose prompt names the tool and adds what your
  `IToolApprovalPresenter` says about the call (the arguments are not copied). Answer it with
  `POST /api/workflows/runs/{runId}/respond`: `approved`, an optional reason in `text` that the agent
  reads, and `remember` (new on `WorkflowRespondRequest`) to save a rule for that agent and tool. The
  answer carries a decision only: the tool call comes from the request the workflow recorded, so a
  `data` value is refused with `400`, and so is `remember` with `approved: false` or on any other
  form. The decision is written to the audit trail as `approval.decision` before the workflow
  continues; an entry that cannot be written refuses the answer. The tool runs once, in the new run the
  answer opens, in whichever process takes the answer. A request nobody answers closes the step that
  waited with the workflow when its time limit passes. The console's workflow card asks for the
  decision, a reason and the rule. An agent node of a `Sequential` node list and a step bound with
  `GetWorkflowAgentStep` run the agent in a plain function node that cannot wait, so a tool that needs
  approval still fails the run there, and so does a tool of the `Magentic` manager; an agent that a
  step's agent calls as a sub-agent still cannot ask. A tool approval is answered once, whatever the
  workflow's response mode: the same decision again follows the run the first one started, and a
  different one is `409`.

- **An A2A conversation keeps its history, and it belongs to the caller that started it.** A message
  that carries a `contextId` now continues a conversation: Tracon stores the session and the agent sees
  the earlier turns. Before, the reply carried the `contextId` back but the agent saw one message, so
  a caller believed it was in a conversation that did not exist. A conversation is stored under the
  tenant, the agent, the caller and the `contextId`, and a caller that sends the `contextId` of another
  caller starts a new conversation. The caller is the signed-in user (`Tracon:Audit:ActorClaimType`
  applies), the API key, the static `AuthToken`, or, with no identity at all, the local operator; all
  callers without an identity share one conversation space, and a signed-in user whose identity cannot
  be read is refused. The session identity is `a2a:` followed by a SHA-256 digest of the four parts, so
  a `contextId` of any length fits the session identity column. Each turn re-sends the history to the
  model, so the tokens per message grow with the conversation. `UseA2A()` registers the store for each
  exposed agent and an `AgentIsolationKeyProvider`; a store or a provider the application registers
  itself wins. A message without a `contextId` is still a single turn, but Tracon stores a session for it too (it makes up a `contextId` and the reply carries it), and session retention is off by default; set a maximum age for the `sessions` target to bound the table. The change adds no migration.

- **An eval case lists the tool calls it expects, with their arguments, and `toolCallArgsMatch` checks
  them.** `EvalCase.ExpectedToolCalls` is a list of `EvalExpectedToolCall`: a tool `Name` and optional
  `Arguments`. The new check kind `toolCallArgsMatch` passes a case when, for every entry, the agent
  called a tool of that name and one such call carried every listed argument. The listed arguments are
  a subset (the call may carry others) and a value compares by its JSON meaning: `2` equals `2.0`, an
  object equals one with the same members in any order, and a `null` value means "absent or null".
  Microsoft Agent Framework's own `ToolCallArgsMatch` compares a number by its .NET type and an object
  by its text, so an expected `2` never matched; Tracon's check does not use it. The reason of a failed
  case names each entry (`not called`, `args mismatch`) and never repeats an argument value. A custom
  evaluator receives the same calls on `EvalItem.ExpectedToolCalls`, as Microsoft Agent Framework
  reads them. The console's case editor takes a tool name and a JSON object of arguments for each call
  and refuses an argument text that is not a JSON object. Promoting a reference run writes the names
  of the tools the run called, without arguments. The change adds `eval_cases.expected_tool_calls`
  (migration `0063` PostgreSQL, `0051` SQL Server, `0050` SQLite). A cell of that column that SQL
  Server or SQLite holds as text and that is no longer the array Tracon writes reads as an entry no run
  can call, so the case fails instead of passing with nothing to check, and the listing still works.
  A custom `IEvalStore` meets this in `Tracon.Testing.Contracts.Xunit`: `EvalStoreContract` adds
  `EvalStoreContract.ReplaceCasesAsync_round_trips_an_expected_argument_of_every_json_kind`,
  `EvalStoreContract.ReplaceCasesAsync_reads_back_an_empty_list_for_a_case_that_expects_no_tool_call` and
  `EvalStoreContract.A_row_written_before_expected_tool_calls_existed_reads_back_its_names`, and renames
  `ReplaceCasesAsync_stores_expected_tools_and_context` to
  `EvalStoreContract.ReplaceCasesAsync_stores_expected_tool_calls_and_context`. The last one needs three
  hooks a store on SQL overrides (`SupportsRawSql`, `ExecuteRawAsync`, `QualifiedEvalCasesTable`, and
  `FormatIdForRawSql` when it spells an id otherwise); a store without SQL skips it.

- **A host registers a tool the model provider runs, and a definition selects it by name.**
  `AddHostedTool(name, kind, factory)` registers a web search, a code interpreter or a file search
  (`HostedToolKind`); the factory receives a `HostedToolContext` with the tenant, the provider of the
  agent's model and the services, and builds the Microsoft.Extensions.AI tool for that tenant. A
  definition lists the name in `ToolNames`, the same list as a function tool. The provider runs the
  tool inside the model call, so no approval, permission or timeout applies. A provider states which
  kinds its API surface runs through the new `IHostedToolSupport` (`HostedToolKinds`); a definition
  that selects a kind the provider would drop, on the primary model or on a fallback, is refused when
  the agent is built and by `POST /api/agents/validate` (`hosted_tool_unsupported`, with the path of
  the tool name), instead of running without the tool. Measured against the request each adapter
  writes: `openai-responses` runs all three kinds, `anthropic` and `google` run the web search and
  the code interpreter, `openai` (Chat Completions) and `azure-openai` run none. An agent selects at
  most one hosted tool of each kind; the cached compiled agent follows what the factory builds, so a
  tenant that replaces its vector store reaches the next run. `GET /api/tools` lists hosted tools with
  `ToolDescriptor.HostedKind`, the console shows a badge, and the sample has `hosted-openai`,
  `hosted-claude` and `hosted-gemini`.
- **A hosted tool call is priced.** `Tracon:Pricing:HostedTools:{provider}:{kind}:PerCall` sets the price
  of one call. The amount joins the run's non-token cost, so the budget, the quota and the
  `run.completed` webhook count it once, and a budget the call used up stops the next model turn. A
  kind with no price is listed on the run and not added to its cost (the run's
  `UnsummedItemCount`); it is never priced at zero, and the host logs one warning. The code
  interpreter is billed by container and its price per call is an estimate. When Anthropic bills more
  searches than the response shows, the difference is added. The harness's own web search is priced
  the same way; before, no hosted call had a cost. `ToolUsageUnits.Calls` names the unit.
- **An agent calls a callable agent with one tool call.** `SubAgentSettings.CallMode`
  (`SubAgentCallMode.Background`, the default, or `Tool`) picks what the model sees. With `Tool`,
  each agent in `CallableAgentNames` is one tool named after the agent (each run of characters
  other than letters and digits becomes one underscore), instead of Microsoft Agent Framework's
  background task tools. The sample `router` agent asked one question took 5 model calls and about
  3,600 input tokens with background tasks, and 2 model calls and about 410 input tokens with
  `Tool` (real OpenAI `gpt-5.4-mini`, three runs each); for work that runs in parallel,
  background tasks stay the better choice. The depth limit, the budget of the tree, the tenant
  check, the wait limits and the refusal of a sub-agent that needs tool approval apply in both
  modes. A callable agent whose tool name another tool of the agent already has (`ToolNames`, a
  skill tool, `search_knowledge`, another callable agent) is refused by validation and save
  (`sub_agent_tool_name_conflict`, `400`) and by the compiler. The agent editor has a control for
  the mode and the version compare shows it. A sub-agent that fails because its model provider
  is down, or because it no longer exists, answers the parent with the reason as the tool result,
  as a background task does. A row saved before this setting reads as background tasks.
- **A definition sets the model's request fields and the tool-call loop.** `ModelBinding` gains
  `Seed`, `TopK`, `StopSequences`, `FrequencyPenalty`, `PresencePenalty`, `ToolMode` (`auto`,
  `none`, `required` or `required:<tool>`), `AllowMultipleToolCalls` and `ReasoningOutput`
  (`None`, `Summary` or `Full`), and `ToolLoop` (`ToolLoopSettings`: `AllowConcurrentToolCalls`,
  `MaximumConsecutiveErrors`, `TerminateOnUnknownCalls`). They are part of the definition, so a
  version, an experiment variant and a replay carry them; before, only an agent extension could
  set most of them and `Reasoning.Output` could not be set at all. A provider states which of the
  fields its API surface sends through the new `ISamplingSupport` (`SamplingFields`). A definition
  that sets a field the provider would drop, on the primary model or on a fallback, is refused
  when the agent is built and by `POST /api/agents/validate` (`sampling_field_unsupported`, with
  the path of the field), instead of running without it. The shipped providers state what the
  model adapters send; the table is in Model providers. `Tracon.Testing`'s `FakeModelProvider`
  accepts every field, and the agent editor has controls for all of them.
- **Model content and workflow events Tracon has no type for are recorded, not dropped.**
  A hosted tool call or result (web search, code interpreter, image generation, hosted MCP),
  a provider `ErrorContent`, generated or attached data, a file reference, and any content
  type Tracon does not know yet are written as a `Custom` run event of type `tracon.content`
  (`RunEventCustomTypes.Content`). A `WorkflowEvent` that the workflow stream does not map,
  such as an event of your own that an executor of a code workflow raises, is written as
  `tracon.workflow.event` (`RunEventCustomTypes.WorkflowEvent`); its warning in the log is
  now a debug entry. The payload holds no data bytes, no URI, no tool inputs or outputs;
  its free text is the provider's error message and the text of a string workflow payload,
  cut at 2 048 characters, and any other workflow payload is named by its type only. Both
  payloads follow `RecordToolPayloads`. Unlike the quota and truncation notices,
  `tracon.content` is not sent on the live `POST /api/agents/{name}/run` stream; a workflow
  run's stream sends `tracon.workflow.event` like every recorded event.
  Plain text with `RecordMessageDeltas` off, reasoning with `RecordReasoningDeltas` off,
  usage and approval content stay as they were. Read the events from
  `GET /api/runs/{id}/events`.
- **A named agent extension reaches Microsoft Agent Framework options on a compiled agent.**
  `AddAgentExtension(name, configure)`, `AddAgentExtension<T>()`, and an instance overload
  register code that sets a framework option, adds an `AIContextProvider`, or wraps the
  agent with `context.Use`. A definition selects it with the new `ExtensionNames` list, in
  call order, and the agent editor offers the registered names (`GET /api/agent-extensions`).
  The agent still goes through the definition compiler, so it keeps the model rings, the
  tenant's own provider key, the egress policy, and cost. The compiler refuses a change to
  an option the definition or a tenant and security decision owns, and names the extension
  and the option. `AgentDefinition`, `AgentDescriptor`, and `AgentDefinitionRequest` gain
  `ExtensionNames`. A provider an extension adds can add tools, and those skip the tool
  chain; a replay that plays tools back does not run extensions.
- **Any `Microsoft.Extensions.AI` speech client serves a named Tracon speech provider.**
  `AddSpeechSynthesizer(name, TextToSpeechClientBinding)` and
  `AddSpeechTranscriber(name, SpeechToTextClientBinding)` take the client from the container,
  by service key or unkeyed. Every call passes the tenant's egress policy against the binding's
  provider name; a tenant with its own key for that provider is refused; the host stops at
  startup when the client is missing or has no provider name. The binding declares the
  character limit and the voice list, which the client cannot report. Billing is an
  estimate, character timing and dialogue are refused. No public member names a
  `Microsoft.Extensions.AI` speech type.
- **`UseOpenAISpeech` registers OpenAI text-to-speech and speech-to-text** as the named
  providers `openai`, with `OpenAISpeechOptions` (`SynthesisModel`, `TranscriptionModel`,
  names, `MaxCharactersPerRequest`, `Voices`, `UseAsDefault`). Each name is bound to one model.
  The clients run on the shared OpenAI client's pipeline and not on `AudioClient`, so a host
  publishes with Native AOT.
- **A factory agent built outside the model registry logs one warning.** When the chat client
  of an `AddAgent(name, factory)` agent did not come from `IModelProviderRegistry`, the catalog
  writes a warning the first time it resolves that agent name, because its model calls skip
  the model rings. An agent that exposes no chat client gets no warning, and a resolve never
  fails because of the check. The guide "Use any Microsoft Agent Framework feature" compares
  the decorator, the factory agent, and the custom agent source by what each keeps, and
  `samples/Tracon.Samples.MafFeatures` builds all three.

- **`TraconErrorTypes` lists the error codes of runs, jobs and OpenAI-compatible errors.** One class holds them in one
  place: the type of a failed run (`tool_timeout`, `content_blocked`, `run_budget_exceeded`, …), the codes of a job the
  queue failed (`tracon.job.attempts-exhausted`, …) and the `error.type` of the OpenAI-compatible endpoints
  (`model_not_found`, …). The `*ErrorType` constants of the exception classes
  (`TraconToolTimeoutException.ToolTimeoutErrorType` and the others) and `JobErrorCodes` stay, and they carry the same
  values; no value changed, so no stored run record and no client that matches a code is affected. The codes of the
  findings that `POST /api/agents/validate` returns (`unknown_tool`, `cycle`, …) are listed the same way and are not
  public members. A code never changes inside a major version.
- **The release check compares HTTP, SSE, telemetry, configuration keys, error codes and the CLI with the last published
  version.** Until now only the .NET API had a check against the last release. From `1.0.0-rc.1` a removed or renamed
  endpoint, response field, event name, instrument, attribute, configuration key, error code, CLI command or option
  stops the release, and so does a change that the rules do not name. A patch release does not add to any of them.
  See [What 1.x promises for HTTP, telemetry, configuration, error codes and the
  CLI](https://tracon.dev/reference/versioning/#what-1x-promises-for-http-telemetry-configuration-error-codes-and-the-cli).

- **One tenant can no longer take every workflow slot of a process, or the front of the job queue.** The new
  `Tracon:Workflows:MaxConcurrentRunsPerTenant` (`TraconWorkflowOptions.MaxConcurrentRunsPerTenant`, off by default, from
  `1` to `MaxConcurrentRuns`) limits how many workflow runs one tenant has going at the same time, in two places. In a
  process, a run beyond the tenant's limit waits for a tenant slot **before** it takes a shared slot, so it holds nothing
  while it waits and never delays another tenant. In the job queue, a worker does not lease a queued workflow job
  (`tracon.workflow`, `tracon.workflow-run`) of a tenant that already holds that many leased, and leases the oldest job
  of another tenant instead; this count covers every process that shares the store, and it is read, not locked, so
  workers that lease at the same moment can take a tenant past the limit by up to their own number. The wait for a tenant
  slot counts toward `RunTimeout`, as the wait for a shared slot does. The setting is checked at startup, and a value that
  is not a number stops the host. See [workflow
  concurrency](https://tracon.dev/guides/background-work/#limit-workflow-runs-per-tenant).

- **The run stream of an agent with a loop says where each iteration ends.** `POST /api/agents/{name}/run` already
  streamed every iteration, but nothing in the `update` frames marked the border, so a client that read the stream
  could take the first draft for the answer. After each iteration the loop evaluated, the stream now sends a
  `loop.iteration-completed` frame, between the last `update` of that iteration and the first of the next. Its data is
  the recorded `LoopIterationCompleted` event, byte for byte what `GET /api/runs/{id}/events` serves. The answer is the
  iteration the last frame closes when it has `continued: false`; when it has `ceilingReached: true`, the answer is the
  iteration after it, because the turn at the ceiling is not evaluated. An agent without a loop, a run that records no
  events and a sub-agent's loop send no frame. The console playground draws a divider at each frame. A client that
  ignores a frame name it does not know needs no change. See [Streaming](https://tracon.dev/http-api/#streaming).

### Changed

- **A workflow run that waits for a concurrency slot is `Queued`, not `Running`.** The run's record still opens before
  the wait, but it reads `Queued` while the run waits for a slot of `MaxConcurrentRuns` or of
  `MaxConcurrentRunsPerTenant`, and `Running` once it has them. A run that finds a slot free opens `Running` as before.
  `GET /api/runs`, the run's detail and the console show it (the `queued` badge was already there). A dashboard that
  counted `Running` rows as "using a slot" now counts only the runs that have one. `POST /api/runs/{id}/cancel` reaches a
  waiting run (`202`, closed `Canceled`); until now it sent every `Queued` run to the job queue.
- **A `Queued` run row can be closed by run reconciliation.** `IRunStore.TouchHeartbeatAsync` now marks `Queued` rows as
  well as `Running` ones, and `IRunStore.ClaimOrphanedRunsAsync` closes a `Queued` row whose heartbeat is older than the
  threshold as `Failed` (`orphaned`). A process writes a heartbeat for the runs it executes, and for a workflow run that waits
  in it for a slot, so a `Queued` row with a heartbeat is a run whose process is gone. A `Queued` row that never sent
  one is still the job queue's and is never closed by reconciliation. **An `IRunStore` you implement must do the same:**
  the shared contract tests (`RunStoreContract`) have three new cases.
- **Breaking for implementers (preview):** `IJobStore.LeaseAsync(owner, leaseDuration, lanes, cancellationToken)` is now
  `IJobStore.LeaseAsync(JobLeaseRequest request, cancellationToken)`. `JobLeaseRequest` carries `Owner`, `LeaseDuration`,
  `Lanes` and the new `TenantCap` (`JobTenantCap`: the handler keys and `MaxLeasedPerTenant`); a new option is a new
  property, not a new parameter. An `IJobStore` you implement takes the request and, when `TenantCap` is set, skips a ready
  job of a handler key in the cap while its tenant holds that many live leases on those keys (an expired lease does not
  count). A caller reads `request.Lanes` where it read `lanes`. `JobStoreContract` has eleven new cases for the cap.

- **`tracon.tenant.id` is no longer on metrics by default.** The metrics `tracon.runs`, `tracon.run.cost`,
  `tracon.judge.cost`, `tracon.judge.score`, `tracon.model.cache`, `tracon.audit.write_failures`,
  `tracon.run.recording_failures` and `tracon.job.executions` carried the tenant on every series, which multiplies each
  series by the number of tenants. They now carry it only when `Tracon:Observability:IncludeTenantTag`
  (`TraconObservabilityOptions.IncludeTenantTag`) is `true`; it is `false` by default. Spans and the quota gauges
  (`tracon.quota.usage`, `tracon.quota.limit`) carry `tracon.tenant.id` as before. **A dashboard or an alert rule that
  filters or groups one of the eight metrics by `tracon.tenant.id` shows no series until you set the key.** A value that
  is not `true` or `false` stops the application at startup. See [the tenant tag on
  metrics](https://tracon.dev/guides/observability/#the-tenant-tag-on-metrics).
- **A workflow run that continues an earlier run no longer starts with a budget of zero.** A person's answer to a
  request, an operator's resume (`POST /api/workflows/runs/{runId}/resume`) and a recovery after an interruption each
  start a new run row, and each used to build a new call-tree budget: a workflow with an approval loop got the whole
  `Tracon:AgentGraph` ceiling again with every answer, and a run that failed for its budget was resumed into the node
  that had been refused. The ceiling is now the logical run's. The new run starts from what the runs before it spent
  (tokens, cost and child runs), and the time limit counts the time the chain's runs were **executing**, not the time
  the workflow waited for a person. A workflow that stayed under its ceiling only because every answer got a new one now
  fails at the node that crosses it, with `run_budget_exceeded`: raise `MaxTotalCost`, `MaxTotalTokens`, `MaxTotalRuns` or
  `MaxDuration` to the size of the whole review, not of one round. A chain that cannot be read (the run store does not
  answer, or it is longer than 128 runs) ends the run `Failed` before its first node. A run that an interruption closed
  holds what its function nodes reported only in its items, and Tracon reads them from there. A nested sub-agent call is
  spent but not counted as a started run. See
  [the ceiling of a workflow](https://tracon.dev/guides/reliability/#the-ceiling-of-a-workflow-is-the-logical-run).
- **An operator's resume of a workflow run records the run it resumes in `continuedFromRunId`.** Until now only a
  recovery wrote the link, so the resume run looked unrelated to its source and `GET /api/runs?continuedFromRunId=`
  did not list it. A run that answers a person's request still does not carry it; the answer names the run that waited
  in its own history. A continuation that a recovery opened keeps the link the recovery wrote. The resume run joins the
  chain the recovery and the response gate read, so a repeated answer follows it, and a resume of a run whose queued job
  may still recover it is recovered with that job: resume a run only after its job has ended.
- **A workflow function node or an agent step is refused once the tenant's cost quota is spent.** The quota was read
  at the door only (`429`), so a workflow that waited for a person, or ran for hours, passed a quota that was spent in its
  first minutes. Before a function node or an agent step starts, the tree asks the tenant's quota: the **cost** limit
  stops a function node, and the **token** and **cost** limits stop an agent step or one agent's call to another. The run
  ends `Failed` with the new error type `tenant_quota_exceeded` (`TraconTenantQuotaExceededException`, class
  `QuotaExceeded`); when one agent calls another, the refusal comes back to the model as a tool result, as a spent call-tree
  budget does. Only a start is refused, and the check counts what the run has spent that its counter has not seen yet, so
  the nodes it already finished count. The run limit stays at the door. A node registered with
  `options.StartsWhenBudgetIsSpent = true` is exempt from the quota as it is from the budget, and a refusal is never retried
  by a node's retry policy. The check reads the tenant's rules (one read; two when the tenant has a rule) each time a node
  or step starts, and follows `Quotas:AllowOnStoreFailure` when the store cannot be read. With `Quotas:Enabled = false`
  nothing is read. Quotas are on by default, so a model call is priced in every installation that has not turned them off:
  `AgentRunBudget.ConsumedCost` now holds the cost of the model calls even when `AgentGraph.MaxTotalCost` is not set.
  The price lookup costs about 1 microsecond per model call (24 models in the catalog). See
  [A quota also stops work that has not started](https://tracon.dev/concepts/governance/#a-quota-also-stops-work-that-has-not-started).
- **A data subject erasure deletes the copies of a session outside the SQL rows before it opens its
  transaction.** The copies are the records of conversation memory in your vector store and the files of a session
  workspace. They used to go after the rows were deleted and before the commit. The files live in the same
  database, and SQLite lets one connection write at a time: a delete on a second connection, while the erasure
  held its writes, waited for the busy timeout and failed. A copy that cannot be deleted still fails the call with
  every row in place, and repeating the call finishes the work. If a later step fails after the copies are gone, the
  repeat call deletes the rest and its audit entry counts only what was left. The `sessions` retention target now always reads the
  expired batch and deletes the files of each session before the rows (an installation with no workspace deletes
  nothing extra, and the delete is answered from the new index).
- **Model Context Protocol SDK `2.3.0`** (`ModelContextProtocol.Core`, `.AspNetCore`, `.Extensions.Tasks`).
  No public member was removed. A published agent's `tools/call` from a client on the `2026-07-28`
  revision now gets its response headers with the result: a proxy with a time-to-first-byte limit
  cuts a long call. Use `EnableTasks` for long calls, or raise that limit.
- **An A2A message is refused before the model is called when session ownership is strict and the
  caller has no identity to own the conversation.** With `Tracon:SessionOwnership:Enabled` and
  `RequireAuthenticatedOwner` on, a conversation needs an owner. A2A wrote no session until now, so the
  rule never reached it. The caller receives a JSON-RPC internal error (`-32603`) and the cause is in
  the server log at `Error` level. A message that resumes a conversation another user owns is refused the
  same way, which matters when one API key speaks for several users. Bind `IRunAttributionContext` to the
  identity pipeline, or turn `RequireAuthenticatedOwner` off, before you upgrade an installation that
  publishes an agent over A2A.

- **`EvalCase.ExpectedTools` and `EvalCaseDraft.ExpectedTools` are replaced by `ExpectedToolCalls`.**
  This breaks source and the JSON of a case. The old field held tool names and no check read it, so a
  case that filled it asserted nothing; a name-only entry of the new field is checked. `PUT
  /api/evals/{name}/cases` answers `400` (`Removed field`) to a body that still carries
  `expectedTools` and names `expectedToolCalls`; the `expectedTools` of the case list is gone. A case
  stored by an earlier build keeps its names: they read back as entries without arguments, the old
  column stays in the table and is never written, and the next save of the case writes the new
  column. A custom check can no longer be registered under the name `toolCallArgsMatch`; it is a
  built-in kind now.

- **A call of a registered hosted tool is a pair of tool events, not a `tracon.content` event.**
  The calls of a tool added with `AddHostedTool` are `ToolInvoking` and `ToolInvoked` events under
  the registered name, with the provider's call identifier and no payload, and a tool invocation
  record that carries the call's measurement (unit `calls`, quantity `1`). A hosted call that no
  registered tool owns, such as the harness's web search, stays a `tracon.content` event as before. A
  streamed call that the provider shows in several pieces is written once. A replay that plays
  tools back (`ReplayTools`) or runs none (`NoTools`) removes a hosted tool, so it does not search
  and bill again.
- **`ModelBinding.AllowConcurrentToolCalls` moved to `ModelBinding.ToolLoop.AllowConcurrentToolCalls`.**
  This breaks source and the JSON of a definition. A definition stored by an earlier build is read
  with the old key moved, so no migration runs and the setting is kept; the next save writes the
  new place only, and so does a rollback. `POST /api/agents`, `PUT /api/agents/{name}` and
  `POST /api/agents/validate` refuse the old key with `400` (`Removed field`) and name the new
  path. A fallback link now takes the whole `ToolLoop` of the primary model.
- **An agent extension and the options of one run can no longer set `Seed`, `TopK`, `StopSequences`,
  `FrequencyPenalty`, `PresencePenalty`, `ToolMode` or `AllowMultipleToolCalls`.** The definition
  owns them now; an extension that changes one fails the compilation and names the option, and
  `ChatClientAgentRunOptions` that sets one throws `ArgumentException`. One exception: a run may
  set `AllowMultipleToolCalls` to `false`, which a handoff step does to hand off to one agent.
- **`/v1/chat/completions` refuses a model setting the agent owns with `400`.** `temperature`,
  `top_p`, `max_tokens`, `max_completion_tokens`, `frequency_penalty`, `presence_penalty`,
  `seed`, `stop`, `logit_bias`, `reasoning_effort`, `response_format`, `tools`, `tool_choice`,
  `functions`, `function_call`, `n`, `logprobs`, `top_logprobs`, `modalities`, `audio`, and
  `web_search_options` were read by nothing: a client that asked for JSON, three choices, or its
  own tool got none of them and no error. A value that asks for the default passes. `/v1/responses`
  already refused its own setting fields. `POST /v1/conversations` refuses initial `items`, which
  it used to drop.
- **Agents inside run recording receive a plain `AgentRunOptions`.** An `IAgentDecorator` with an
  `Order` above 0 no longer sees `TraconRunOptions`; read the run through `TraconRunContext`.
  Microsoft Agent Framework's function-invocation middleware (`AIAgentBuilder.Use` with a
  `FunctionInvocationContext` callback) rejects any other options type, so it threw
  `NotSupportedException` on every run started by a Tracon endpoint. It now runs.
- **`/v1/chat/completions` refuses tool-call history and unknown roles with `400`.** A `tool` or
  `function` message, an assistant message with `tool_calls` or `function_call`, and a role other
  than `system`, `developer`, `user` or `assistant` used to pass: the assistant turn was dropped,
  the tool result went on as plain text without its call id, and an unknown role became `user`.
  Tracon runs the agent's tools on the server, and `/v1/responses` refuses `function_call` items
  the same way. A content part Tracon cannot hand to the model is refused by name, too.
- `AddSpeechSynthesizer(name, null)` and `AddSpeechTranscriber(name, null)` with a literal `null`
  no longer compile: the call is ambiguous between the factory and the binding overloads. Cast the
  `null`, or pass the factory.
- A harness agent that names skills and also calls other agents lists the skill tools after the
  background-agent tools, and puts the skill section of its instructions after the
  background-agents section. They were in the other order. A plain agent, and a harness agent
  that uses only one of the two, does not change.

### Fixed

- **The A2A and MCP exposure check refuses an agent whose skill tools ask for approval.** The check read only
  the tool registry, and the skill tools are not in it, so an agent with skills was exposed and its first
  `load_skill` call closed the run `Failed` with `approval_unanswerable` (`-32603` over A2A). The host now
  refuses the agent when it starts, names the agent and the way out (`Skills.DisableReadApproval`, or remove
  the skills), and opens it when the definition turns the read approval off. A host that exposed such an
  agent and relied on every call failing must set `Skills.DisableReadApproval` on the definition, or take the
  agent off the allow list. The check reads the catalog once, when the host starts: an agent saved later is not
  checked, and a model that calls `run_skill_script` still ends the run at the call unless a standing approval
  rule covers it.

- **A tool that Microsoft Agent Framework already wrapped for approval reports that it needs approval.**
  `AddTool(new ApprovalRequiredAIFunction(fn))` — the default shape of `DockerShellExecutor.AsAIFunction()` —
  asked for approval at run time, but `/api/tools` said `requiresApproval: false`. The A2A and MCP exposure
  check read that flag, so such an agent was exposed and every call failed with `-32603`. The startup check
  now refuses it, and replay sees the approval.
- **The .NET client reads a nullable enum property.** `ToolDescriptor.HostedKind`, `RunComparisonSide.ErrorClass`,
  `RunError.Class`, `WorkflowDescriptor.Kind`, `EvalCase.SourceKind` and `VoiceSession.EndReason` were generated as an
  empty class named after the property in place of the enum, so the client threw `JsonException` when the host sent a
  value (`The JSON value could not be converted to ...ErrorClass`). They are the enum types now. A property the host
  always sends (`WorkflowDefinition.Kind`, `WorkflowSaveRequest.Kind`, `RunErrorStatistics.Class`) is no longer
  nullable in the client. The TypeScript client was not affected.
- **A stored definition written before a list member existed no longer breaks the catalog.**
  The SQL payload read `mcpResourceUris`, `parameters`, and the new `extensionNames` as
  `null` when a row had no such key, and the catalog listing failed with
  `ArgumentNullException` for every agent. The three lists now read as empty.
- A harness agent completes the turn that answers a pending approval. The approved (or rejected)
  tool ran, and then the next model call failed with `InvalidOperationException`: the harness
  reloads the session history on every model call, and the history still held the approval
  request while the response had been consumed. A plain agent was not affected.
- `UseOpenAIImages` and `UseAzureOpenAIImages` publish with Native AOT. The image generator now
  runs on the shared client's pipeline instead of `ImageClient`, whose members name a
  `System.ClientModel` type that version 1.16 renamed; `ilc` stopped with "Failed to load type".
- `/v1/chat/completions` hands image, audio and file parts to the model. It kept only the `text`
  parts, so the model answered as if the user had sent no image, and a message with only an image
  answered `400` "'messages' cannot be empty." Embedded data passes the attachment type check and
  is stored as an attachment, as on `/v1/responses`.
- An image's `detail` and a file's name now reach the model on `/v1/responses` and on
  `POST /api/agents/{name}/run` attachments. The attachment reference dropped both.

## [1.0.0-preview.5] - 2026-10-17

The public API gets smaller before it freezes, and every dependency moves to its
newest line. Twelve interfaces, 23 types that only they used, 81 HTTP request and
response types and `TraconAgentSessionStore` leave the public surface; five service
types lose their public constructors; `AuthorizationConfigurationKey` is removed from
the MCP server types, and a migration moves the stored data. The runtime moves to
Microsoft Agent Framework `1.25.0` and Microsoft.Extensions.AI `10.11.0`.
`Tracon.Azure` calls the Azure OpenAI v1 API, Anthropic `ReasoningEffort` becomes
adaptive thinking, and a configuration value Tracon cannot read now stops startup. The
release adds one migration to each SQL provider and breaks source in several places:
read Upgrading from 1.0.0-preview.4 first.

### Upgrading from 1.0.0-preview.4

- **Migrations.** PostgreSQL `0062`, SQL Server `0050` and SQLite `0049`
  (`remove_authorization_configuration_key`). With `AutoApplyMigrations` (the default) they run at
  startup. Each one moves the stored `authorization_configuration_key` of an MCP server into its
  `header_configuration_keys` map as the `Authorization` entry, then drops the column. A row with
  OAuth on, or whose map already names `Authorization` in any letter case, keeps its map: the
  connection never read the old key there. An empty key moves nothing. Only the key **name** moves;
  no secret value is stored or touched.
- **The column is dropped in the same migration, so there is no way back.** A build from before the
  migration cannot read the table. Back up the database before the first start on this version.
- Every MCP connection reconnects once after the upgrade (its definition fingerprint changed).
- **Public types changed or left the surface.** Each entry below (Changed, Removed) says what to do:
  - `McpServerDefinition.AuthorizationConfigurationKey` and
    `McpServerRequest.AuthorizationConfigurationKey` are removed; declare the key name as
    `HeaderConfigurationKeys["Authorization"]`. `PUT /api/mcp-servers/{name}` answers `400` to a body
    that still carries `authorizationConfigurationKey`.
  - Twelve interfaces, 23 types, 81 HTTP request and response types and `TraconAgentSessionStore`
    are `internal`. A host that named one declares its own type, or resolves a service from DI.
  - `IRunStore.UpdateRunCostAsync` has no default for `tenantId`; pass the run's tenant.
  - `SingleTenantContext`, `HttpTenantContext`, `TimeoutAIFunction`, `CompiledAgentCache` and
    `DefaultRunAttributionContext` have no public constructor; resolve them from DI.
  - `OpenAILiveOptions.BackendModel`, `LiveVoiceSessionOptions.BackendModel`,
    `QuotaUsageQuery.AsOf` and `LiveVoiceDelegationMode.Responses` are gone.
  - `Tracon.Client`: `IMcpToolRefresher` and the `body` parameter of `TraconRefreshMcpToolsAsync`
    are gone.
  - `JobStoreContract` gains a test; a third-party `IJobStore` that keeps a time limit in a 32-bit
    millisecond column fails it.
- **Behavior changes most likely to reach an upgrade**, each described in its entry below: a
  `Tracon:Voice:Live:DelegationMode` or `Tracon:UtilityModel` value Tracon cannot read stops startup;
  `Tracon.Azure` uses the Azure OpenAI v1 API and refuses an Entra token over plain HTTP; Anthropic
  `ReasoningEffort` is adaptive thinking; `Tracon.Anthropic` and `Tracon.Google` no longer promise
  Native AOT compatibility.
- A project that pins Agent Framework hosting packages next to Tracon moves them to the versions in
  the dependencies entry (Changed), or restore fails with `NU1608` or `NU1107`.
- Pin every Tracon package to the same exact version. The pre-release Microsoft Agent Framework
  hosting packages are pinned at `1.25.0-preview.261014.1`, `Microsoft.Agents.AI.Hosting.OpenAI` at
  `1.25.0-alpha.261014.1` and `A2A.AspNetCore` at `1.0.0-preview3`.

### Changed

- **`Tracon.Azure` calls the Azure OpenAI v1 API.** Requests go to `{endpoint}/openai/v1/` through
  the plain `OpenAI` client, with the deployment name in the request's `model` field and no
  `api-version` query string; `Azure.AI.OpenAI` is no longer a dependency (its stable line stopped
  at 2.1.0 in December 2024). The resource must support the v1 API. `UseAzureOpenAI`,
  `AzureOpenAIProviderOptions` and the registered provider name do not change; an address that
  already ends in `/openai/v1` is used as it is. An API key is sent as `Authorization: Bearer`. An
  Entra credential now goes only over HTTPS: a `CredentialFactory` with a plain `http://` address is
  refused at the first call instead of sending the token. The health check calls
  `GET /openai/v1/models`, sends the key as `Authorization: Bearer` like the chat call (it sent an
  `api-key` header), and reports unhealthy instead of sending an Entra token over plain HTTP.
  Image generation uses the same v1 endpoint (`/openai/v1/images/generations`). The Azure-only request fields (On Your Data) never worked with the OpenAI
  version in use and have no counterpart on the plain client; `SupportedSettings` stays empty.
- **`ModelBinding.ReasoningEffort` becomes adaptive thinking on Anthropic.** The request now carries
  `thinking: {"type":"adaptive"}` and `output_config.effort` (`low`, `medium`, `high`, `xhigh`). The
  budget form Tracon sent before (`thinking.type = enabled` with `budget_tokens`) is rejected with
  `400` by every current Claude model; the adaptive form is accepted by all of them (measured on the
  Claude 5 family). A model that predates adaptive thinking, `claude-haiku-4-5`, is the reverse: set
  `anthropic.thinking.mode = enabled` and `anthropic.thinking.budgetTokens` for it. A binding that
  names no `ReasoningEffort` sends the same request as before.
- **`Tracon.Anthropic` and `Tracon.Google` no longer promise Native AOT compatibility.** The promise
  is now a measured result: a Native AOT app that calls the provider through its real request and
  response path must publish with no trim or AOT warning and run. `Tracon.OpenAI`, `Tracon.Azure`
  and `Tracon.Voice` pass. The Anthropic SDK's chat adapter fails on the first request under
  Native AOT, and the Gemini credential library publishes with about a hundred warnings. The
  packages' own code is unchanged and stays analyzer-clean. The release rehearsal now publishes and
  runs a provider smoke, and fails when the compatibility page promises AOT for a provider package
  the smoke does not reference.

- **`IRunStore.UpdateRunCostAsync` no longer defaults `tenantId` to `null`.** The parameter stays
  `string?`; a caller now states the tenant on purpose. `null` skipped the tenant check, so a
  caller that left the argument out silently wrote a cost onto another tenant's run. A call
  `UpdateRunCostAsync(runId, cost)` stops compiling: pass the run's tenant. A store implementation
  drops `= null` from its own signature; the call shape does not change otherwise. The shipped
  `RunStoreContract` passes the tenant explicitly.
- **Five service types keep a public type and lose the public constructor.** The container, or the
  tool pipeline, builds them, and the guides and samples never call `new` on them:
  `SingleTenantContext`, `HttpTenantContext`, `TimeoutAIFunction` (both constructors),
  `CompiledAgentCache` and `DefaultRunAttributionContext`. Resolve them from DI. Their
  registrations are factories now; `TryAdd` and `Replace` behave as before, so an `ITenantContext`
  registered before `AddTracon()` still wins. A new test pins every public constructor of a
  concrete class, so the next service type cannot reach the surface unnoticed.
- **`Tracon:Voice:Live:DelegationMode` refuses a name that is not a delegation mode.** The host
  stops at startup with a message that names the setting. A misspelled name used to fall back to
  `Client` without a word.
- **`Tracon:UtilityModel` refuses a number it cannot read.** `Temperature`, `TopP` and
  `MaxOutputTokens` that do not parse (`"abc"`, `"1.5"` for the token limit, `"0,7"`) stop the host
  at startup with a message that names the key and the text as written. Such a value used to bind
  as `0`, so a typo ran the context-compaction summarizer at temperature zero without a word. An
  empty or blank value still means "not set": the provider default applies. Code that assigns
  `TraconOptions.UtilityModel` after binding still wins.
- **`Tracon.Client` is regenerated with NSwag `14.7.1`.** Its public surface does not change. The
  client now passes the cancellation token when it reads the body of an unexpected response, so a
  cancelled call no longer waits for that body.
- **`Tracon.Testing.Contracts.Xunit` keeps asking for `xunit.v3.extensibility.core` and
  `xunit.v3.assert` `>= 3.2.2`.** The package was measured on xunit.v3 `4.0.2` with
  `Microsoft.Testing.Extensions.TrxReport` `2.5.1`; a project on either line restores without
  `NU1608`.
- **Runtime dependencies move to the newest lines.** Microsoft Agent Framework `1.25.0` (the
  hosting packages `1.25.0-preview.261014.1`, `Microsoft.Agents.AI.Hosting.OpenAI`
  `1.25.0-alpha.261014.1`, `A2A.AspNetCore` `1.0.0-preview3`, all still exact pins),
  Microsoft.Extensions.AI `10.11.0`, OpenAI `2.14.0`, the .NET platform packages `10.0.13`,
  Anthropic `12.58.0`, Google.GenAI `1.26.1`, Azure.Core `1.63.0` and Microsoft.Data.SqlClient
  `7.1.1`. A project that pins Agent Framework hosting packages next to Tracon now gets `NU1608`
  or `NU1107` at restore, as before; move it to these versions. Behavior does not change: the
  Anthropic provider still sends `ReasoningEffort` as an extended-thinking budget, although the
  SDK's own default is now adaptive thinking. `Microsoft.Bcl.AsyncInterfaces`,
  `Microsoft.Bcl.HashCode` and `Microsoft.Bcl.Memory` leave the dependency graph of `Tracon.Core`
  (and so of `Tracon` and `Tracon.Google`): the token-counting data package now ships a `net8.0`
  build.
- **The console build tools move to their newest lines; building the console needs Node.js
  22.22.2 or later on the 22 line, 24.15 or later on the 24 line, or 26 and above.** TypeScript
  `7`, Vite `8` (Rolldown), Vitest `5`, jsdom `30` and React `19.3.1`. The embedded console grows from 211.9 KB to 215.4 KB gzip (budget 250 KB), and the
  embeddable widget (`embed/embed.js`) still runs in strict mode: Vite 8 dropped the
  `"use strict"` directive and the build now asks for it. The embedded file set, the public API
  and the HTTP contract do not change. A contributor on Node.js 20 now gets an `npm ci` engine
  error that names the required version, not a wall of failing tests. The NuGet packages are not
  affected: a consumer does not build the console.
- **The compiled `dist/` of `@tracon/client` is byte for byte the same, in the same 23 files.**
  Only the development dependencies in its `package.json` move (Vitest `5`, `@types/node`
  `26.6`); a consumer installs the same code and the same runtime dependency. It still builds
  with TypeScript `5.9`, because `openapi-typescript` `7` needs the TypeScript 5 compiler API
  that TypeScript 7 does not ship.

### Removed

- **`McpServerDefinition.AuthorizationConfigurationKey` and `McpServerRequest.AuthorizationConfigurationKey`
  are gone** (`authorizationConfigurationKey` on the wire; deprecated since `1.0.0-preview.3`). The
  `Authorization` header is declared like any other credential header:
  `HeaderConfigurationKeys["Authorization"]` (`headerConfigurationKeys` on the wire). Code that sets
  or reads the member stops compiling. The same members leave the generated `Tracon.Client` types
  `McpServerDefinition` and `McpServerRequest` and the `@tracon/client` schema, and the field leaves
  both HTTP schemas. A third-party `IMcpServerStore` that persisted the field drops it and must keep
  `HeaderConfigurationKeys` (the shipped `McpServerStoreContract` gains
  `Every_field_of_a_server_survives_a_round_trip`). **`PUT /api/mcp-servers/{name}` refuses a body that
  still carries `authorizationConfigurationKey`**, with any value and in any letter case, with `400`
  (title `Removed field`) and a detail that names `headerConfigurationKeys["Authorization"]`. Without
  the rule the field would be dropped silently and the first tool call would fail with `401`. Other
  unknown fields are still ignored. The console form writes the key name into
  `headerConfigurationKeys.Authorization`; an empty input sends no map, so a stored map is kept. The
  migration (see Upgrading) moves the stored data first.
- **Twelve interfaces and the 23 types that only they used are `internal`.** They are plumbing
  between Tracon's own packages. No guide, sample or contract suite asks a consumer to implement
  one, and the shipped implementations are the only ones. `IToolRegistry`, `IVoiceHealthCheck` and
  `IWorkflowRunner` stay public: a guide names the first two, and `IWorkflowRunner` is the one
  public way to run a workflow in process. A host that registered its own implementation of one of
  these interfaces, or named one of the types, stops compiling. The types, by package:
  - `Tracon.Abstractions` (34): `IAuditActorResolver`, `IAuditDecorated`, `IMcpOAuthCoordinator`,
    `IMcpPromptClient`, `IMcpResourceClient`, `IMcpResourceContextProviderFactory`,
    `IMcpToolRefresher`, `IStatePreflightReader`, `ITraconDrainState`, `IWebhookPublisher`,
    `IWorkflowFunctionCatalog`, `McpOAuthCompleteResult`, `McpOAuthOperationStatus`,
    `McpOAuthStartResult`, `McpOperationStatus`, `McpPromptArgumentSummary`, `McpPromptContent`,
    `McpPromptListResult`, `McpPromptSummary`, `McpRefreshOutcome`, `McpResourceContent`,
    `McpResourceListResult`, `McpResourceSummary`, `StateGenerationTally`,
    `StatePreflightTarget`, `StateSample`, `WebhookApprovalSummary`, `WebhookEvalSummary`,
    `WebhookEventPayload`, `WebhookJobSummary`, `WebhookQuotaSummary`, `WebhookRunSummary`,
    `WebhookScoreSummary`, `WorkflowFunctionDescriptor`.
  - `Tracon.AspNetCore` (1): `ITraconUiProvider`.
- **`TraconAgentSessionStore` is `internal`.** Its base type, `AgentSessionStore`, has been
  `[Experimental]` since Agent Framework `1.22`, and a public type cannot freeze a base its owner
  may still change. `/v1/responses` keeps using it unless the host registers its own
  `AgentSessionStore`; that registration always wins. No guide or sample built the class. A host
  that did, to wrap it in `IsolationKeyScopedAgentSessionStore`, supplies its own store instead. A
  store the host supplies replaces the Tracon-backed one for `/v1/responses` entirely: the tenant
  filter, the session ownership check and the sessions API of Tracon do not see those
  conversations.
  The store keeps session identities as before: the `isolation` partition of a key is folded into
  the identity as `<escaped key>::<session id>`, and a partition it does not know is refused.
- **Four members that never worked or never did anything are gone.**
  `OpenAILiveOptions.BackendModel` and `LiveVoiceSessionOptions.BackendModel` were read by
  nothing. `QuotaUsageQuery.AsOf` was `[Obsolete]` since `1.0.0-preview.3` and no store applied
  it; use `PeriodStarts`. `LiveVoiceDelegationMode.Responses` made every session that chose it fail
  at the provider with a `502`; the enum keeps `Client`, the only mode. A `BackendModel` key left
  in configuration is ignored and the host starts.
- **81 HTTP request and response types are `internal`.** They were public only so that the
  OpenAPI document could carry their descriptions. A .NET client does not use them:
  `Tracon.Client` generates its own types, and the HTTP contract does not change. Paths, methods,
  schema names, `required`, property types and `$ref` are identical, and the document keeps every
  description (see Fixed for the five that changed). A host that referenced one of these types
  declares its own record for the body or uses the generated client type of the same name. The
  descriptions now come from `[Description]`, so a new HTTP type must carry it; a test fails the
  build when a schema or property loses its description. The types, by package:
  - `Tracon.Abstractions` (17): `AgentValidationReport`, `CanaryDecisionKind`,
    `CanaryEvaluation`, `ContextWindowEstimate`, `DataSubjectErasureResult`,
    `ImageGenerationOperatorRequest`, `ImageGenerationOperatorResponse`, `JudgeFailure`,
    `JudgeRunResponse`, `RetentionPreview`, `RunCostRecalculationResult`,
    `SessionBranchRequest`, `SessionBranchResult`, `SpeakRequest`, `SpeakResponse`,
    `ValidationMessage`, `ValidationSeverity`.
  - `Tracon.AspNetCore` (63): `AcceptedRunResponse`, `AgentDetailResponse`,
    `AgentRollbackRequest`, `AgentSkillRequest`, `AgentVersionDiffResponse`,
    `ApiKeyCreateRequest`, `ApprovalDecisionRequest`, `CurrentTenantResponse`, `EvalCaseInput`,
    `EvalCasePromotionRequest`, `EvalRunDetailResponse`, `EvalRunTriggerRequest`,
    `EvalSuiteSaveRequest`, `ExperimentCanaryResponse`, `ExperimentResultsResponse`,
    `ExperimentSaveRequest`, `InboundTriggerAcceptedResponse`, `InboundTriggerResponse`,
    `InboundTriggerSaveRequest`, `JobDetailResponse`, `JobScheduleSaveRequest`,
    `JobTriggerRequest`, `LiveVoiceSessionCreateRequest`, `LiveVoiceSessionCreateResponse`,
    `LiveVoiceSessionStatusResponse`, `LoopCriterionCatalogResponse`,
    `LoopCriterionKindResponse`, `LoopJudgeModelResponse`, `McpOAuthStartResponse`,
    `McpPromptArgumentsRequest`, `McpRefreshResponse`, `QuotaSaveRequest`, `QuotaUsageResponse`,
    `RetentionPolicySaveRequest`, `RetentionRunTriggerResponse`, `RunComparisonResponse`,
    `RunComparisonSide`, `RunFeedbackRequest`, `RunInputResponse`, `RunReplayResponse`,
    `SearchKnowledgeHit`, `SessionDetailResponse`, `SkillScriptGrantRequest`,
    `TenantEgressPolicyRequest`, `TenantEgressPolicyResponse`, `TenantProviderBindingRequest`,
    `TenantProviderBindingResponse`, `TenantRequest`, `ToolApprovalRuleRequest`,
    `TraconAuthenticationMeta`, `TraconMetaResponse`, `TraconRoleMeta`, `TraconStorageMeta`,
    `UploadDocumentResponse`, `WebhookSaveRequest`, `WebhookTestResponse`,
    `WorkflowFunctionResponse`, `WorkflowRespondHttpRequest`, `WorkflowResumeHttpRequest`,
    `WorkflowRollbackRequest`, `WorkflowRunHttpRequest`, `WorkflowSaveRequest`,
    `WorkflowVersionDiffResponse`.
  - `Tracon.Core` (1): `OnlineEvaluationSummary`.

- `Tracon.Client.Generated.IMcpToolRefresher` and the `body` parameter of
  `TraconApiClient.TraconRefreshMcpToolsAsync`. The class was never a request body: it was
  the server's own refresher service, published by mistake as a schema. A call that passed
  `null` or nothing as the body drops the argument; `TraconRefreshMcpToolsAsync(cancellationToken)`
  sends the same request.

### Added

- **`WorkflowRunRequest.DefinitionVersion`.** The version of a stored workflow's definition to
  run, the same rule as `WorkflowResumeRequest.DefinitionVersion`: the run compiles that version
  and records it on its row. `null` (the default) runs the current definition; a version the
  workflow does not have fails the run and never falls back; a workflow defined in code ignores
  the value. The HTTP API does not set it.
- **`JobStoreContract` checks a job time limit beyond 24.8 days.**
  `A_jobs_time_limit_beyond_an_int_of_milliseconds_survives_the_round_trip_and_the_lease` writes a
  30-day limit and reads it back from the enqueue, the read and the lease. A third-party
  `IJobStore` that keeps the limit in a 32-bit millisecond column now fails the contract.

### Fixed

- A queued workflow run that recovery starts again, because the interrupted attempt wrote no
  checkpoint, now compiles the definition version that attempt recorded. It compiled the current
  definition, so a save made while the job waited changed what ran in the same session. Batch
  items restart the same way.
- Cancelling a run at the moment it finishes no longer fails with `ObjectDisposedException`.
  `POST /api/runs/{id}/cancel` answers `409` instead of `500`, and cancelling a run tree still
  reaches every sibling. Closing a live voice session while a delegation finishes no longer
  ends the close with the same exception.
- `UseLiveVoice(options => options.DelegationMode = …)` with a value outside the enum stops the
  host at startup. It reached the provider on the first session and ended as `500`.

- Anthropic prompt caching with `anthropic.promptCaching.placement = system` keeps the cached
  instructions in the top-level `system` array under Anthropic SDK `12.58`. The SDK adapter now
  moves only the system messages in front of the first user or assistant message there; the
  provider appended the instructions block after the conversation, so it went out as a
  `role: system` entry inside `messages` and the cache breakpoint sat in the conversation. The
  block is now placed in front of the first non-system message.

- Five property descriptions in the OpenAPI document no longer carry an unresolved XML
  reference. `WorkflowSaveRequest.nodes` read `IReadOnlyList&lt;string&gt;? WorkflowSaveRequest.AgentNames`
  where the text means `AgentNames`; `DataSubjectErasureResult.dryRun`, `JobScheduleSaveRequest.lane`,
  `InboundTriggerSaveRequest.payloadPath` and `InboundTriggerResponse.payloadPath` had the same
  fault. `@tracon/client` and `Tracon.Client` carry the corrected text.

- `POST /api/mcp-servers/refresh` no longer declares a request body. The endpoint took its
  refresher service without `[FromServices]`; in a host without `Tracon.Mcp` the framework read
  the service from the body instead, so the OpenAPI document, `@tracon/client` and `Tracon.Client`
  carried a body schema (`IMcpToolRefresher`) for four preview releases. The request itself is
  unchanged. A test now fails the build when any endpoint reads a service interface from its body.

## [1.0.0-preview.4] - 2026-10-15

Workflows become a control plane for long-running pipelines that a person
reviews. A run can be queued and resumes after a restart; a waiting request has
one answer, an inbox and a time limit; every step has a time limit,
cancellation, a cost record and a budget gate; and a graph written in code gets
the same node capabilities as a saved chain, with a revision loop that is safe
on a back edge. Speech gains multi-speaker dialogue and named providers chosen
per tenant. The release changes the public API and adds seven migrations to
each SQL provider: read Upgrading from 1.0.0-preview.3 first.

### Upgrading from 1.0.0-preview.3

- **Migrations.** PostgreSQL `0055`–`0061`, SQL Server `0043`–`0049` and SQLite
  `0042`–`0048`. With `AutoApplyMigrations` (the default) they run at startup.
- **Five public types changed.** Each entry below says what to do:
  - `IRunStore` gains `RecordNonTokenUsageAsync` and `ListNonTokenUsageAsync`; a
    custom store adds both (Changed).
  - `IWorkflowDefinitionStore` gains `GetVersionAsync`, `ListVersionsAsync` and
    `RollbackAsync`; a custom store keeps the version history (Changed).
  - `IWorkflowFunctionCatalog` gains `ValidateSettings` (Changed).
  - `TraconApiClient`: three methods gain optional parameters; a call that passes
    `skip`, `take` or the cancellation token by position names the argument.
  - `ModelProviderSettingsContract.SupportedSetting` is now `virtual` and nullable;
    an existing override adds `?` to its return type.
- **One source break:** `AddWorkflowFunction<TInput, TOutput>(name, factory, null)`
  is ambiguous (`CS0121`); pass a description or use the overload that takes
  options.
- **Behavior changes most likely to reach an upgrade**, each described in its
  entry below: a configuration value Tracon cannot read stops startup; answering
  a workflow request is audited before it runs, and an audit store that cannot
  write refuses the answer with `500`; knowledge-base embedding applies the
  tenant's egress policy; every run of a tree adds its spend to the quota; a
  harness-loop agent hands only its final iteration to a workflow step, the
  OpenAI-compatible and A2A streams and voice; `functions` and `requests` are
  reserved workflow names.
- Pin every Tracon package to the same exact version. The pre-release Microsoft
  Agent Framework hosting packages stay pinned at their 1.20.0 previews.

### Known limitations

- The native agent stream (`POST /api/agents/{name}/run`) streams every
  harness-loop iteration, the loop's feedback included, with no boundary between
  iterations. A client that shows that stream as the answer shows the drafts.
  Read the run's `loop.iteration-completed` events, or use a surface that
  returns the final iteration (the non-streaming run, the OpenAI-compatible
  endpoints, a workflow step).
- In a graph written in code, an agent step's version pin is fixed when the graph
  is built, and the graph is built for every run; there is no pin per production.
  A revision that runs after a new agent version is saved uses that version.

### Security

- Answering a workflow's pending human request is now the seventh fail-closed audit
  operation. Every `POST /api/workflows/runs/{runId}/respond` that starts a run writes
  `workflow.respond` (the source run, the request id, the run the answer starts, the
  response mode and a SHA-256 of the answer — never the answer itself) before the run
  starts, queued or streamed, in every response mode. **Behavior change:** when the audit
  store cannot write the entry, the answer is refused with `500` and no run, record or job
  is created; before, an answer was never audited. `IAuditLog`'s documented set grows from
  six operations to seven. The hash tells two answers apart; it does not hide an answer
  with few possible values, such as a yes or no.

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
- `Tracon.SqlServer` no longer restores a `System.Security.Cryptography.Pkcs` with known
  vulnerabilities (GHSA-846f-f764-76mx, high; GHSA-2fmv-rv2c-59x7, moderate).
  `Microsoft.Data.SqlClient` pulls 8.0.1 on `net8.0` and 9.0.13 on `net9.0` and `net10.0`,
  and every SqlClient release up to 7.1.1 does the same, so a new project that used
  `--persistence sqlserver` restored with `NU1902` and `NU1903`. The package now depends on the
  patched version for each target framework (8.0.2, 9.0.21, 10.0.13), which wins over
  SqlClient's own. It adds no further packages to the graph.

### Fixed

- A workflow run that reaches its time limit no longer ends its stream with an exception. When the
  limit fell between the moment an event arrived and the moment it was recorded, recording it on
  the run's cancelled token threw `TaskCanceledException` out of the stream: the run was closed
  `Canceled`, but a streaming caller got the exception instead of the closing event. The run now
  stops there as it does between two steps.
- A job released for another attempt no longer loses that attempt. A failed attempt with attempts
  left makes the job leasable at once, and the worker could lease it again while the attempt that
  released it was still finishing on the same worker. It logged "already executing" and dropped the
  new lease, but the store had already counted the attempt: the job stayed `Leased` until the lease
  ran out and then failed as out of attempts without running again. The new lease now waits for the
  previous attempt and runs, unless that attempt finished the job.
- A pending workflow request whose type is a record of the graph's own is answered correctly. The run that
  answers reads the request back from a checkpoint, where its data is JSON, and the plan-review check asked the
  framework to read that JSON as a Magentic plan review, every member of which is optional: a record such as
  `{"text": "...", "violation": null}` was taken for one, and the answer was built the wrong response type
  (`Message type MagenticPlanReviewResponse does not match expected response type`). The check now reads the
  port's declared request type. The run that published the request held the live object and never showed the defect.

- A harness loop agent no longer hands its drafts to the caller that acts on its
  answer. A streaming loop yields every iteration, its drafts and its feedback
  included, and six surfaces read that stream as the answer: the next node of a
  workflow (a text-to-speech node spoke the rejected version), the next agent
  of a ready-made Sequential workflow, the streaming
  `/v1/chat/completions` and `/v1/responses`, the streaming A2A call, and the two
  voice surfaces, which spoke the drafts aloud. These surfaces now run a loop
  agent through the non-streaming path and stream its final iteration only; its
  usage stays the sum of every iteration. **Behavior change:** on these
  surfaces a loop agent's answer arrives in one piece when the loop ends, not
  token by token. The native `POST /api/agents/{name}/run` stream is unchanged
  and still streams every iteration. A custom `IAgentDecorator` that does not
  delegate `GetService` hides the loop, and the agent then streams as before.
- An approval that ran out of time no longer erases what the run that asked had recorded. The
  expiry closed the run `Failed` with a completion built from nothing, and `IRunStore.CompleteRunAsync`
  replaces the event count, usage and cost of the row with what the completion carries, so a run that
  had spent tokens before it paused lost its usage, its cost and its event count. The expiry now reads
  the run first, closes it only while it is still `AwaitingApproval` (a run something else closed keeps
  its outcome), and writes those values back.
- `Tracon:Retention:IdempotencyKeys` is now bound from configuration. Its `MaxAgeDays`
  and `Archive` keys were ignored and the target kept its one-day default; only code
  (`Configure<TraconRetentionOptions>`) could change it. A configuration that sets
  them now takes effect, and `MaxAgeDays` below 1 fails startup like every other
  target.
- `/v1/chat/completions` now reports why the answer ended. `finish_reason` was always
  `"stop"`, so an OpenAI SDK caller could not tell a cut-off answer from a complete
  one. **Behavior change:** an answer the model stopped at its output token limit or
  at its context window is `"length"`, and an answer a content filter cut off after
  it produced text is `"content_filter"`, on the JSON response and on the last chunk
  of the stream (`data: [DONE]` still ends it). The value comes from the answer's
  final model call; a tool turn's reason is never reported, and run recording does
  not need to be on. A filtered answer with no content still fails with `502`
  (`error` on the stream), and a cut-off structured answer still fails as before.
- A failed read of the quota threshold notice no longer fails the agent run's stream.
  With `TraconQuotaOptions.PublishThresholdToRunStream` on, a run store error while
  the stream read the notice back ended the stream of a completed run with an
  `error` frame. **Behavior change:** the stream now logs a warning and ends with
  `done`, without the notice; the notice stays on `GET /api/runs/{id}/events`.
- A tenant's quota now counts what a call tree spends after its root run ended. The root
  read its tree from the run store once, while its own row closed, so a child agent still
  running in the background at that moment (a root that starts a task and answers without
  waiting) never reached a counter, and neither did the amount a tool reported after its
  call timed out and its run closed. **Behavior change:** every run of a tree now adds its
  own tokens and cost to the counter when it ends, under the root's tenant and name
  (a child adds no run: the tree is still one run), and a tool amount that arrives after
  its run closed is added to the counter once, in `Tracon:Pricing:Currency`. The counter
  and `GET /api/runs/{runId}` give the exact total; the `run.completed` webhook is
  unchanged and shows the tree as it stands when the root closes. A threshold crossed by a
  child run is written to that child's own event stream (with
  `Quotas:PublishThresholdToRunStream`; read it from `GET /api/runs/{runId}/events`) and
  its `quota.threshold` webhook names that run; a tool amount that crosses one is announced
  by the webhook only.
  A child priced in another currency than `Tracon:Pricing:Currency` adds its tokens and
  the amounts it reported beyond tokens, not its token cost. A store of your own needs
  no change; `QuotaStoreContract` has two cases for an entry that adds tokens and cost and no
  run.
- A job no longer repeats a failure the next attempt would get again. The worker
  retried every handler failure up to the attempt limit: an eval whose judge was cut
  off at its output limit ran its unfinished case and paid for it on every attempt,
  and a queued run whose model answered a permanent `4xx` ran again too.
  **Behavior change:** a failure whose stable type is `output_truncated` or
  `tenant_provider_refused`, or a model provider failure
  (`upstream_error`) with a `4xx` other than `408` and `429`, fails the job on that
  attempt. The decision reads the type, never the message; an exception a consumer
  handler throws itself keeps its retries, and so does `JobRetryException`. A workflow
  node retry and the online evaluation read the same decision.
- A tenant's refusal of a provider is no longer classified by its text. Its error
  type was the .NET type name, and a tenant or provider name with "timeout" or "429"
  in it made the refusal a transient fault that a workflow node retried.
  **Behavior change:** every refusal (model, fallback, summarizer, loop judge, image,
  speech, embeddings, live voice) is the new `TraconTenantProviderRefusedException`,
  error type `tenant_provider_refused`, with the new class
  `RunErrorClass.TenantProviderRefused` (in the OpenAPI document and both generated
  clients). A compilation no longer wraps it as `TraconCompilationException`
  (`compilation_failed`); the message still names the agent. A binding that names a
  configuration key the tenant may not read is a refusal too: the image, speech and
  knowledge endpoints answer `403` for it, not `500`. The model path's refusal of a tenant credential its
  provider cannot use was `provider_credential_unsupported` and is now
  `tenant_provider_refused` too. The image and live voice refusals name their kind of
  provider instead of "model provider".
- A provider failure masked as `upstream_error` that answered `429` is now
  `RateLimited`, not `ProviderError`, and a status .NET carries only on
  `HttpRequestException.StatusCode` now reaches the record (`(HTTP 401)`).
- An Anthropic answer cut off by the context window (`model_context_window_exceeded`)
  and a turn the API paused (`pause_turn`) no longer complete the run as finished.
  The SDK's adapter reported both as a plain stop. A cut-off structured answer now
  fails with `output_truncated` and a plain one writes `tracon.output.truncated`,
  both naming the context window; the event payload gains a `cause` field
  (`output_limit` or `context_window`). A paused turn is continued, its blocks sent
  back as the API returned them, up to five times; a turn still paused fails the call.
  New: `TraconFinishReasons.ContextWindowExceeded`, a finish reason any provider can
  report.
- A SQL store write that the server picks as a deadlock victim is run again. The SQL
  Server upsert gave error 1205 to its caller under concurrent writes. Every store
  transaction now opens at one boundary that runs the whole transaction again, up to
  five times, on a new connection.
- A failed webhook send whose result could not be recorded kept its retry delay. The
  store error used to replace the send's failure: the job was released at once, and on
  the last attempt the delivery stayed `Pending`. It now ends `Failed`.
- `TraconToolUsage.Report` refuses a measurement the record cannot hold, as a function
  node's item is refused. An oversize unit, provider or model reached the insert, and
  on PostgreSQL and SQL Server the failed insert lost the call's whole record.
- `GET /api/voice/voices` answers `502` when the provider fails, as
  `POST /api/voice/speak` does; it answered `500` (new in the OpenAPI document and
  both generated clients).
- The first function node of a workflow node list runs once per run. A run sends the
  first node the user's message and then Microsoft Agent Framework's turn token, on every
  start and resume, and a first node that takes `object` received both and ran twice — a
  node with an outside side effect did it twice. **Behavior change:** saving a node list
  whose first node takes `object` answers `400`, and a stored one fails before its first
  node runs; declare `List<ChatMessage>` or `IEnumerable<ChatMessage>`.
- No money total adds amounts in two currencies. A run tree, the totals and breakdowns
  of `GET /api/stats`, the points of `GET /api/stats/timeseries`, an experiment arm and
  the online evaluation's judge cost added the runs' amounts and labelled the sum with one
  of their currencies (the in-memory store the first, the SQL stores the alphabetically
  largest). When the runs are in more than one currency, the total now keeps no amount
  and no currency, and the new `CurrencyCount` says how many there are; letter case is
  not a second currency. `RunSpend.Total` for a tree returns `null` for such a tree, so
  the quota and the `run.completed` webhook no longer count a mixed sum: an agent root
  counts its own cost. The console shows *mixed currencies*.
- A run whose caller can never answer its tool approval no longer waits forever.
  `/v1/chat/completions` answered `200` with empty content; it now answers `409` with
  the error type `approval_required` (or, on a stream, an error frame of that type and
  no `[DONE]`). `POST /api/agents/{name}/run` without a `sessionId` answered `200`; it
  now answers `409` with `errorType` `approval_unanswerable` (or an `error` event
  instead of `done`). In both, and for an agent exposed over MCP or A2A, the run closes
  `Failed` with the error type `approval_unanswerable`; an MCP task poller still sees
  the refusal. The `409` responses are new in the OpenAPI document and both generated
  clients.
- A queued turn that ends at a tool approval no longer calls the model again when the
  approval cannot be written. The write is tried again within the lease, and a write
  the store committed but reported as failed is not written twice; past the lease the
  job fails with `tracon.job.approval-save-failed` and the run closes `Failed`.
- A structured-output agent (`Json` or `JsonSchema` with validation on) whose turn
  stops at a tool approval or at a client-side tool call no longer fails with "The
  response is empty." Such a pause is not validated; the turn that carries the decision
  or the tool result is.
- A workflow run stopped by a graceful shutdown keeps its checkpoints while
  `ResumeInterruptedRuns` is on, also with `KeepCheckpointsAfterCompletion` off. They
  were deleted when the stopped run closed, and its continuation ran every finished step
  again; the continuation now removes them when it finishes.
- An agent can name a skill that exists only under a skill root (`SkillRoots`). The
  catalog and the definition check looked in code and in the store only, so such an
  agent failed with "the skill was not found" and no HTTP run reached a script on disk.
  A disk skill is installation-wide like a code skill; a stored skill under the same
  name shadows it for its tenant. An edit to its `SKILL.md` reaches the next run.
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

- Function nodes and agent steps for a workflow graph built in code. `GetWorkflowFunction(name[, configure])`
  binds a function registered with `AddWorkflowFunction` and `GetWorkflowAgentStep(workflow, agent[, configure])`
  binds a catalog agent as a plain step, so a hand-built graph with a way back and a human gate gets what a
  stored chain's steps have: the time limit, the retry policy, the budget gate, a cost line that names the node,
  the strict read of the settings (`WorkflowFunctionStepOptions.Settings`, or typed with `SetSettings`) and an agent
  step's own timeout and version pin (`WorkflowAgentStepOptions`). Until now the wrapper that carries them was
  reachable only from a stored chain's compiler, and the budget gate could not be built in consumer code. An agent
  bound with `GetWorkflowAgent` still cannot be the target of a back edge (it runs only after a turn token), so the
  agent step is the way to put an agent where a message returns. An unknown function or settings that do not fit
  end the run `Failed` before its first node, naming the workflow and the node; a version pin that cannot hold
  fails the step by name before its model call.
- `WorkflowRegistrationOptions.RunTimeout` and `WorkflowRegistrationOptions.Parameters` give a workflow defined in
  code the run time limit and the parameter schema a stored definition has. The run timeout shortens
  `Tracon:Workflows:RunTimeout` or lengthens it up to `MaxRunTimeout`, and a value above the ceiling stops the host
  at startup, naming the workflow. With a schema, `GET /api/workflows` lists it, the run endpoint answers `400`
  early for an unknown or missing value, a queued run carries the values, and a run that is answered or resumed
  uses the recorded ones. Without one, every value is still refused as unknown.
- `WorkflowFunctionOptions.MaxConcurrency` limits how many calls of a function run at the same time in the process,
  across workflows and tenants, in a stored chain and in a graph built in code. A call that finds every slot taken
  waits; the wait ends when its run is cancelled or reaches its deadline and is not part of the node's `Timeout`;
  each attempt of a retried node takes a slot of its own; the budget gate is read after the slot is taken. The limit
  is per process, so replicas multiply it.
- A workflow run that the request expiry service fails now publishes `run.failed`, with the payload any failed
  workflow run carries. Until now such a run was closed in the store and the webhooks heard nothing, so a pipeline
  driven by webhooks waited for a run that had ended. A run closed `Completed` (every request answered) publishes
  nothing: its answer's own run published `run.completed`.

- A list of the human requests a tenant's workflow runs wait on, and a time limit for them.
  `GET /api/workflows/requests` (`state` is `pending`, `answered` or `all`; `workflowName`,
  `skip`, `take`) lists one item per request, oldest first, across every workflow; it needs the
  `Reader` role and read access to runs, and a registered `IRunAuthorizationHandler` that refuses
  reading runs rejects it with `403` instead of filtering it. `WorkflowPendingRequest` gains
  `workflowName`, `sessionId`, `expiresAt`, `answered` and `lastResponseRunId`, on this list and on
  `GET /api/workflows/runs/{runId}/requests`; `IWorkflowRunner` gains `ListRequestsAsync`, with a
  default that lists nothing. The same request id in the same session is one request: an answered
  run stays `AwaitingInput` and its continuation publishes the open requests again, so the item
  names the newest run that waits on it. The list is built from a new `workflow_requests` table
  (one migration for each SQL provider) that the engine writes when a run starts waiting and the
  respond endpoint marks answered; it holds no copy of the prompt, which is read from the run's
  event. A run that started waiting before the migration has no row and is not in the list. The
  console lists the same requests in a *Workflow requests* panel on the Approvals screen.
- A workflow can give its human requests a time limit. `WorkflowDefinition.RequestTimeout`
  (`requestTimeout` on `PUT /api/workflows/{name}`, the *Answer time limit* field in the console's
  editor, `WorkflowRegistrationOptions.RequestTimeout` for a workflow defined in code) is from one
  minute to 365 days and counts from the moment the run published the request. A background scan
  (`Tracon:Workflows:RequestExpirationScanInterval`, one minute, and
  `MaxRequestExpirationsPerScan`, 100; one instance of a cluster scans) closes the run when its last
  request passes: `Failed` with the error type `WorkflowRequestExpired` (error class `Timeout`) when
  something went unanswered, `Completed` without an error when every request had been answered. An
  answer to a closed run is refused with `400`, as for any run that is not `AwaitingInput`. The
  run keeps its usage, cost and event count. A run that waits keeps the limit of the version it
  started with. This is what lets retention release what a waiting run keeps: its request event,
  checkpoints and run input row were never deleted while it waited, and an answered run waits too.
  The new `workflow_requests` retention target
  (`Tracon:Retention:WorkflowRequests`, 30 days after the request expired) deletes the closed rows of
  the list. **Behavior change:** `requests` joins `functions` as a workflow name that cannot be
  saved, because `GET /api/workflows/requests` matches before `GET /api/workflows/{name}`.
- `Tracon.Testing.Contracts.Xunit`: `WorkflowRequestStoreContract` for `IWorkflowRequestStore`, the
  seam behind the list; a custom store passes it the way it passes the other store contracts. The
  request store also lists the runs a stopped process left waiting
  (`IWorkflowRequestStore.ListStrandedAsync`): the scan closes a request before its run, and the
  instance that takes the scan lease next closes the runs whose requests were closed and which
  still wait. `WorkflowDefinitionStoreContract` gains `The_request_timeout_survives_the_round_trip`;
  a custom `IWorkflowDefinitionStore` that keeps its own copy of a definition must carry
  `RequestTimeout`.
- One answer per workflow request. `WorkflowResponseMode` (`Branch`, the default, and
  `SingleAnswer`) is set on a stored definition (`WorkflowDefinition.ResponseMode`,
  `responseMode` on `PUT /api/workflows/{name}`) or on a workflow defined in code with the
  new `AddWorkflow(name, factory, Action<WorkflowRegistrationOptions>)` overload, and is
  reported on `WorkflowDescriptor`. In `SingleAnswer`, two identical answers to one
  request — a double click, a retried call — start one run: the second follows it (the
  same SSE frames read from the stored run, or the same `202` with
  `Prefer: respond-async`), and a different answer gets `409`, which names neither the run
  nor the other answer. An answer whose run failed or was cancelled before the workflow
  received it can be given again and starts a new run; a queued answer whose job is still
  waiting or running is followed instead, because the job's recovery owns that work. Two
  answers that arrive together may both open a run before one of them claims the request:
  the other closes its own run as `Canceled` with the error type `request_already_answered`
  and no events (no webhook, metric or event sink fires for it), and its audit entry names
  that run. That run is still a run: it is listed by `GET /api/runs` and counted in
  `RunStatistics.CanceledRuns`, so it is part of the `ErrorRate` denominator. A waiting run is
  answered under the mode of the definition version it recorded. `Branch` keeps opening a new
  run for every answer. A queued `SingleAnswer` answer whose job was never written (the
  process stopped after the answer was recorded and before the job was queued, or the queue
  refused the job and its `Queued` run could not be closed) is finished by a repeat of the
  same answer: the repeat writes the job and follows the run, and two repeats at the same
  time write it once. **Known limit:** when a process stops in the middle of a streamed
  `SingleAnswer` run, the run stays `Running` and a repeat of the answer follows it forever.
  Only run reconciliation (`Tracon:RunReconciliation:Enabled`, off by default) closes it:
  reconciliation closes `Running` runs whose heartbeat stopped and reads no `Queued` run.
  Turn reconciliation on where such a request must stay answerable after a crash.
- `GET /api/workflows/runs/{runId}/responses` lists the answers given to a run's requests:
  who answered, when, the run each answer started and its state. The records live in the
  new `IWorkflowResponseStore` (in memory by default; the new `workflow_responses` table
  on PostgreSQL, SQL Server and SQLite). A record is removed only with its source run, by a
  data subject erasure; no retention target deletes runs, so the table grows with the run
  history. A store implementation is checked with the new
  `WorkflowResponseStoreContract` in `Tracon.Testing.Contracts.Xunit`.
- `RunEventType.WorkflowResponseApplied` (`workflow.response-applied` on
  `GET /api/runs/{runId}/events`): the run an answer starts names the run it answered and
  the request id right after it starts, even with `RecordToolPayloads` off. The console's
  run timeline links it back to the source run.
- The console shows which pending requests already have an answer, links the run that
  answer started, says when a one-answer workflow refused a different answer, and has a
  "One answer per request" switch in the workflow editor and the version comparison.

- The live stream of an agent run (`POST /api/agents/{name}/run` without
  `Idempotency-Key`) now says when the answer was cut off. A plain-text answer the
  model stopped at its output token limit or at its context window completes, and
  its `tracon.output.truncated` event (`RunEventCustomTypes.OutputTruncated`) was
  only on `GET /api/runs/{id}/events`; a client that read the live stream took the
  partial answer for a complete one. The stream now sends that event as a `custom`
  frame right before `done`, always, with the same data `/events` serves. Each
  recorded event is one frame: a harness loop whose iterations were cut sends more
  than one, and a tool turn cut off in the middle of the run sends none. With the
  quota notice on, both notices arrive in record order. A client that reads frames
  by name and ignores unknown `customType` values needs no change. No event, and so
  no frame, is written while run recording is off.
- `TraconFinishReasons.ContextWindowExceeded`: the finish reason a model provider
  reports when the conversation and the answer filled the model's context window.
  Tracon treats it as a cut-off answer and names the window.
- `RunErrorClass.TenantProviderRefused` (`17`): the tenant's provider rules refused
  the call before any provider was called.
- `TraconTenantProviderRefusedException`: the one exception every path throws when the
  tenant's provider rules refuse a call (its egress policy, a credential binding that
  cannot be resolved, a provider that cannot take the tenant's own key). Catch it to
  tell the tenant's rule from a provider failure.
- `CurrencyCount` on `RunTreeCost`, `RunNonTokenCost`, `RunStatistics`,
  `TimeSeriesPoint` and `ExperimentVariantResult`, and `JudgeCostCurrencyCount` on
  `OnlineEvaluationSummary`: how many currencies the runs behind a money total are
  priced in. Above one, the total carries no amount (see *Fixed*).
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
- A workflow function node can report what it spent beyond tokens. The new extension
  `IWorkflowContext.ReportUsageAsync(ToolCallUsage)` (in `Tracon.Workflows`) records one
  item: a text-to-speech character, a render second, a minute of audio. It returns
  `ValueTask<bool>` and does not throw except for a `null` argument; it returns `false`
  outside a Tracon workflow run, for an invalid item (an empty `Unit`, a negative quantity
  or amount, or a value beyond what its column holds: a unit of 64 characters, a currency
  of 16, a provider of 100, a model of 200, a quantity above 9,999,999,999, an amount
  above 1,000,000,000) and once the run holds 1,000 items. An item is written at once and
  is not cancelled with the run, so a cancelled or failed run keeps what its node had
  spent, and a retried node keeps the items of its failed attempts. The item names the
  node that reported it; a custom executor in a graph built in code reports with no
  node name. An item reported after the run closed (a node that outlived its timeout)
  stays in the run's detail and does not change the total, the quota entry or the webhook.
- A run has a non-token cost, a total of its own beside the token cost.
  `RunNonTokenCost` (`Amount`, `Currency`, `IsEstimated`, `UnsummedItemCount`) is on
  `RunRecord.NonTokenCost` (the run itself) and `RunRecord.TreeNonTokenCost` (the run and
  every run below it, the run counted once), and `RunCompletion.NonTokenCost` carries it
  to the store when a run closes. `null` means the run reported nothing, which is not
  zero; an `Amount` of `null` with a positive `UnsummedItemCount` means items were
  reported and none could be priced. One rule decides what adds: an item adds only when
  it has an amount in `Tracon:Pricing:Currency` (an item with no currency label is in it).
  Items with no amount, or in another currency, are listed and counted in
  `UnsummedItemCount`, never added or converted, and a quantity is never added across
  units. An estimated item adds and raises `IsEstimated`. Tracon never prices a node's
  work: the node reports the amount it computed. `RunSpend.Total(RunCost?, RunNonTokenCost?)`
  and `RunSpend.Total(RunTreeCost?, RunNonTokenCost?)` are the one place the token cost and
  the reported amount are added; amounts in two currencies are never added.
- `ToolCallUsage.Provider` and `ToolCallUsage.Model`, names for the reader of the record
  that take no part in a total. The built-in `speak`, `transcribe` and `generate_image`
  tools fill them. A tool still reports with `TraconToolUsage.Report`, and its measurement
  now also joins the run's non-token total the moment the tool reports it.
- `GET /api/runs/{runId}/usage` (Reader role, `runs:read` scope) lists the items a run's
  function nodes reported, in the order reported. It lists the run's own items, not its
  children's; a tool's measurement stays on `GET /api/runs/{runId}/tools`. An unknown run,
  another tenant's run and a denied run all answer the same `404`. `GET /api/runs` and
  `GET /api/runs/{runId}` carry `nonTokenCost` and `treeNonTokenCost`. All of it is new in
  the OpenAPI document and both generated clients (`TraconListRunNonTokenUsageAsync`,
  `RunNonTokenCost`, `NonTokenUsageRecord`).
- `NonTokenUsageRecord` and the `IRunStore` members `RecordNonTokenUsageAsync` and
  `ListNonTokenUsageAsync`. Three migrations add a `run_non_token_usage` table (its rows are
  deleted with their run), four snapshot columns on `runs` (`non_token_cost`,
  `non_token_currency`, `non_token_estimated`, `non_token_unsummed`) and `usage_provider` and
  `usage_model` on `tool_invocations`: PostgreSQL `0058`, SQL Server `0046` and SQLite
  `0045`. Existing rows keep `NULL`: nobody reported a number for them.
- The console shows a tool call's measurement (quantity and unit, the amount or
  "unpriced", "estimated", provider and model) and, for a run that reported anything, a
  "Non-token usage" panel ending in "Reported in this run", "Reported in the whole tree"
  and "N not added".
- The `tracon.recording.stage` tag has a new value, `non_token_usage`: an item a function
  node reported could not be recorded. Alert rules that list the stages need it.
- `Tracon.Testing.Contracts.Xunit`: `NonTokenUsageContract`, and new scenarios in
  `RunStoreContract` and `ToolInvocationContract` for the snapshot, the tree total and a
  measurement's provider and model.
- A stored workflow keeps its version history. Every save appends a version and a rollback
  appends the old content as a new one; nothing is rewritten. `GET /api/workflows/{name}/versions`
  (Reader) lists the full snapshots, newest first, `GET /api/workflows/{name}/versions/{a}/diff/{b}`
  returns two of them as `left` and `right`, and `POST /api/workflows/{name}/rollback`
  (Admin, body `WorkflowRollbackRequest`) rolls back. A rollback is judged like a save: a version
  that names a function that is no longer registered, settings that no longer fit, or an agent
  version that no longer exists answers `400` and writes nothing. An unknown version is `404`, a
  workflow defined in code is `409` (its versions read `404`). The audit trail records
  `workflow.rollback` with the version it came from and the one it wrote. A version number is
  never reused: deleting a workflow keeps its history and a workflow saved again under the same
  name continues the numbering. New in the OpenAPI document and both generated clients
  (`TraconListWorkflowVersionsAsync`, `TraconGetWorkflowVersionDiffAsync`,
  `TraconRollbackWorkflowAsync`, `WorkflowVersionDiffResponse`, `WorkflowRollbackRequest`).
- `IWorkflowDefinitionStore.GetVersionAsync`, `ListVersionsAsync` and `RollbackAsync`. Three
  migrations add the `workflow_definition_versions` table (no foreign key to `workflows`):
  PostgreSQL `0059`, SQL Server `0047` and SQLite `0046`. Each stored workflow starts its history
  at the version it has today; the versions it had before were never kept. A save writes the
  current row and its history entry in one transaction.
- A workflow run's row records the version of the definition it was compiled from
  (`RunRecord.AgentVersion` of the workflow's root row; `null` for a workflow defined in
  code and for a row written before this release). A resume or a response is stamped with the
  version it ran.
- `definitionVersion` on `POST /api/workflows/runs/{runId}/resume` and `.../respond`
  (`WorkflowResumeHttpRequest.DefinitionVersion`, `WorkflowRespondHttpRequest.DefinitionVersion`,
  `WorkflowResumeRequest.DefinitionVersion`, `WorkflowRespondRequest.DefinitionVersion`) names the
  stored version to continue with. A version the workflow does not have is `400` before a stream
  opens or a job is queued, and throws before any run row is opened when the engine is called directly; a run is never
  continued with the current definition in its place. A queued resume and a queued response carry it.
- `WorkflowNodeReference.AgentVersion` pins an agent step to one version of that agent. The step
  runs that version's instructions, model and tools while the agent is edited, and its child run
  row records the version. Only an agent node in the `nodes` form can pin; a function node, a
  value below 1, an agent defined in code and a version the agent does not have are refused with
  `400` when the workflow is saved and when it is compiled. The pin is not part of the step's
  identity, so changing it does not make a waiting run's checkpoint unusable. The agent's own
  sub-agents and skills still resolve their current version.
- The console lists a workflow's versions, compares two of them and rolls back, and an agent
  step in the chain editor can pin a version of a stored agent.
- `Tracon.Testing.Contracts.Xunit`: new scenarios in `WorkflowDefinitionStoreContract` for the
  history, the numbering after a delete, rollback, concurrent saves, tenant isolation, a pin's
  round trip and cancellation, and the optional `SupportsRawHistory`, `ExecuteRawAsync`,
  `QualifiedWorkflowHistoryTable` and `RawNowExpression` hooks a SQL provider overrides to prove
  that a failed history write leaves the current definition as it was.

### Changed

- The agent step of a workflow, in a stored chain and in a graph built in code, now asks the agent for its whole
  answer instead of a stream. The step never forwarded a delta to the workflow's feed, so the stream bought
  nothing, and the streaming path has no structured-response repair. **Behavior change:** a step whose model
  answers JSON that does not fit the agent's schema is asked again up to `Tracon:StructuredResponse:MaxRepairAttempts`
  (before, the broken answer reached the next node), and the step's own `runs` row has `IsStreaming = false` and a
  `message.completed` event beside the `message.delta` it already had. Its tokens, cost and input are recorded as
  before. A loop agent still hands the next node its final iteration only.

- A workflow function node no longer starts once the call tree's cost or time budget is spent.
  Until now the budget refused a model call and a new agent step but let a function node run
  and do its paid work (speech, a render, a GPU minute), and a chain of function nodes only
  never stopped at all. **Behavior change:** with `Tracon:AgentGraph:MaxTotalCost` or
  `Tracon:AgentGraph:MaxDuration` set, the next function node does not start once the tree has
  passed either; the run ends `Failed` with `run_budget_exceeded` (class `QuotaExceeded`), the
  message names the node and the setting to raise, and no later node runs. Each attempt of a
  retried node is checked, and the refusal is never retried, whatever the registered
  `IRunErrorClassifier` says. `MaxTotalTokens` does not stop a node (it is on by default and a
  function node spends no tokens), and a host that sets neither ceiling is not affected. A node
  that is already running is never stopped, so a tree can pass its ceiling by as much as its last
  node spends. A node that spends nothing and has to run afterwards, such as a step that stores
  what the budget has just paid for, sets the new `WorkflowFunctionOptions.StartsWhenBudgetIsSpent`
  to `true` in its registration; an operator cannot set it in a definition. A run built in code
  with `AddWorkflow` and its own executors is not gated: read
  `TraconRunContext.Current?.Budget` there.
- `POST /api/workflows/runs/{runId}/respond` changes in the default `Branch` mode too.
  The run an answer starts now has its row opened (`Running` for a streamed answer,
  `Queued` for a queued one) before the answer is recorded and before the stream starts,
  and the answer's record is written before the run is handed over. **Behavior change:**
  when the record cannot be written (`workflow_responses` unavailable), the answer is
  refused with `500` and the opened run is closed `Failed` with the error type
  `answer_not_recorded` and no events; before, the answer was not recorded at all. A run
  store that cannot open the row refuses the streamed answer with `500` too; before, the
  engine's run writer logged that failure and the run went on unrecorded. Every
  run an answer starts now carries `WorkflowResponseApplied` at sequence 1, so its later
  events are numbered one higher than before.
- **Behavior change:** the in-memory job store (the default `IJobStore`) refuses a job whose
  id is already queued, as the PostgreSQL, SQL Server and SQLite stores' primary key does;
  before, it replaced the queued job, which could set a leased job back to `Pending`. The
  `IJobStore.EnqueueAsync` documentation now states the refusal, and `JobStoreContract`
  checks it with a new case. A queued run whose enqueue is refused because its job is
  already there (another call wrote it) is no longer closed as `Failed`; the call answers
  with the job that is there.
- `AddWorkflow(builder, name, factory, string? description = null)` is now two overloads,
  `AddWorkflow(builder, name, factory)` and `AddWorkflow(builder, name, factory, string? description)`,
  beside the new one that takes `Action<WorkflowRegistrationOptions>`. Every call compiles
  as before except one that passes a literal `null` as the third argument, which is now
  ambiguous; leave the argument out instead.

- **Behavior change:** a configuration entry Tracon cannot read now stops the
  application at startup instead of being dropped without a word. Four settings
  change:
  - `Tracon:Quotas:ThresholdPercents` and `Tracon:Webhooks:RetryDelays`: an entry that
    is not a number (a `TimeSpan` for the delays), or that is zero or below, was
    skipped, so the list came out shorter than written. It now fails startup and the
    message names the key and quotes the entry (`ThresholdPercents[1] is '9O'`).
  - `Tracon:ContentProtection:Columns`: an entry that is not one `ProtectedColumn`
    name — a misspelling, an empty entry, an undefined number, or two names joined by
    a comma, which was read as a third column — was skipped, and the column you meant
    stayed in plain text. It now fails startup and the message quotes the entry.
  - `Tracon:ContentProtection:Keys`: an entry with an empty configuration key name was
    skipped. It now fails startup and the message names the key id.
  - `Tracon:ContentGuard:Pattern:MaskedPii`: one misspelled name (`"Emial,CreditCard"`)
    turned PII masking off for the whole list, and a number with an undefined bit was
    accepted. It now fails startup and the message quotes the value. An empty value
    still means no pattern.

  The `Columns` and `Keys` checks run while content protection is off too: an old
  encrypted row is still read through `Keys`. A value that code sets for the same
  member after binding replaces the configured one and clears the error. An undefined
  `ProtectedColumn` or `PiiPatterns` value set in code (for example
  `options.MaskedPii = (PiiPatterns)64` in `AddPatternContentGuard`) now fails startup
  as well.
- **Behavior change:** a workflow run that waits to be resumed keeps the definition version
  it recorded. Before, `resume` and `respond` compiled the current definition: an edit made
  while the run waited changed its settings, timeouts and parameter schema without notice, and a
  structural edit (a step added, removed or reordered, the pattern or the stored form changed)
  made the run impossible to resume. Now such an edit changes nothing for that run. To carry a run
  over a fix, name the edited version in `definitionVersion`; a version whose graph differs from
  the checkpoint's fails the run with "the graph's structure differs", as before. A run whose row
  recorded no version (written before this release) resumes with the current definition, and a
  resume of a workflow that was deleted is still refused.
- **Breaking for implementers (preview):** `IWorkflowDefinitionStore` has three more members
  (`GetVersionAsync`, `ListVersionsAsync`, `RollbackAsync`). A store of your own has to keep the
  history; a default implementation would have dropped every pinned step the first time the
  workflow was edited. The in-memory store and the three SQL stores implement them, and
  `WorkflowDefinitionStoreContract` runs against yours.
- The console writes the `nodes` form of a chain when it has a function node **or** a pinned
  agent step (it was: a function node). A chain of agents alone with no pin is still saved as
  `agentNames`. Pinning the first step of such a chain changes the stored form, which is a graph
  change: the editor warns, and a run that waits keeps resuming the version it recorded.

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
- **Source break:** `IRunStore` gains two members, `RecordNonTokenUsageAsync(NonTokenUsageRecord,
  CancellationToken)` and `ListNonTokenUsageAsync(Guid, CancellationToken)`. They have no
  default implementation, so a custom `IRunStore` does not compile until it adds both.
  A store must also keep `RunCompletion.NonTokenCost` on the run and return it, with the
  tree total, as `RunRecord.NonTokenCost` and `TreeNonTokenCost`. Derive the new
  `NonTokenUsageContract` beside `RunStoreContract` to prove it. The shipped stores
  implement both members; only a store of your own needs the change.
- **Behavior change:** `WebhookRunSummary.Cost` on `run.completed` and `run.failed` now
  means the token cost and the amount the run reported beyond tokens, together. It already
  said "the total amount"; a run that reported nothing sends the same number as before.
  The new `WebhookRunSummary.NonTokenCost` is the reported part, absent when nothing was
  reported, so the token part is `Cost - NonTokenCost` whenever both are in the same
  currency. When a tree's token cost and its reported amount are priced in different
  currencies, `Cost` stays the token cost alone and `NonTokenCost` is still given. A
  consumer that read `Cost` as tokens only should subtract it.
- **Behavior change:** the amounts a run reported beyond tokens count against the
  tree budget (`Tracon:AgentGraph:MaxTotalCost`), a tenant's cost quota and the cost in
  both webhooks. An amount that adds is recorded into the budget the moment it is
  reported, so the tree's next model call is refused once the ceiling is passed, and the
  quota entry of an agent root and of a workflow root includes it. A tool's measurement
  joins the same total. A run that reports nothing is unaffected. Still token-only on
  purpose: `GET /api/stats`, the dashboard, the `tracon.run.cost` metric, the comparison
  of two runs and the console's Tools screen. A quota has no limit on a unit such as
  characters. The same ceiling also stops a function node from starting (see the entry that begins
  "A workflow function node no longer starts" at the top of this section).

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
