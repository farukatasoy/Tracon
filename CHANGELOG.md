# Changelog

All notable changes to Tracon are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [Unreleased]

### Upgrading from 1.0.0-preview.6

- **The compiled agent cache holds at most 4096 agents and drops one that is unused for an hour.** It kept one agent for every tenant, agent, definition version and culture for the life
  of the process, so an installation that takes the tenant from a header and lists no `AllowedTenants` let the caller choose how much memory it held: a compiled agent weighs 8.3 KiB with no tools
  and a short instruction and 42 KiB with sixty tools and an 8 KiB instruction (measured; the instruction counts about four bytes per character). An agent that leaves the cache is not
  disposed: a run that holds it keeps using it, and the next run compiles it again, which took 46 to 138 µs and allocated 20 to 66 KiB per agent in the measurement, against the seconds of a model call.
  What to do:
  - **A host with more than 4096 (tenant, agent, definition version, culture) combinations in use at once can raise `Tracon:AgentCache:MaxEntries`.** The default holds 900 entries for a single
    tenant with a hundred agents, three cultures and three live versions of each without reaching it. A cache whose `CompiledAgentCache.Count` sits at the limit all the time is the sign.
  - **`Tracon:AgentCache:IdleLifetime` (one hour) is what removes the entry of a definition version that was saved over.** Raise it for agents that run once a day and must not compile again;
    the value is at most 30 days. The cache has no timer: the check runs while the cache is in use.
  - Nothing else changes: the empty `AllowedTenants` default, the cache key and what a tenant can see are the same.

- **Hosts that restart on a timer to get memory back can restart less often.** Before this release the telemetry agent of every run stayed in memory, about 7 KiB per run, and a periodic restart (or a memory limit that recycled the container) was the only thing that returned it. Measure the live heap over a day before you lengthen the interval. The telemetry itself is the same: spans, attributes and metrics are not renamed or reshaped.
  - **If you export `gen_ai.client.operation.duration` and `gen_ai.client.token.usage`, do not work around the old leak by disposing the agent.** With the OpenTelemetry SDK 1.20.1 a meter disposed after its run retired its series: each run became a series of its own that held one measurement, and a collection while idle returned none. Measured with a collection after every 20 runs: 20 series of count 1 for disposed meters, one series of count 200 for meters that stay alive. The pool therefore keeps its lanes while the host lives and disposes them when the host stops.
  - A stream your code never disposes (a hand-written `GetAsyncEnumerator` that is dropped without `DisposeAsync`) still holds the lane it took. Use `await foreach`, which disposes it.

- **On SQL Server, the names of definitions are compared by their characters, letter case included. `Support` and `support` are two agents, as they already
  were on PostgreSQL and SQLite.** SQL Server compared them by the collation of the database, and its default ignores case, so saving `support` after `Support`
  replaced the first row without a sign (every definition is written by an upsert), a read by either spelling returned the other's content, and the runs, the
  sessions and the statistics of the two agents were listed and counted as one. Migration `0060_ordinal_definition_names` gives `Latin1_General_BIN2` to the name of every
  definition (agents and their version counters, workflows and their versions, skills with their resources and scripts, eval suites, experiments, inbound triggers,
  schedules, MCP servers, webhook subscriptions) and to every column that repeats an agent, workflow, skill, script or tool name: runs, sessions, conversations, voice
  sessions, workflow requests, quotas and their usage, approval rules and pending approvals, tool invocations, run events, agent files, the target a job, a schedule or a
  trigger names, and the `entity` of an audit entry (`agent:Support`, which the audit list filters on). No value changes and no row is merged: names that were distinct stay distinct. Left alone: provider and tenant names (the library folds those to lower
  case before it stores them), handler and lane names, and the labels a span or a workflow node carries.
  What to do:
  - **Plan the upgrade window.** The migration is one command in one transaction and each table it rebuilds is closed to reads and writes until the whole
    migration commits. Measured in an emulated amd64 container on Apple Silicon (slower than native): 1.3 s over 100 000 runs (with 200 000 run events), and 5.1 s over
    a million runs with 2 million run events, half a million tool invocations and 100 000 sessions of 8 KB each (3.6 s of it the three indexes of `runs`). That is
    inside the default `CommandTimeoutSeconds` of 30; raise it for the run that applies the migration if your storage is slower, or apply it in your own deployment step
    (`AutoApplyMigrations = false`). Apply it before you start the new instances.
  - **A request that spells a name differently from the definition no longer finds it.** `/api/agents/support/run` for an agent saved as `Support` ran it on SQL Server and
    answers `404` now, as it does on the other stores. Call every definition by the spelling it was saved with.
  - **Check the SQL you wrote against these tables.** A comparison of one of these columns with a column of another collation fails with "Cannot resolve the collation
    conflict" (error 468). That includes a join of `runs_v1.agent_name` to a column of your own: write `COLLATE Latin1_General_BIN2` on your side to keep the match exact,
    or `COLLATE DATABASE_DEFAULT` on the view's side to ignore case again. The migration refreshes an existing `runs_v1`. A list ordered by a name now sorts by code point,
    capitals before lowercase letters.
  - SQL Server still ignores trailing spaces in every collation, so `Support` and `Support ` remain one name there.
  - **Drop what you built over these columns before the migration, and build it again after.** An index, a statistic, a schema-bound view, a foreign key or a computed column of your own over one of the columns above stops the migration (`ALTER COLUMN` refuses a column something depends on): the host does not start while `AutoApplyMigrations` is on. Tracon's own objects are rebuilt by the migration.

  A custom store must compare these names by their characters too. New contract tests, which a store that folds case fails:
  `AgentDefinitionStoreContract.Names_that_differ_only_in_case_are_two_definitions`, `WorkflowDefinitionStoreContract.Names_that_differ_only_in_case_are_two_definitions`,
  `AgentSkillStoreContract.Names_that_differ_only_in_case_are_two_skills`, `EvalStoreContract.Names_that_differ_only_in_case_are_two_suites`,
  `ExperimentStoreContract.Names_that_differ_only_in_case_are_two_experiments`, `InboundTriggerStoreContract.Names_that_differ_only_in_case_are_two_triggers`,
  `JobScheduleStoreContract.Names_that_differ_only_in_case_are_two_schedules`, `WebhookStoreContract.Names_that_differ_only_in_case_are_two_subscriptions`,
  `QuotaStoreContract.Agent_names_that_differ_only_in_case_are_two_quotas`, `ToolApprovalRuleStoreContract.Tool_names_that_differ_only_in_case_are_two_rules`,
  `SkillScriptGrantContract.Skill_names_that_differ_only_in_case_are_two_grants`, `McpServerStoreContract.Names_that_differ_only_in_case_are_two_servers`, `ToolInvocationContract.Tool_names_that_differ_only_in_case_are_two_tools_in_the_usage`,
  `RunStoreContract.Runs_of_agent_names_that_differ_only_in_case_are_listed_and_counted_apart` and
  `SessionStoreContract.Sessions_of_agent_names_that_differ_only_in_case_are_listed_apart`. `TenantIsolationContract` gains the protected helper
  `TenantIsolationContract.AssertNamesThatDifferOnlyInCaseAreSeparateRecordsAsync`, which they share.

  A store must also page runs that started at the same instant without repeating or skipping one (`RunStoreContract.Paging_through_runs_that_started_at_the_same_instant_lists_each_run_once`): break a tie on the timestamp with the run id.

- **A custom store must treat a tenant that differs only in letter case as the same tenant.** `Acme`, `acme` and `ACME` were already one tenant in the HTTP layer and in the
  SQL stores, but a store that compared the tenant text as written passed every contract. The contract suite now writes a record under one spelling and reads, lists and
  deletes it under another, both ways round, and a store that does not fold the tenant fails. The tenant is the one identifier that is folded: the names and keys a client
  chooses stay as written (see above). What to do: pass every tenant you write, query or delete with, from a parameter, from `ITenantContext` or from inside a record,
  through `AmbientTenantScope.Normalize`; store the folded value, compare it with a bare `=`, and return it in the records you read back. New contract tests, which a store
  that compares the tenant as written fails: `TenantIsolationContract.Tenant_identifiers_that_differ_only_in_letter_case_are_one_tenant` (every store contract derived from it),
  `SessionStoreContract.A_session_written_under_another_letter_case_is_read_back_with_the_canonical_tenant`,
  `RunStoreContract.A_run_started_under_another_letter_case_is_read_back_with_the_canonical_tenant`,
  `IdempotencyStoreContract.A_tenant_spelled_in_another_letter_case_reaches_the_same_reservation`,
  `RunInputStoreContract.A_tenant_spelled_in_another_letter_case_reads_its_own_input`,
  `WorkflowDefinitionStoreContract.A_definition_written_under_another_spelling_is_read_under_the_canonical_tenant`,
  `AgentDefinitionStoreContract.A_definition_saved_under_another_spelling_is_read_under_the_canonical_tenant`,
  `TenantEgressPolicyStoreContract.A_policy_saved_under_another_spelling_is_returned_and_read_under_the_canonical_tenant`,
  `AttachmentStoreContract.An_attachment_written_under_another_spelling_is_read_under_the_canonical_tenant` and
  `JobStoreContract.Lease_with_a_tenant_cap_counts_two_spellings_of_one_tenant_together`.

- **On SQL Server, the identity of a user, the name of an experiment variant and the actor of an audit entry are compared by their characters, letter case included. `Alice` and `alice` are two users, as they already were on PostgreSQL and SQLite.** SQL Server compared them by the collation of the database, and its default ignores case, so the session list of `bS7` returned the sessions of `Bs7` (state included), while a read of one session by id compared the owner exactly and refused it; the run list and the per-user breakdown merged two people, the arms `A` and `a` of an experiment were one arm, and the audit list filtered by one actor returned the entries of the other. Migration `0061_ordinal_identity_columns` gives `Latin1_General_BIN2` to `sessions.owner_id`, `runs.user_id`, `runs.variant` and `audit_log.actor`. No value changes and no row is merged: none of the four columns is part of a key (three indexes hold them, all filtered and not unique), so two spellings that were two rows stay two rows. Left alone: the owner of a singleton lease, which is the identity of a node that the library writes.
  What to do:
  - **Plan the upgrade window.** The migration is one command in one transaction, and the `runs`, `sessions` and `audit_log` tables are closed to reads and writes until it commits. Measured in an emulated amd64 container on Apple Silicon (slower than native), over rows that carry only their required columns: 0.7 s for 100 000 runs (with 10 000 sessions of 8 KB and 100 000 audit entries), 5.4 s for a million runs (with 100 000 sessions of 8 KB and a million audit entries). That is inside the default `CommandTimeoutSeconds` of 30; raise it for the run that applies the migration if your rows are wider or your storage slower, or apply it in your own deployment step (`AutoApplyMigrations = false`). Apply it before you start the new instances.
  - **Check the SQL you wrote against these columns.** A comparison of one of them with a column of another collation fails with "Cannot resolve the collation conflict" (error 468): write `COLLATE Latin1_General_BIN2` on your side to keep the match exact, or `COLLATE DATABASE_DEFAULT` on the column's side to ignore case again.
  - SQL Server still ignores trailing spaces in every collation, so `Alice` and `Alice ` remain one user there.
  - **Drop what you built over these columns before the migration, and build it again after.** An index, a statistic, a schema-bound view, a foreign key or a computed column of your own over one of the four columns stops the migration (`ALTER COLUMN` refuses a column something depends on): the host does not start while `AutoApplyMigrations` is on. Tracon's own indexes are rebuilt by the migration.

  A custom store must compare these identities by their characters too. New contract tests, which a store that folds case fails: `SessionStoreContract.Owners_that_differ_only_in_letter_case_are_two_owners`, `RunStoreContract.Users_that_differ_only_in_letter_case_are_two_users` and `RunStoreContract.Experiment_results_of_variants_that_differ_only_in_case_are_listed_apart`.

- **A tenant identifier holds only ASCII letters, digits, `_`, `.` and `-`; every other character is refused.** `AmbientTenantScope.Normalize`, `AmbientTenantScope.Begin`, the
  tenant an `ITenantContext` returns and every store now throw an `ArgumentException` (the message names the index and the code point of the first character, never the value)
  for a tenant with a space, an accented letter, a full-width letter, `*`, `:`, a control character or anything else outside that set. The HTTP surface already allowed
  nothing else in a header or a claim (`400` and `403`); the management routes that carry a tenant in the path (`/api/tenants/{tenantId}/…`, `/api/tenants/{slug}`) now answer
  `400` for it too, where a `GET` or a `DELETE` used to reach the store, and `POST /api/triggers/{tenantId}/{name}` answers the generic `401` of an unknown trigger. The reason is the
  database: SQL Server compares a tenant column by the collation of the database, which treats an accented letter written as one character and as two (`é` and `e` plus a
  combining accent) and a full-width letter and its plain one as equal, so two tenants were one on SQL Server and two on PostgreSQL, SQLite and in memory, and they shared
  idempotency keys, API keys and runs. Over this alphabet no collation joins two identifiers that differ after the lower-case fold (measured on SQL Server 2019 and 2022, with the
  default collation and with `Turkish_CI_AS`), so the tenant columns stay as they are.
  What to do:
  - **A custom `ITenantContext` returns an identifier of that alphabet.** One that returns the text of a header, a claim or a subdomain as it came fails on the first store call
    with a `500` for a value outside it. Check the text with the new `AmbientTenantScope.TryNormalize(string?, out string?)` (it applies the same rule without throwing) and answer the
    client yourself with `400`. `samples/Tracon.Embedded` shows it.
  - **`TraconOptions.DefaultTenantId` and `TraconTenancyOptions.AllowedTenants` are checked at startup.** An entry outside the alphabet stops the host with an options validation failure.
  - **Rows an earlier release wrote under such a tenant stop the migration runner.** On every start the runner now names the tables that hold a `tenant_id` outside lower-case
    `a-z`, `0-9`, `_`, `.`, `-`, as it already did for capitals; it changes nothing. `*` in `retention_policies` is the default policy of every tenant and is not a finding. Rename the
    tenant, per table, to its ASCII spelling, except in `audit_log`: the tenant of an entry is part of its hash, so a renamed entry fails the chain verification
    (`GET /api/audit/verify` reports it as broken). For that table export the entries of the tenant, then either remove all of them (a chain belongs to one tenant, so the chains of
    the others stay whole) or rename them and accept that the chain of that tenant fails from the first renamed entry; the runner's message says the same. With `AutoApplyMigrations`
    off the runner does not run, so look before you upgrade: a retention run that reads the tenant of such a row (attachments, sessions when a content eraser is registered, and the
    targets whose key holds the tenant) fails on it and records the failure, and it fails again on every run until the row is dealt with (repeat for every table the first query lists):

    ```sql
    -- PostgreSQL
    SELECT table_name FROM information_schema.columns WHERE table_schema = 'tracon' AND column_name = 'tenant_id';
    SELECT count(*) FROM tracon.runs WHERE tenant_id COLLATE "C" ~ '[^a-z0-9_.-]';
    -- SQL Server
    SELECT t.name FROM sys.columns c JOIN sys.tables t ON t.object_id = c.object_id WHERE SCHEMA_NAME(t.schema_id) = N'tracon' AND c.name = N'tenant_id';
    SELECT COUNT(*) FROM tracon.runs WHERE tenant_id COLLATE Latin1_General_BIN2 LIKE N'%[^-a-z0-9_.]%';
    -- SQLite (the tables carry the prefix, `tracon_` by default)
    SELECT m.name FROM sqlite_master m JOIN pragma_table_info(m.name) p WHERE m.type = 'table' AND p.name = 'tenant_id';
    SELECT count(*) FROM tracon_runs WHERE tenant_id GLOB '*[^a-z0-9_.-]*';
    ```
  - **A custom store gets one more case.** `TenantIsolationContract.Tenant_identifiers_outside_the_allowed_characters_are_refused` (every store contract derived from it) hands the store
    such a tenant and expects an `ArgumentException` on a write and on a read, with nothing written. Passing the tenant through `AmbientTenantScope.Normalize`, as above, is enough.

- **Behavioural breaking change: a JSON request body longer than 8 MiB answers `413`.** Nothing bounded a body before: a 32 MB `message` was read,
  parsed, recorded with its run and echoed, about thirty times its size in memory at once, and only the server's own limit (30 MB on Kestrel,
  written nowhere) stood in front of it. `TraconEndpointOptions.MaxJsonBodyBytes` now bounds every JSON body under the endpoints, 8 MiB by default,
  counted as the bytes arrive so a chunked body, or one whose length lies, stops at it too. The OpenAI-compatible routes answer in their own error
  shape. A file upload keeps its own limit and the MCP, A2A and AG-UI endpoints are not under it. A client that sends a larger JSON body (the
  definition import takes up to 8 MiB by its own limit and is unaffected at the default) raises the option; set a reverse proxy's limit no higher.

- **Behavioural breaking change: `RequireProductionProfile()` asks one more decision, `UnenforcedQuotaOnStoreFailure`.** With quota enforcement on and
  `Tracon:Quotas:AllowOnStoreFailure` at its default of `true`, a run goes ahead when the quota store fails, so a tenant with a spending
  ceiling goes past it for as long as the database is down. That default is deliberate (an outage that stops every run is its own failure), but it was the
  one fail-open default the profile did not name. A host that calls `RequireProductionProfile()` stops at start until it sets
  `Tracon:Quotas:AllowOnStoreFailure` to `false` or accepts the risk by name with `profile.Accept(TraconProductionRisk.UnenforcedQuotaOnStoreFailure)`.
  A host with `Tracon:Quotas:Enabled` off is not asked, and a host that does not call `RequireProductionProfile()` changes nothing.

- **Behavioural breaking change: `RequireProductionProfile()` asks one more decision, `UnauthenticatedLocalOperator`.** A host that calls it and maps the
  endpoints with no `AuthToken` and no `RequireAuthorization(...)` policy stops at start until it sets one or accepts the risk by name. With neither, a request
  that presents no credential is the local operator with full rights. That is right for a caller on the same machine; a reverse proxy that listens on a
  Unix socket and adds no forwarding header makes every remote caller look local, because a connection of that kind has no remote address, and
  nginx in its default form is such a proxy. An API key does not answer the decision: a request without one is still admitted. A host that does not call
  `RequireProductionProfile()` changes nothing. Answer it with `TraconEndpointOptions.AuthToken` or a policy, or `profile.Accept(TraconProductionRisk.UnauthenticatedLocalOperator)`
  when nothing but the machine can reach the endpoints.

- **Behavioural breaking change: `MapTracon` throws on anything that is not the application, and a time span in configuration must be a clock.**
  - `MapTracon` on `app.MapGroup(...)` (or inside `UseEndpoints(...)`) used to succeed and leave the surface half built: the request body limit, the `Idempotency-Key`
    buffering, the CORS answer, the mapping of a malformed body and of an unreachable store to their problem replies and the WebSocket middleware are added to the
    application's pipeline, which a group does not have, so a request with an `Idempotency-Key` failed with `400 Invalid request body` and a body past
    `MaxJsonBodyBytes` was read. It throws an `ArgumentException` now, naming the fix: call it on the application and pass the path as the `prefix` argument
    (`app.MapTracon("/ops/tracon")`). `MapTraconMcpServer`, `MapTraconA2A` and `MapTraconAGUI` on a group say that they need the application, where they used to ask
    for `MapTracon` that had already run.
  - A time span key (37 options, such as `Tracon:Tools:DefaultTimeout`) is read as `[days.]hh:mm:ss`, with an optional fraction and sign: `00:00:30`, `1.00:00:00`,
    `00:00:00.250`. A bare number and an hour above 23 are refused at start and the message names the key. `TimeSpan.TryParse` read `30` as thirty days and `48:00:00`
    as forty-eight, so a `Tools:DefaultTimeout=30` meant to be seconds was a tool call that could run for a month, and `00:30` was thirty minutes. A key that already
    holds a clock (`00:00:30`, `0:00:30`, `2.12:00:00`) reads as before. A time span in a request body is read the same way, as the schema
    always said (`^-?(\d+\.)?\d{2}:\d{2}:\d{2}(\.\d{1,7})?$`): `"timeout": "30"` on a workflow node and `"runTimeout": "00:30"` are a `400` that names the field,
    where `System.Text.Json` read them as thirty days and thirty minutes (it already refused `"48:00:00"`, now with a message that says what is expected). A value the framework wrote (`00:00:30`, `1.00:00:00`, `49.17:02:47.2950000`) reads
    as before, so an exported definition imports unchanged. A custom `JsonConverter<TimeSpan>` registered through `ConfigureHttpJsonOptions` still wins.

- **Behavioural breaking change: an MCP server's response over 16 MiB fails the call, and its resource and prompt lists are cut.** The SDK reads a response to its end and builds the
  whole message from it: a 20 MB tool result cost about 500 MB of memory before `DefaultMaxOutputBytes` could trim it, and a server nobody controls can send more.
  `Tracon:Mcp:MaxResponseBytes` (16 MiB) now ends a response that passes it with an error that names the option; a body that declares a larger length is refused without being read,
  and a `text/event-stream` response is counted per event, so the stream a client keeps open for notifications has no limit on its total. It applies when a connection is
  opened. Raise it if a server returns a larger answer on purpose. `Tracon:Mcp:MaxResourcesPerServer` (1000) and `Tracon:Mcp:MaxPromptsPerServer` (100) keep the first resources
  and prompts a server lists, as `MaxToolsPerServer` keeps tools; a resource past the limit is not declared, so reading it answers "not declared". A server with more than 1000
  resources raises the first option.

- **Behavioural breaking change: a tool result over 1 MiB is trimmed unless you say otherwise.** `Tracon:Tools:DefaultMaxOutputBytes` defaulted to unlimited so that an
  upgrade never cut output, but a result reaches the model request, the run's tool-call record and the session state whole: a 20 MB result from an MCP server cost about
  1.6 GB of memory. The default is 1 048 576 bytes now. A longer result is replaced by the same JSON envelope any over-limit result gets (`{"truncated": true,
  "omittedBytes": …, "content": …}`), a `ToolOutputTruncated` event is written, and a tool's own `MaxOutputBytes` still wins. A tool that must return more raises the value,
  or sets `DefaultMaxOutputBytes = null` in code for the old behaviour (a configuration key cannot be `null`: write a large number there). The MCP client still reads a
  server's whole answer before the limit trims it.

- **Behavioural breaking change: an agent that carries more than 128 tools does not compile.** Every tool's definition goes into every model request: 5 000 tools put about
  690 KB of definitions into each one, and OpenAI refuses a request with more than 128. `Tracon:Tools:MaxToolsPerAgent` (128) bounds what the model would be sent (the
  tools the definition selects, the vector search tool and the sub-agent tools); an agent over it fails with an error that names the count and the option, and a run of it
  answers `400`. Raise the option for a provider that accepts more. The value must be at least 1.

- **Behavioural breaking change: `PUT /api/workflows/{name}` refuses a sequential chain that no run could finish.** A chain takes one super-step for each of its steps
  (one more when it is written as `agentNames`), and `Tracon:Workflows:MaxSuperSteps` is 100. A chain of 2 000 steps was saved with a `200` and every run of it ended
  `Failed` at the limit after 11 to 23 seconds; it is a `400` that names the longest chain that fits now. The rollback of a workflow and the definition import apply the same
  rule. A workflow written in code is not checked when it is registered. Shorten the chain, split it into two workflows, or raise `MaxSuperSteps`. The other patterns are not
  counted: they reach the limit through the conversation they hold.

- **Four migrations changed in place, and a database that applied one of them before must be recreated.** `lower()` follows the locale
  of the database, and in a Turkish one `lower('OpenAI')` ends in a dotless i (U+0131) instead of the i of `openai`. PostgreSQL `0038` and `0062` and SQL Server `0025`
  and `0050` folded names with it: on such a database `0038` and `0025` rewrote a tenant's provider binding to a name no agent
  definition says (the tenant's own key then went unused without an error) and `0062` did not find a header written as `AUTHORIZATION`.
  They now fold with the ASCII rules (`COLLATE "C"`, `COLLATE Latin1_General_BIN2`), whatever the database. A migration changes the
  checksum the database recorded, so a database that applied one before fails the check at start. This is a release candidate
  cost taken before `1.0.0-rc.1`, as `0059` was; a database created by this release is not affected. A new test reads every
  migration and refuses a `lower()` or `upper()` without an ordinal collation.

- **On SQL Server, the identifiers a client chooses are compared by their characters, letter case included. `Session-A` and `session-a` are two sessions,
  as they already were on PostgreSQL and SQLite.** SQL Server compared them by the collation of the database, and its default ignores
  case, so a second spelling replaced the first one's row without a sign (an upsert) and a read by either spelling returned the
  other's content. Migration `0059_ordinal_key_collation` gives `Latin1_General_BIN2` to the session id (`sessions.id` and the
  `session_id` of runs, attachments, pending approvals, responses, voice sessions and workflow checkpoints, requests and responses),
  the idempotency key, the agent file path, the workflow checkpoint (its `checkpoint_id` and `parent_id`), request and claim ids
  (including the request id of a pending approval), a run score's author, message id and name, and the rate-limit partition key. No value
  changes and no row is merged: ids that were distinct stay distinct. SQL Server still ignores trailing spaces in every collation, so
  `abc` and `abc ` remain one key there while the other stores keep them apart; to make the stores agree on the ids that open a session, the
  run endpoint, `/v1/responses`, the AG-UI endpoint and the branch endpoint (`newSessionId`) answer `400` to an id that starts or ends with
  whitespace. An id that only labels a row (an attachment's `sessionId`, for one) is not checked.
  What to do:
  - **Plan the upgrade window.** The migration is one command in one transaction that rebuilds the keys of 13 tables. While it
    runs those tables are closed to reads and writes, and they stay closed until the whole migration commits. Measured in an emulated
    amd64 container on Apple Silicon, so slower than native (before `workflow_checkpoints.parent_id` and `pending_approvals.request_id` joined the
    migration; both tables hold small rows and were not measured again): 14 s over 2.5 million rows (1.2 GB), 32 s over a database of 150 000
    sessions with 8 KB of state each and a million runs (2 GB). `CommandTimeoutSeconds` defaults to 30 and bounds that one command: a
    timeout rolls the migration back, and every start repeats it. Raise `Tracon:SqlServer:CommandTimeoutSeconds` (`0` is unlimited)
    for the run that applies it, or apply it in your own deployment step (`AutoApplyMigrations = false`), and do it in a quiet hour.
    Apply it before you start the new instances; an instance of the older release works on the migrated schema once it commits.
  - **Check the SQL you wrote against these tables.** A comparison of one of these columns with a column of another collation
    fails with "Cannot resolve the collation conflict" (error 468). That includes a join of `runs_v1.session_id` to a column of your
    own: write `COLLATE Latin1_General_BIN2` on your side to keep the match exact, or `COLLATE DATABASE_DEFAULT` on the view's side to
    ignore case again. The migration refreshes an existing `runs_v1`, so that its column reports the new collation.
  - **Write each identifier with one spelling** if your code, a client or a stored reference spelled it two ways and relied on SQL
    Server treating them alike: it now reaches two records. A list ordered by an agent file path or a score name now sorts by
    code point, capitals before lowercase letters.
  - The names of definitions (agents, workflows, skills, schedules and the like) kept the collation of the database in this change; the entry above
    ("the names of definitions are compared by their characters") moves them too.

  A custom store must compare these identifiers by their characters too. New contract tests, which a store that folds case fails:
  `SessionStoreContract.Ids_that_differ_only_in_case_are_two_sessions`,
  `IdempotencyStoreContract.Keys_that_differ_only_in_case_are_two_reservations`,
  `AgentFileStoreContract.Paths_that_differ_only_in_case_are_two_files`,
  `WorkflowCheckpointStoreContract.Session_and_checkpoint_ids_that_differ_only_in_case_name_other_checkpoints`,
  `RunScoreStoreContract.Author_message_and_name_that_differ_only_in_case_open_rows_of_their_own`,
  `RunStoreContract.Runs_of_session_ids_that_differ_only_in_case_are_listed_apart` and
  `RateLimitCounterStoreContract.Partitions_that_differ_only_in_case_are_counted_apart`.

- **The voice conversation socket asks an API key for the `RunsWrite` scope, and a socket that your authorization policy
  authenticates opens only from the installation's own origin or from `AllowedOrigins`.** The socket checked a key by hand
  and nothing else, so a key that could only read agents (`AgentsRead`) opened a conversation that runs them, although the
  endpoint that opens a live voice session asks for `RunsWrite`. A key without it now gets `403` before the upgrade; give it
  the scope. Separately, a browser sends its cookie on a WebSocket from any page and a WebSocket is not bound by CORS, so
  when `RequireAuthorization` authenticated the socket with a cookie, a page of another origin could open a conversation as
  the signed-in user and read every frame. The handshake now answers `403` for an `Origin` that is neither the request's own
  nor one of `AllowedOrigins`. A client that sends no `Origin` header (a server, a mobile app) and a socket that presents its
  own token or key in the subprotocol are not judged by origin.

- **A cron expression written as `N/step` counts from `N` to the end of the field, and one that can never match is refused when
  it is saved.** `5/15` in the minute field meant only minute 5 (once an hour); it now means `5-59/15`, minutes 5, 20, 35 and
  50, as Cronos reads it, so a schedule written that way runs more often than before. `0 0 30 2 *` (the 30th of
  February) was accepted and then failed when the next run was computed; it is now a `FormatException` at parse time, and a
  day and month combination that does occur, such as `0 0 29 2 *`, is found even when it is years away (the search looks eight
  years ahead). On the night a clock goes back, a job that runs often (`*/5 * * * *`, or a step in the hour field) runs through
  both passes of the repeated hour; before, it fell silent for that hour and five minutes. A job that names its hour
  (`30 2 * * *`) still runs once, in the first pass.

- **A job's lease is fenced. `IJobStore.RenewLeaseAsync`, `MarkRunningAsync`, `CompleteAsync` and `ReleaseForRetryAsync` take a `JobLease` (job, owner, attempt) and return `bool`, and `JobCompletion.JobId` is removed.** A worker that stalled past its lease (a long pause, a frozen container, a network cut) could come back after another worker had leased the job and still complete it or return it to the queue: the store checked neither the owner nor the attempt on those writes, and the worker never learned that a renewal had been ignored. The job then read `Completed` while the new holder was still running it, or went back to `Pending` and was leased a third time. Each of the four writes is now one conditional statement. It applies only while the job's lease is the one the worker took (same owner **and** same attempt, status `Leased` or `Running`) and otherwise answers `false` and changes nothing. The attempt is part of the key because a worker keeps its owner identity when it leases a job again. The built-in worker cancels the handler's token when a renewal is refused, or when renewals keep failing until it can no longer prove the lease (a third of the lease before it runs out). It then writes nothing for that attempt, counts no job and publishes no `job.completed` or `job.failed`: the worker that holds the job does. `IJobHandler`'s token is cancelled for four reasons now, and a queued workflow run whose job lost its lease keeps its checkpoints for the worker that took the job over. A cancelled job refuses a renewal too and is not treated as a lost lease. Fencing keeps the job's record right; it does not undo what the stalled handler already did elsewhere, so execution stays at-least-once. To upgrade a custom `IJobStore`, take the `JobLease`, check it in the same atomic step as the write, and return `false` when the write did not apply; `CompleteAsync` and `ReleaseForRetryAsync` also answer `false` for a cancelled job. `JobStoreContract` changes the calls it makes to take a `JobLease` and gains `JobStoreContract.A_worker_that_lost_its_lease_cannot_close_the_new_owners_attempt`, `JobStoreContract.A_stale_attempt_of_the_same_owner_cannot_complete_the_new_attempt`, `JobStoreContract.A_worker_that_lost_its_lease_cannot_return_the_job_to_the_queue`, `JobStoreContract.A_released_attempt_cannot_touch_the_attempt_that_replaced_it`, `JobStoreContract.Renewing_a_lease_extends_it_only_for_the_attempt_that_holds_it`, `JobStoreContract.Renewing_the_lease_of_a_cancelled_or_finished_job_returns_false`, `JobStoreContract.Completing_or_releasing_with_another_workers_lease_changes_nothing` and `JobStoreContract.Only_one_of_two_concurrent_completions_of_the_same_lease_is_applied`. The first two fail a store that checks the owner alone, or the attempt alone; a store that reads the lease and writes in a second step passes them, and only the last one can catch it, when its two calls overlap.

- **Four more contract tests catch a store that loses a concurrent update.** `ApiKeyStoreContract.A_revoked_key_stays_revoked_while_its_last_use_is_being_recorded` (a last-use stamp must not bring a revoked key back), `RetentionPolicyStoreContract.Concurrent_progress_appends_all_count` (concurrent progress appends are all counted), `RunStoreContract.A_completed_run_stays_completed_while_its_cost_is_being_updated` (a cost update must not make a finished run read as running again) and `WebhookStoreContract.Failure_counter_counts_every_concurrent_failure` (the failure counter counts every overlapping failure). A custom store passes them when each of those updates is one atomic step rather than a read followed by a write. The shipped in-memory stores were changed to meet them.

- **A conversation identifier is local to the calling tenant. `GET /v1/conversations/{id}`, `GET …/items`, `DELETE …/{id}` and
  `POST /v1/responses` (`conversation`, `previous_response_id`) no longer answer `404` for an identifier that only another
  tenant holds, and `ISessionStore.GetOwnerTenantIdAsync` is removed.** Two tenants can hold the same session identifier
  (a session is keyed by tenant and identifier), but these routes looked the identifier up across every tenant and took
  the first row's tenant as its owner. A tenant could get `404` for its own conversation, and the `404` told a caller that
  another tenant used the identifier. The routes now ask the calling tenant only, and an identifier it does not hold is an
  unused reservation: `GET` answers `200`, the item list is empty, `DELETE` answers `200` with `deleted: false`, and
  `POST /v1/responses` opens a session under it in the caller's own tenant without touching the other tenant's. `404`
  remains for a refusal by `IRunAuthorizationHandler` or by session ownership, and for a custom `ISessionStore` that returns
  another tenant's record. Your `IRunAuthorizationHandler` is now also asked about an identifier that only another tenant
  holds, as it is for any identifier the caller's tenant has not used. The voice endpoints already worked this way. Remove the method from your own `ISessionStore`;
  `SessionStoreContract` drops `GetOwnerTenantIdAsync_returns_null_for_a_never_used_id` and
  `GetOwnerTenantIdAsync_returns_the_real_owner_independent_of_the_ambient_tenant`, and gains
  `SessionStoreContract.Two_tenants_may_hold_the_same_id_and_each_reads_only_its_own`, which a store keyed by the identifier alone fails.

- **A database that applied any earlier preview migration refuses to start: the checksum of 177 migration files changed. No SQL statement
  changed; only comments did.** `MigrationRunner` compares the checksum it recorded for each applied migration with the file's
  checksum, and the comments inside the files carried references that mean nothing outside the repository, so they were
  removed. The failure reads "Migration '…' has been applied to the database but the file's content has changed". The
  preview line has no upgrade path for stored data between previews: recreate the database, or write the new checksum of each
  changed migration into its migration ledger row. From the first release candidate on an applied migration is never edited
  again.

- **`RequireProductionProfile()` asks five new decisions, but only of a host that declares more than one instance with
  `Tracon:Deployment:Instances`. At the default of `1` a host that calls it starts exactly as before.** `TraconProductionRisk`
  gains `InstanceLocalState`, `UnreconciledRuns`, `UnelectedBackgroundWork`, `InstanceLocalRateLimit` and
  `InstanceLocalMcpTokens`, each with its own `Accept` line. Why they were added: Tracon now supports two instances on one
  PostgreSQL or SQL Server database, and the settings that make two instances work are all off by default, so a deployment
  that runs two and forgets one gets no error. With the store in memory or in a SQLite file each instance sees its own runs;
  with run reconciliation off a cancel that reaches the other instance answers `409`; with single-executor election off
  every instance runs the periodic background work; with the rate-limit scope at `Instance` the deployment admits the limit
  once per instance (the inbound trigger limit included, which is always on, so the decision is asked while `Tracon:RateLimit:Enabled` is off too); with the MCP token store in `Memory` an authorization one instance started cannot finish on the other.
  Answer each with the setting the failure message names, or accept it with `profile.Accept(TraconProductionRisk.X)`;
  `InstanceLocalMcpTokens` is accepted when no MCP server uses OAuth. Tracon cannot count replicas, so a deployment that
  runs two instances and does not declare them is not asked. New key: `Tracon:Deployment:Instances` (default `1`, at least
  `1`). New public types: `TraconDeploymentOptions`, `TraconOptions.Deployment`. The guide *Run more than one instance*
  lists what two instances share and what stays per instance.

- **MCP discovery runs on every instance, also with `Tracon:SingletonExecution:Enabled` on.** It ran under the
  single-executor lease, so only the instance that held the lease refreshed its tool catalog and the other instance had no
  MCP tool at all (two instances on one database: 1 tool on the lease holder, 0 on the other, with or without OAuth on the
  server). The catalog is a cache of the process and holds the live connection a tool call goes through, so every instance
  needs its own. Each instance now opens one connection to each server and lists its tools once for each
  `Tracon:Mcp:RefreshInterval`. With one instance, or with singleton execution off, nothing changes. The lease named
  `mcp-discovery` is no longer taken; a row with that name in `singleton_leases` is harmless and expires.

- **`RequireProductionProfile()` asks a new decision: `UnkeyedAuditChain`. A host that calls it and keeps its audit
  trail in a database does not start until the decision is answered or accepted by name.** `TraconProductionRisk` gains
  `UnkeyedAuditChain`. A host that never calls the method sees nothing change. Why it was added: the audit trail's
  hash was a plain SHA-256 of a public form, so anyone who could write to the audit table could change an entry,
  recompute every hash after it and still get `Valid` from `GET /api/audit/verify`. Answer it by putting a random key of at
  least 32 bytes, base64, under `Tracon:AuditChainKeys:<id>` and setting `Tracon:Audit:ChainKeyId` to `<id>`; or accept it
  with `profile.Accept(TraconProductionRisk.UnkeyedAuditChain)`. It is not asked while the audit log is Tracon's in-memory
  one. The failure message names the two settings and carries no value.
- **A name that ends in a line feed is no longer a valid name.** Seven validators wrote their pattern as `^…$`, and in .NET
  `$` also matches before a trailing `\n`, so `"acme\n"` passed. They now end the match at the end of the text: the tenant
  id (`HttpTenantContext.IsValidTenantId`), a custom run event type, a job handler key, a job lane, a knowledge collection
  name, an MCP server or tool name and an agent parameter name (`POST /api/agents/validate` and a save now report a
  parameter name that ends in a line feed as "not a valid identifier"; rename it). A tenant claim that ends in a line feed now answers `403` (`Tenant claim
  malformed`), and a route tenant that ends in one answers `400`. A tenant saved earlier under such a name (only a
  claim or a route segment could carry the character) is unreachable by that name: its rows are still in the database.
  Rename them or save the tenant again under the name without the line feed. `AnchoredPatternTests` fails when a new
  whole-value pattern ends in `$`, whether it is written as `[GeneratedRegex]`, `new Regex("…")` or a static `Regex` call.
- **Migrations for the audit chain key.** PostgreSQL `0067`, SQL Server `0055` and SQLite `0054`. With `AutoApplyMigrations`
  (the default) they run at startup. Each adds one nullable column, `audit_log.key_id`. No row is backfilled and none is
  re-signed: an entry written before the upgrade keeps `NULL` and its plain SHA-256, which the walk still checks.
- **Public types changed.** `AuditEntry` gains `KeyId`, `AuditChainVerification` gains `UnkeyedEntries`, `AuditChainStatus`
  gains `UnknownKey = 3` and `TraconAuditOptions` gains `ChainKeyId`. Code that switches over the three
  `AuditChainStatus` values (a generated client included) must handle `UnknownKey`. A custom `IAuditLog` that computes its own chain
  is not asked to sign. The shared `AuditLogContract` gains the signed-chain cases and two optional hooks,
  `CreateKeyedLogAsync` and `KeyedLogsShareStorage`; an implementation that does not override them skips those cases.
  The new cases: `AuditLogContract.A_chain_signed_by_two_keys_verifies_after_rotation`,
  `AuditLogContract.A_keyed_chain_verifies_as_valid`,
  `AuditLogContract.Concurrent_keyed_writes_for_one_tenant_produce_a_single_valid_chain`,
  `AuditLogContract.Downgrading_a_keyed_entry_is_detected_as_broken`,
  `AuditLogContract.Re_signing_under_another_key_id_is_detected_as_broken`,
  `AuditLogContract.Recomputing_a_keyed_row_without_the_key_is_detected_as_broken`,
  `AuditLogContract.Tampering_a_keyed_row_directly_is_detected_as_broken`,
  `AuditLogContract.The_write_path_sets_the_key_id_and_ignores_a_caller_supplied_one`,
  `AuditLogContract.Turning_the_key_off_keeps_the_entry_and_the_walk_reports_the_downgrade`,
  `AuditLogContract.Two_tenants_keyed_chains_verify_independently`,
  `AuditLogContract.Two_writers_with_a_mixed_key_configuration_leave_the_chain_broken_at_the_unsigned_entry_for_good`
  and `AuditLogContract.Unkeyed_history_before_the_first_keyed_entry_stays_valid_and_is_counted`.

- **A cancel request reaches a run whichever instance takes it. A custom `IRunStore` must implement one new member and one changed signature.**
  `IRunStore.TouchHeartbeatAsync` now returns `ValueTask<IReadOnlyList<RunCancellationRequest>>` instead of `ValueTask`, and
  `IRunStore.RequestCancellationAsync(Guid runId, string tenantId, DateTimeOffset at, CancellationToken)` is new. A store
  that compiled before fails to build (`CS0535`, `CS0738`) until it has both; return an empty list from `TouchHeartbeatAsync`
  and `false` from `RequestCancellationAsync` to keep the old behavior, or implement them as the `RunStoreContract` cases
  describe. `RunStoreContract` gains eleven cases for the two members. New public types: `RunCancellationRequest`, and
  `RunRecord.CancelRequestedAt` (also `cancelRequestedAt` in `GET /api/runs/{id}` and the generated clients).
  The eleven cases: `RunStoreContract.A_request_written_while_Queued_survives_the_upsert_to_Running`,
  `RunStoreContract.ClaimOrphanedRunsAsync_returns_the_cancellation_request_of_the_run_it_closes`,
  `RunStoreContract.RequestCancellationAsync_ignores_another_tenants_run`,
  `RunStoreContract.RequestCancellationAsync_is_false_for_a_terminal_run_and_leaves_the_row_alone`,
  `RunStoreContract.RequestCancellationAsync_is_false_for_an_unknown_run`,
  `RunStoreContract.RequestCancellationAsync_keeps_the_first_time_and_stays_true`,
  `RunStoreContract.RequestCancellationAsync_records_the_request_and_the_next_heartbeat_returns_it_with_the_tenant`,
  `RunStoreContract.RequestCancellationAsync_uses_the_expected_tenant_and_not_the_ambient_one`,
  `RunStoreContract.RequestCancellationAsync_works_on_a_Queued_run`,
  `RunStoreContract.TouchHeartbeatAsync_returns_an_empty_list_when_no_request_was_written` and
  `RunStoreContract.TouchHeartbeatAsync_returns_only_requested_active_rows_with_their_tenant`.
- **`POST /api/runs/{runId}/cancel` answers `202` where it answered `409` for a run another instance executes, when
  `Tracon:RunReconciliation:Enabled` is `true`.** The receiving instance writes the request onto the run's row
  (`runs.cancel_requested_at`) and the owner reads it on its next heartbeat, so the run ends within one
  `RunReconciliation:HeartbeatInterval` (30 seconds by default). With reconciliation off the call still returns `409`, and its
  message now names the setting. A run that belongs to a queued job needs neither: the endpoint cancels the job and the worker
  stops the handler within `Scheduling:PollInterval`. A run whose owner stopped before it read the request is closed `Failed`
  (`orphaned`) by reconciliation and is not continued.
- **A queued run cancelled while it runs now leaves its job `Cancelled`.** The endpoint stopped the handler first and
  cancelled the job second, so the worker took the stop for a failure: the job showed `Failed` and, with
  `AsyncRun:MaxAttempts` above `1`, it was leased again and the run, closed `Canceled`, went back to `Running` and called the
  model a second time. The endpoint now cancels the job first. A job-monitoring script that matched a cancelled run's job on
  `Failed` (queued agent runs showed `TaskCanceledException failed.`, queued workflow runs `tracon.workflow-run.failed`) must
  match `Cancelled`; the `tracon.job.executions` metric counts it under `Cancelled`.
- **Migrations for the cancel request.** PostgreSQL `0069`, SQL Server `0057` and SQLite `0056`. With `AutoApplyMigrations`
  (the default) they run at startup. Each adds one nullable column, `runs.cancel_requested_at`. No row is backfilled and no
  index is added.

- **Migrations for the rate-limit counter.** PostgreSQL `0070`, SQL Server `0058` and SQLite `0057`. With
  `AutoApplyMigrations` (the default) they run at startup. Each creates one table, `rate_limit_counters`
  (`partition_key`, `window_start`, `permit_count`), with no tenant column and no index beyond the primary key. The table
  stays empty and is never written while `Tracon:RateLimit:Scope` is `Instance`, which is the default.
- **New public types for the rate-limit counter.** `IRateLimitCounterStore`, `RateLimitWindowCount` and `RateLimitScope` in
  `Tracon.Abstractions`; `TraconRateLimitOptions.Scope`; `RateLimitCounterStoreContract` in
  `Tracon.Testing.Contracts.Xunit`; `TraconDiagnostics.RateLimitStoreFailureCounterName` and `TraconDiagnostics.Tags.Limiter`.
  A host that registers nothing keeps the in-memory counter, and the setting that uses it is off by default, so nothing
  changes for it.
  A counter store of your own passes the contract's cases:
  `RateLimitCounterStoreContract.A_later_window_restarts_the_count`,
  `RateLimitCounterStoreContract.A_partition_key_is_compared_as_a_whole`,
  `RateLimitCounterStoreContract.A_slow_clock_does_not_move_the_stored_window_back`,
  `RateLimitCounterStoreContract.An_earlier_window_is_counted_in_the_stored_window`,
  `RateLimitCounterStoreContract.Concurrent_increments_are_all_counted`,
  `RateLimitCounterStoreContract.Concurrent_increments_that_open_a_new_window_restart_the_count_once`,
  `RateLimitCounterStoreContract.Increments_in_the_same_window_count_up`,
  `RateLimitCounterStoreContract.Partitions_are_counted_apart` and
  `RateLimitCounterStoreContract.The_first_increment_of_a_partition_counts_one_in_the_given_window`.

- **The `Tracon` package no longer brings PostgreSQL.** It now carries the HTTP API, workflows, MCP, the OpenAI
  provider, and the console, and no storage engine, so a host on SQL Server, SQLite, or the in-memory stores no longer
  takes Npgsql. A host that calls `UsePostgreSql` and references only `Tracon` fails to build (`CS1061`: `ITraconBuilder`
  does not contain a definition for `UsePostgreSql`). Add the package with
  `dotnet add package Tracon.PostgreSql --prerelease`. A project generated with `--persistence postgres` from the new
  template already references it.
- **A project generated from the new template takes the model name from `appsettings.json`, not from `Program.cs`.**
  The generated `Program.cs` no longer carries the `WRITE_MODEL_NAME_HERE` placeholder. It reads
  `Tracon:Providers:<Provider>:DefaultModel` (Azure OpenAI: `DefaultDeployment`) and binds the value to the sample agent.
  A project generated earlier keeps its own `Program.cs` and is not affected. For a new project, pass the model to
  `dotnet new tracon-api --model <name>`, or fill the setting in. With an API key set and no model name the app stops
  at startup and names the setting; with no key it still starts, as before.
- **An agent that sets `Temperature`, `TopP` or `TopK` for the `anthropic` provider no longer builds.** Anthropic SDK 13
  removed the three fields from the Messages request, so the SDK adapter drops them: a definition that set one ran
  without it. Tracon refuses to drop a field without saying so. `POST /api/agents/validate` now reports
  `sampling_field_unsupported` at `model.temperature`, `model.topP` or `model.topK`, a save returns `400`, and a run of
  a stored agent fails with the compile error. The same holds when Anthropic is only a `Fallbacks` link, and for the
  judge model of `AddModelRunJudge`: its first judgment fails with an error that names the field. Clear the
  field. Providers that already sent it (`openai`, `openai-responses`, OpenAI-compatible servers, `azure-openai`,
  `google`) are not affected.
- **A custom `ISamplingSupport` must now name `Temperature` and `TopP`.** `SamplingFields` gains the two members. A
  provider that implements `ISamplingSupport` and reports neither is read as not sending them, and an agent that sets one
  for it no longer builds. Add `SamplingFields.Temperature | SamplingFields.TopP` to `SupportedSamplingFields` if your
  provider sends them. A provider that does not implement `ISamplingSupport` is read as sending both, as before.
- **The Anthropic requests carry a bearer token.** SDK 13 sends the API key as `Authorization: Bearer <key>` with
  `anthropic-version: 2026-10-21` where SDK 12 sent `x-api-key` with `2023-06-01`; the health check
  (`GET /api/models/health/anthropic`) sends the same. The live API accepts both forms with an API key (measured). If an
  `Endpoint` points at a gateway that reads only `x-api-key`, make it accept the bearer form or map the header.
- **Microsoft Agent Framework 1.26.** A host that references `Microsoft.Agents.AI` 1.26 now runs Tracon: 1.26 added an
  abstract `AgentSessionStore.DeleteSessionAsync`, and the `Tracon.AspNetCore` 1.0.0-preview.6 binary can fail to load its
  session store against it (`TypeLoadException`). The packages that depend on Agent Framework now declare 1.26.0 as
  their floor, and `Tracon.AspNetCore` references the Hosting packages `1.26.0-preview.261023.1` and
  `1.26.0-alpha.261023.1` (exact versions, as before). Raise your own `Microsoft.Agents.AI*` references to 1.26.0 or later
  when you upgrade.
- **A saved agent for the `google` provider with a seed outside the 32-bit range no longer builds.** The Google.GenAI
  client library narrowed such a seed without a check (`3000000000` reached the model as `-1294967296`), so a definition
  saved earlier ran with a seed nobody had written. `POST /api/agents/validate` now reports `sampling_value_out_of_range`
  at `model.seed`, and a run of the stored agent fails with the compile error. Change the seed to a value from
  -2147483648 to 2147483647, or clear it.
- **`AddJobHandler` and `UseScheduling` are now methods of the Tracon chain, not of `IServiceCollection`.** They moved
  from `TraconServiceCollectionExtensions` to `TraconBuilderExtensions`. Every other
  capability already was one, and the site taught two entry points. A host that calls either on `builder.Services` fails
  to build (`CS1061`: `IServiceCollection` does not contain a definition for `AddJobHandler`, or for `UseScheduling`). Call
  it on the chain `AddTracon()` returns: `builder.AddTracon().AddJobHandler<MyHandler>("contoso.my-job")` and
  `.UseScheduling(options => options.RunWorker = false)`. The behavior is the same, with one difference in order: a
  handler is registered after `AddTracon()`, because the chain does not exist before it. The key checks, the scoped
  lifetime and the rule that code wins over configuration are unchanged. Tracon's own packages now follow one rule, checked
  by a test: `AddTracon` is the only public extension of `IServiceCollection` or `IHostApplicationBuilder`.
- **The warning for a factory agent now names the way out, and a second case warns.** `AddAgent(name, factory)` declares
  no model, so Tracon wrote one warning when its chat client did not come from `IModelProviderRegistry`. The text now
  says to declare the model with `AddAgent(name, model, factory)`. A factory agent that declares no model and whose client
  does come from the registry (built with the synchronous `CreateChatClient`) now gets a warning too: its calls use the key
  set up at start, skip the tenant's egress policy, and its run records carry no model and no cost. A host that read
  the log for the old text, or treated a warning as a failure, sees the new line once for each such agent name.
- **`AddAgent(name, factory, description)` has two overloads instead of one optional parameter.** The public API
  analyzer refuses more than one overload with an optional parameter, and the new `AddAgent` forms below come in pairs.
  A call with or without a description compiles as before, including `description:` by name.

- **Migrations.** PostgreSQL `0066`, SQL Server `0054` and SQLite `0053`. With `AutoApplyMigrations`
  (the default) they run at startup. Each adds five nullable columns to `runs`: `cache_write_input_tokens`,
  `cache_write_long_input_tokens`, `cache_write_cost`, `cache_write_price_per_mtok` and
  `cache_write_long_price_per_mtok`. No column is dropped and no row is backfilled: a run recorded before the
  upgrade keeps `NULL` in all five, which reads as "the write was not priced apart". A database that turned on
  `EnableReadViews` also gets a second definition of `runs_v1`: its `total_cost` adds the write charge and the
  five columns come last.
- **A prompt-cache write can raise the cost of a run.** With a write price set (see Added), the tokens Anthropic
  wrote into its cache leave the plain input charge and are billed at the write price. A run that wrote
  therefore costs more than it did before, and the cost quota ceiling, the webhook total and the statistics
  follow. With no write price nothing changes.
- **Public types changed.** `RunUsage`, `RunCost`, `RunTreeCost`, `RunStatistics`, `ModelDescriptor` and
  `ModelPriceOverride` gain properties; the wire, the OpenAPI document and both clients gain the same fields.
  Code that adds the cost terms of a `RunCost` by hand must add `CacheWriteCost`: call `RunCost.Total()`
  instead. A custom `IRunStore` must store the new fields and pass the new cases of the shared contract:
  `RunStoreContract.A_run_that_reports_only_a_cache_write_count_still_produces_a_usage_record`,
  `RunStoreContract.Statistics_total_the_cache_writes_of_every_run_beside_the_input_total`,
  `RunStoreContract.The_cache_write_charge_round_trips_with_its_unit_prices_and_counts_toward_every_total`,
  `RunStoreContract.The_cache_write_counters_round_trip_and_stay_null_when_they_were_never_reported`,
  `RunStoreContract.Tree_cost_adds_the_cache_write_charge_of_every_run_in_the_tree`,
  `RunStoreContract.Tree_usage_keeps_a_cache_write_class_nobody_in_the_tree_reported_null`,
  `RunStoreContract.Tree_usage_sums_both_cache_write_classes_across_the_runs_of_the_tree` and
  `RunStoreContract.UpdateRunCostAsync_writes_the_cache_write_charge_and_its_unit_prices`. Two existing
  expectations changed because a total now has four terms:
  `RunStoreContract.Every_cost_total_includes_the_cache_charge` and
  `RunStoreContract.No_cost_total_adds_amounts_in_two_currencies`.
- **`RequireProductionProfile()` asks five more decisions: a host that calls it does not start until each is answered or
  accepted by name.** `TraconProductionRisk` gains `UnregisteredRolePolicies`, `UncheckedToolCalls`,
  `UnrestrictedTenantEgress`, `UnverifiedTenantHeader` and `UnlimitedTenantWorkflows`. A host that never calls the
  method sees nothing change. The startup failure names every open decision with its setting, what it is today and how
  to answer it; accept one with `profile.Accept(TraconProductionRisk.<Risk>)`. Why each was added:
  - `UnregisteredRolePolicies`: an unregistered `Tracon.Reader`, `Tracon.Operator` or `Tracon.Admin` policy adds no role
    check, so any caller that passes the outer layers reaches the endpoint it would have guarded. It was skipped in
    silence unless `RequireRolePolicies` was on. Answer it by defining the three policies in `AddAuthorization(...)`. It
    is asked only of a host that called `MapTracon` before it started: the answer is known there.
  - `UncheckedToolCalls`: the built-in `IToolAuthorizationHandler` allows every call, so any caller of a run can make an
    agent run any tool it has. Answer it by registering your own handler, or with the new
    `Tracon:Tools:RequireTenantPolicy` and a tool policy for each tenant (see below). It is asked of every host,
    single-tenant ones included.
  - `UnrestrictedTenantEgress`: a tenant with no egress policy may call every provider the deployment has. Answer it with
    the new `Tracon:TenantProviders:RequireEgressPolicy` (see below). It is asked only when tenants are separated.
  - `UnverifiedTenantHeader`: with `UseTenancy` enabled, `AllowHeaderResolution` open and no `ClaimType`, any client
    that reaches the endpoint names the tenant it acts as. Answer it by setting `ClaimType`, or by closing the header
    path. Accept it when a gateway in front of Tracon writes the header. A request authenticated with an API key keeps
    that key's tenant either way.
  - `UnlimitedTenantWorkflows`: with `UseWorkflows` and separated tenants, one tenant that starts many runs holds every
    workflow slot. Answer it with `Tracon:Workflows:MaxConcurrentRunsPerTenant`. It is not asked while
    `Tracon:Workflows:Enabled` is `false`.

  A host that does not separate tenants (the built-in single-tenant context, or `UseTenancy(o => o.Enabled = false)`) is
  not asked the egress, header and workflow decisions: they are about one tenant reaching what another should not, so
  they are reported as not applicable.
- **A tenant value that is sent and is not a valid tenant id is now refused, instead of becoming the default tenant.**
  While tenancy is on and the value source is active, a malformed header answers `400` (`Tenant header malformed`) and
  a malformed claim of an authenticated identity answers `403` (`Tenant claim malformed`). Before this the value was
  dropped and the request ran as the default tenant, so a client with a typo in its tenant read and wrote the default
  tenant's data, and `AllowedTenants` did not catch it. A valid id is `^[a-zA-Z0-9_.-]+$` and at most 64 characters. An
  absent header, an empty header and a missing claim are not malformed and still resolve to the default tenant. The
  response never carries the value. The refusal is made by Tracon's endpoint filter, so it covers the endpoints Tracon
  maps; an endpoint you map yourself that reads `ITenantContext` still resolves a malformed value to the default tenant,
  so check it with `HttpTenantContext.IsValidTenantId` there. A client that sends a malformed tenant now sees the `400` or `403`; fix the value, or
  stop sending it. The contract gate does not see this change, because it treats a documented `4xx` as documentation.
  Covered by `TenantValueFormatTests`; the old `TenancyTests.Malformed_tenant_falls_back_to_default` was removed with
  the behavior it asserted.

- **An application or test project that targets `net8.0` or `net9.0` cannot restore this release: every package now
  targets `net10.0` only.** Restore fails with `NU1202`. Move the project to `net10.0`, or stay on `1.0.0-preview.6`, the
  last release that carries `net8.0` and `net9.0`; it stays on nuget.org. The `1.0.0-preview.3` notes announced the drop
  for the first release after Microsoft support for .NET 8 and .NET 9 ends on 2026-11-10; the drop no longer waits for
  that date. The source generator inside `Tracon.Core` now builds against Roslyn 5.0.0, the compiler of the oldest .NET
  10 SDK, so every SDK that can build `net10.0` loads it.

- **Chat completions: a stream that asked for `include_usage` ends with one more chunk.** A client that sends `stream_options.include_usage: true` (the OpenAI .NET SDK always does) gets, after the chunk that carries `finish_reason`, a chunk with an empty `choices` list and `usage`, then `[DONE]`. A client that treats the last chunk as the finish chunk must skip a chunk with empty `choices`. A client that does not send the option sees no change.

- **A custom `IAuditLog` must hash the entry it stores.** New contract tests, which a store that hashes the text as the caller wrote it fails: `AuditLogContract.A_chain_written_under_another_letter_case_verifies` and `AuditLogContract.A_chain_with_a_NUL_character_in_an_entry_verifies`. What to do: fold the tenant with `AmbientTenantScope.Normalize` and apply whatever rewrite your store makes to a field (a NUL character, for one) before you compute the hash, not after.

- **Two requests the in-memory and SQLite stores used to take are refused with `400`.** `PUT /api/quotas` with a `maxCost` of 10 000 000 000 or more (the money columns of the other stores cannot hold it), and `?bucket=` with a number that names no bucket.

- **On SQL Server, a request that stores a value too long for its column answers `400`, no longer `503`.** The identifier columns of the schema hold 200 characters
  (`nvarchar(200)`), and a longer `displayName`, `toolName`, `messageId`, suite `agentName` or `oauthClientId` made the server refuse the write. The host took every
  provider exception for "the store could not answer" and returned `503 Persistence store unavailable`, so a client or proxy that retries a `503` sent the same value again and got
  the same answer, and an operator saw a database alarm for a caller's input. The same applies to a number that does not fit its column (`400`, title `Number out of range for the
  store`); PostgreSQL keeps text of any length, so a number is the only value it can refuse there. Each provider recognizes its own errors: SQL Server `2628` and `8152`
  (text), `8115` (number); PostgreSQL SQLSTATE `22001` and `22003`. A connection that cannot be opened, a schema that is not current, a deadlock and a timeout stay `503`.
  What to do:
  - **Stop retrying `400` for this title.** The body is `application/problem+json` with the title `Value too long for the store`; sending the same request again gives the same answer.
    A client, proxy or queue that retried these as `503` now sees a request it should not repeat. The `detail` and the log name no field, column, table or value: the provider's
    message carries the value that was cut, and a value can be a secret pasted into the wrong field.
  - **The stores still differ.** SQL Server counts UTF-16 code units (100 emoji fit in `nvarchar(200)`, 101 do not); PostgreSQL and SQLite keep text of any length, so the same request
    succeeds there. Validate the length in your own client when the same code runs on both.
  - **A store you wrote yourself is not covered.** Only the SQL Server, PostgreSQL and SQLite providers recognize a refused value; an exception from another provider is still `503`.

- **`Tracon.Client` and `@tracon/client`: a request that sets only the required members is valid, and the OpenAPI document says what the server does.** The generated client wrote every
  property it had, so a caller that set only `Name` and `Model` sent `"providerSettings": null` and got `400`, a `JsonElement` left unset threw `InvalidOperationException` in the
  serializer, and the TypeScript type of a free-form object was `Record<string, never>`, which no key may carry a value. The server now accepts `null` for these members and the document
  and both clients are regenerated. Nothing changes for a caller of the HTTP API, and an older client that sends the `null` today is accepted.
  What to do, for the .NET client (`Tracon.Client`; these types are generated, the server packages do not change):
  - **A free-form dictionary is `IDictionary<string, object>?`, no longer `object`.** `ModelBinding.ProviderSettings`, `AgentDefinitionRequest.Metadata`, `AgentSkillRequest.Metadata`,
    `EvalExpectedToolCall.Arguments` and `ToolInputResponse.Content` are nullable and take a `Dictionary<string, object>`; assigning another object, such as a boxed `JsonElement`, no
    longer compiles. `AgentDefinition.Metadata` and `AgentSkillDefinition.Metadata` (what a response returns) are `IDictionary<string, object>`, no longer `object`; read a value with
    its key.
  - **`EvalSuiteSaveRequest.Checks` and `JobScheduleSaveRequest.Payload` are `JsonElement?`, no longer `JsonElement`.** Code that reads them needs `.Value` or a null test.
  - **A request member the server fills in when it is left out starts with that value.** `McpServerRequest.Enabled` and `RequiresApproval`, and the `Enabled` of `AgentSkillRequest`,
    `InboundTriggerSaveRequest`, `JobScheduleSaveRequest`, `QuotaSaveRequest`, `RetentionPolicySaveRequest` and `WebhookSaveRequest` start at `true`, no longer `false`;
    `JobScheduleSaveRequest.TimeZone` starts at `"UTC"` (it was `null`, which the server refused); `RunReplayRequest.ToolMode` starts at `ReplayTools` (it was `NoTools`);
    `AgentSkillResourceDefinition.MediaType`, `CanaryPolicy.MinSampleSize`, `CanaryPolicy.RampInterval` and `ResponseCacheSettings.Lifetime` start at `text/plain`, `20`, `01:00:00`
    and `00:10:00`. **This changes what a call that did not set the member records.** The one to check first is `TraconSaveMcpServerAsync`: a server saved with only an `Endpoint`
    used to be saved disabled with its approval gate off; it is saved enabled with the gate on. Set `RequiresApproval = false` on a server you meant to run without approval.
    The other `Enabled` members change what the saved record does, so read the calls that leave `Enabled` out before you upgrade. Raw HTTP callers and `@tracon/client` never saw a
    change: the server has always filled in `true`, and `enabled` was already optional in the TypeScript types. Only a `Tracon.Client` call that did not set the member sent `false`:
    - **`TraconSaveRetentionPolicyAsync` saves an enabled policy, and that policy deletes data.** A policy with `MaxAgeDays` or `MaxRows` used to be saved disabled when `Enabled` was
      left out; it now removes rows older than the limit (or beyond the row count) the next time the cleanup job runs, and `Archive = false` deletes them without a copy. A policy with
      neither limit still removes nothing. Preview the target first, and set `Enabled = false` on a policy you want to keep paused.
    - **`TraconSaveWebhookAsync` saves a subscription that delivers events.** Payloads leave for the subscription's URL from the first matching event. Set `Enabled = false` to register one without sending.
    - **`TraconSaveInboundTriggerAsync` saves a trigger that accepts requests**, and each request it accepts starts its agent or workflow. `TraconSaveScheduleAsync` saves a schedule that dispatches on its cron
      expression, and `TraconSaveQuotaAsync` saves a rule that refuses runs once its limit is reached (`429`).
  - **Nine operations list the `501` they have always answered when the capability is not registered**, so the client raises `TraconApiException<ProblemDetails>` instead of the bare
    `TraconApiException`: `TraconVoiceHealthAsync`, `TraconRefreshMcpToolsAsync`, `TraconListMcpPromptsAsync`, `TraconGetMcpPromptAsync`, `TraconListMcpResourcesAsync`,
    `TraconReadMcpResourceAsync`, `TraconStartMcpOAuthAsync`, `TraconGetWorkflowGraphAsync` and `TraconListWorkflowRequestsAsync`. A `catch (TraconApiException)` still catches it.
  What to do, for `@tracon/client`:
  - **A free-form object is `{ [key: string]: unknown }`, `null` or absent, no longer `Record<string, never>`.** A cast to `Record<string, never>` that worked around the old type can
    go; the type also accepts `providerSettings: { 'anthropic.promptCaching': true }` without one. `checks` and `payload` are `unknown | null`, optional.
  - **An enum alias no longer carries `| null`.** `RunErrorClass`, `WorkflowKind`, `EvalCaseSource`, `HostedToolKind`, `RunScoreBucket`, `RunScoreTarget`, `TimeSeriesBucket` and
    `VoiceSessionEndReason` were unions that ended in `null` because one member somewhere was nullable. The member that is nullable says so on its own wrapper
    (`errorClass?: null | RunErrorClass`); a `Exclude<…, null>` written to narrow the alias can go.
  - **A member with a server default stays optional and gains a `@default` note** (`enabled`, `requiresApproval`, `timeZone`, `lifetime`, `minSampleSize`, ...). Omitting it applies the
    server default; nothing about the call changes.
  - The document lists `501` on the nine operations above, so `error` is typed for it.
  The published `tracon.json` is also corrected in three places that a consumer's own document is not: a nullable enum is nullable once, a nullable free-form value is `anyOf` and not
  `oneOf` (a `null` matched both branches), and a dictionary of free-form values names its value schema. The transformer is one file, `samples/Tracon.Api/OpenApi/PublishedDocumentCorrections.cs`.

- **A request body that writes an enum member as a number no member has is refused with `400`.** The converter that reads the enum fields of a body took any integer (and any string of
  digits) as the enum with that number, whether or not a member had it. `POST /api/runs/{runId}/feedback` with `"kind": 0` answered `200` and stored a score of kind `0`
  (`RunScoreKind` starts at `1`); every list that held it then wrote `"kind": 0` where the document says one of four names. It answers `400` now, with the message that lists the members, like an
  unknown name. A number that is a member (`"kind": 1`) is accepted as before, a nullable enum (`sourceKind` of the eval-case promotion body) is refused the same way and still takes `null`, and a `[Flags]` enum still takes a combination. What to do: nothing, unless a client sends numbers; send the name.

- **Behavioural breaking change: the default `Idempotency-Key` limit is 200 characters, not 255.** `Tracon:Idempotency:MaxKeyLength` defaulted to 255, and SQL Server keeps the key in an
  `nvarchar(200)` column, so a key of 201 to 255 characters passed the check and was refused by the store. That refusal is a `400` (the SQL data-fault mapping), but the filter said
  "at most 255" and the other stores took the key, so the same request answered `200` on PostgreSQL, SQLite and the in-memory store and `400` on SQL Server. The default is the width of the
  narrowest column now, and the answer is the same on every store: `400` with "The key may be at most 200 characters". A client that sends keys of 201 to 255 characters (rare: a UUID is 36)
  shortens them, or the host sets `Tracon:Idempotency:MaxKeyLength` back; above 200 SQL Server still refuses the key.

- **A number that names no member of an enum query parameter is refused with `400` on every endpoint.** The binder reads an enum parameter with `Enum.TryParse`, which takes any integer, so `GET /api/runs?status=99`
  answered `200` with an empty page (text that names no member was already `400`). Seven parameters did: `status` and `kind` of `GET /api/runs`, `status` of `GET /api/jobs` and of
  `GET /api/webhooks/{name}/deliveries`, `target` of `GET /api/evaluation/scores/summary`, `kind` of `GET /api/stats/timeseries` and `period` of `GET /api/quotas/usage`; `bucket` of `GET /api/evaluation/scores/summary` and of `GET /api/stats/timeseries` answered `400` since the
  previous release. They answer `400` now, with the message that lists the members, like a name that is not one. One endpoint filter on the endpoint group does it, reading each handler's signature when the endpoint is
  built, so a parameter added later is covered without anyone remembering to ask (and a `[Flags]` enum takes any combination of its members). A number that is a member (`?status=2`) is accepted as before.

- **Behavioural breaking change: a text option made only of white space stops the host at start.** A number, flag or duration key that held only white space was "not given" and kept its default, but a text
  option kept the blanks as its value: `Tracon:Audit:ChainKeyId` of `"   "` named a key called blanks (and stopped the host later, through the hosted start check, with a message about a missing key),
  `Tracon:ContentGuard:Pattern:MaskReplacement` of blanks masked a match with spaces, `Tracon:Pricing:Currency` reported cost in a currency called blanks, and `Tracon:SingletonExecution:OwnerId`
  named an instance with blanks. Seventeen options are refused now, each by the validator of its options type, from configuration or from code, and the message names the member and says to write a value or
  remove the key: `Audit:ActorClaimType`, `Audit:ChainKeyId`, `ContentGuard:Pattern:MaskReplacement`, `ContentProtection:ActiveKeyId`, `Images:Provider`, `Images:Model`, `Knowledge:EmbeddingProvider`,
  `Pricing:Currency`, `SessionOwnership:ManagementPolicy`, `SingletonExecution:OwnerId`, `Voice:Conversation:VoiceId`, `Voice:Conversation:OutputMediaType`, `Voice:Live:Instructions`,
  `Webhooks:AllowedConfigurationPrefix`, `Mcp:AllowedConfigurationPrefix`, and the two prefixes `TenantProviders:AllowedConfigurationPrefix` and `InboundTriggers:AllowedConfigurationPrefix`
  (which the permutation test cannot find by reflection and which have their own test). An empty text is still "not given", and the empty string stays the documented way to say that no policy manages another
  user's sessions. What to do: a host whose configuration holds such a value (a template that left `"   "` where a value belongs) removes the key or writes the value. `ConfigurationPermutationTests`
  no longer keeps a list of text options that accept blanks: a new text option that does is a failure.

- **Behavioural breaking change: four MCP limits of zero or less stop the host at start.** `Tracon:Mcp:MaxToolsPerServer`, `MaxResourceBytesPerResource` and `MaxResourceBytesTotal` of zero or less must now be at least 1, and
  `OAuthTokenSlidingLifetime` must be greater than zero (its documentation already said so and nothing checked it). A tool limit of zero kept no tool (the first tool of a server tripped it, with a warning that quoted the limit),
  a byte limit of zero trimmed every resource to an empty text, and nothing checked the sliding lifetime; nothing said so when
  the host started, and the setting looked applied. `MaxResourcesPerServer` and `MaxPromptsPerServer` keep their documented meaning of zero (keep none).

- **Behavioural breaking change: `RequireProductionProfile()` asks one more decision, `OrphanedRunsAfterRestart`.** A deploy stops the host, and a run in flight gets the cancellation: a model that honours it
  ends the run as canceled, and one that does not leaves the process to end first (the host waits its shutdown timeout, then exits). A crash or a kill does the same. With the state in PostgreSQL, SQL Server or a
  SQLite file, that row is still there when the next process starts, and only run reconciliation closes it; with `Tracon:RunReconciliation:Enabled` at its default of `false` it stays `Running` for ever, counted as
  running and diluting the error rate. The decision list had a row for this only when more than one instance is declared (`UnreconciledRuns`). A host that calls `RequireProductionProfile()`, keeps its state where it
  survives a restart and has reconciliation off stops at start until it sets `Tracon:RunReconciliation:Enabled` to `true` or accepts the risk by name with
  `profile.Accept(TraconProductionRisk.OrphanedRunsAfterRestart)`. It is not asked on the in-memory stores (a restart takes every row with it), and not when `Tracon:Deployment:Instances` is above `1`: `UnreconciledRuns` asks the
  same setting there, so a host that already answered or accepted it is not stopped again. The default does not change (reconciliation writes a heartbeat for every running run, which a development host does not want),
  and a host that does not call `RequireProductionProfile()` changes nothing.

### Added

- **`Tracon:AgentCache:MaxEntries` (4096) and `Tracon:AgentCache:IdleLifetime` (one hour) bound the compiled agent cache.** When a new entry would pass the limit the entries used least
  recently are dropped, down to a tenth below it, and an entry that was not used for the lifetime is dropped by a check that runs while the cache is in use. A hit takes no lock and allocates nothing.
  Both values are checked when the host starts: zero, a negative value, more than 1,000,000 entries or more than 30 days is refused. New public type `TraconAgentCacheOptions` and property
  `TraconOptions.AgentCache`; the cache stays registered with `TryAdd`, so a registration of your own wins. The two keys are in the `AgentCache` rows of the configuration reference.

- **`Tracon:Idempotency:ReservationTimeout`: a key whose process died is taken over after an hour.** A request reserves its `Idempotency-Key`, runs, and then
  stores its response. A process that died in between left a reservation nobody finished, and every later request with the key answered `409` until
  retention deleted the record. The next request now takes a reservation that is older than `ReservationTimeout` (default one hour, `00:00:00`
  never takes one over), on the memory store and on the three SQL providers; of several requests that race for one expired reservation exactly one
  takes it, and a completed key is never taken over. Keep the value longer than your longest request that carries a key: a request that is still
  running when its key is taken runs a second time. `IdempotencyRequest` gains `ReservationTimeout` for a store you wrote or call yourself;
  New contract tests, which a store you wrote has to pass: `IdempotencyStoreContract.A_reservation_older_than_the_timeout_is_taken_over_by_the_next_request`, `IdempotencyStoreContract.A_reservation_younger_than_the_timeout_is_still_in_progress`, `IdempotencyStoreContract.A_reservation_is_never_taken_over_without_a_positive_timeout`, `IdempotencyStoreContract.A_timeout_longer_than_any_reservation_has_been_alive_takes_nothing_over_and_does_not_throw`, `IdempotencyStoreContract.A_completed_key_is_never_taken_over_however_old_it_is`, `IdempotencyStoreContract.A_taken_over_reservation_belongs_to_the_taker_with_its_own_fingerprint`, `IdempotencyStoreContract.Only_one_of_several_requests_that_race_for_an_expired_reservation_takes_it`, `IdempotencyStoreContract.A_reservation_of_another_tenant_is_not_taken_over`, `IdempotencyStoreContract.A_request_that_was_taken_over_cannot_store_its_response_under_the_new_owner`, `IdempotencyStoreContract.A_request_that_was_taken_over_cannot_release_the_new_owners_reservation`, `IdempotencyStoreContract.A_request_that_was_taken_over_cannot_delete_a_response_the_new_owner_stored`, `IdempotencyStoreContract.The_owner_of_a_reservation_completes_and_releases_it_as_before`.
  `CompleteAsync` and `ReleaseAsync` of `IIdempotencyStore` take an optional `reservedAt`, the `CreatedAt` of the reservation the caller holds: a
  request that was taken over and finishes late changes nothing, so it cannot store its response under the request that took the key or delete
  that request's record. A store you wrote has to take the parameter; `null` keeps the old behavior.

- **`Tracon:RunRecording:SinkTimeout`: a run event sink that does not return no longer stops its run.** Tracon calls each `IRunEventSink` while it holds the
  run's append gate, one event at a time, so a sink that never returned (a lost connection, a full queue it waits on) held every later event
  of the run and the run with them. The writer now waits `SinkTimeout` (default 30 seconds; `00:00:00` waits for ever, which is how it
  behaved, and so does `Timeout.InfiniteTimeSpan` in code, written `-00:00:00.001` in configuration) for one event, then drops that sink for the run as it does for a sink that throws: logged, counted once at the `sink` stage of
  `tracon.run.recording_failures`, the store and the other sinks unaffected. The sink is not told to stop; its call finishes on its own,
  and a failure it ends with is observed rather than reported later as an unobserved task exception. Nothing is remembered between runs: every
  run that meets a sink which does not return waits the whole time on its first event.

- **A stored `Handoff` workflow names who may hand off to whom: `WorkflowDefinition.Handoffs`, with a per-run limit,
  `MaxHandoffs`, that stops a loop of handoffs.** Before, a stored `Handoff` workflow was a star: the first agent could
  hand off to every other agent and none of them could hand off, so a specialist could not return to the coordinator
  and two specialists could not pass a case to each other. The only way out was to leave the stored definition and
  build the graph in code, which loses the console, the versions and the file transfer. `Handoffs` is a list of
  `WorkflowHandoff` (`From`, `To`, an optional `Reason` the model reads as the description of the handoff tool). An empty
  list is the star, so a definition saved before the field existed runs as it did. A save refuses an edge that names an
  agent outside `AgentNames`, an edge from an agent to itself, a pair twice, a blank reason, a first agent with no edge
  out of it and an agent the first agent cannot reach, each with a `400` that names the edge. Edges that let two agents
  hand off to one another can loop and the framework does not count handoffs, so Tracon counts them for each run: the
  handoff past `MaxHandoffs` (10 when left out) is stopped before the framework acts on it and the run ends `Failed`
  with the new error type `handoff_limit_exceeded` (`TraconErrorTypes.HandoffLimitExceeded`, class `QuotaExceeded`).
  A resumed or answered run is a new run and counts from zero. The limit applies to a workflow stored as a definition.
  A `Magentic` definition also takes `MaxStalls` (rounds without progress before a replan, the framework's 3 when left
  out) and `MaxResets` (replans before the task stops, no limit when left out); at the reset limit the run ends
  `Completed` with a message that says it stopped at the limit, in place of the manager's answer. Both are 1 or more:
  a reset limit of 0 stops the task before its first round. The framework's `EnableReturnToPrevious` is not offered:
  a Tracon run starts with no earlier state. The console edits the edges, the three limits and shows the edges in the
  version compare; adding or removing an edge is a different graph for a run that waits, and the editor says so. The
  file of `tracon export` and `tracon import` carries the four fields. New public types: `WorkflowHandoff`, and
  `WorkflowDefinition.Handoffs`, `MaxHandoffs`, `MaxStalls`, `MaxResets`. `MaxIterations` no longer claims to be the only
  guard against a handoff loop.
  A custom `IWorkflowDefinitionStore` keeps the four fields with each version; the new cases:
  `WorkflowDefinitionStoreContract.A_handoff_definition_without_edges_or_a_limit_reads_back_as_the_star_with_no_limit`,
  `WorkflowDefinitionStoreContract.Handoff_topology_and_the_handoff_limit_survive_the_round_trip`,
  `WorkflowDefinitionStoreContract.Magentic_limits_survive_the_round_trip` and
  `WorkflowDefinitionStoreContract.The_handoff_topology_and_the_limits_are_kept_by_the_version_they_were_saved_as`.


- **A definition moves between installations as a file: `GET /api/definitions/export`, `POST /api/definitions/import`,
  `tracon export`, `tracon import`, and a Transfer screen in the console.** Before, an agent built and versioned in a
  development installation could only reach production by retyping it: `GET` returns the detail shape and `PUT` takes
  another, an MCP server's plain header value is never returned, and a version pin carries the source installation's
  number, which names other content in the target. The export writes the agents, workflows, stored skills and remote
  MCP servers of the caller's tenant as one `tracon.definitions` file (format version 1, sorted by name, so the same
  definitions write the same bytes). `?agent=` and `?workflow=` start from named definitions and bring what they need
  (the skills an agent loads, the agents it calls, reviews with or shares instructions from, the MCP servers whose tools
  it uses); `?types=` writes every definition of the kinds named. The import plans every item against the target (`create`,
  `newVersion`, `update`, `unchanged`, `rejected`, `blocked`, `skipped`), validates the whole file as if it were already
  saved, and writes only when no item is refused. `dryRun` defaults to `true`, a dry run needs `Operator` and writes
  nothing (not even an audit line), writing needs `Admin`. Every item passes the **same rules** the save endpoints apply
  (the four rule sets were moved out of their endpoints so both writers call one copy), so an import cannot accept what a
  save refuses. A version pin is translated to the version the target holds, and a pin to a version the file does not
  hold stops the import with `import_pin_untranslatable`. An agent that uses a tool of an MCP server the file would
  create is `blocked` until the servers are imported first (`types=mcpServers`). Importing the same file twice writes
  nothing the second time. A file carries no tenant, history, run, session, `secret`, OAuth token, plain MCP header
  value or script permission; it moves the **names** of the headers and of the configuration keys. A member this version
  does not know, and a `formatVersion` above 1, are refused with `400` that names the path. The stores share no
  transaction: a store that fails part way answers `200` with `partial: true`, and running the import again writes the
  rest. `tracon import` exits `0` valid or written, `1` argument error, `2` could not run, `3` the file is invalid or an
  item is blocked. Nothing in the public API of the server packages changed; `Tracon.Client` and `@tracon/client` gain the
  two operations. The guide *Move definitions between installations* has the rest.

- **A run records the citations the model attached to its answer, and how many of its request's messages each source
  added.** Before, a hosted web search, a file search or a retrieval tool gave the model a source for its claim, and the
  run record kept the claim without the source: an auditor read the record and could not see what an answer rested on.
  Likewise a context provider (conversation memory, a retrieval provider) added messages to the model's request and
  the record held no trace of it. Two reserved `Custom` event types are new in `GET /api/runs/{id}/events`:
  `tracon.citation`, one for each citation, with `title`, `url`, `fileId`, `toolName`, `snippet` and the cited span;
  and `tracon.context`, one for each run, the messages of the first model request counted by source (`External`,
  `ChatHistory`, `AIContextProvider`, with the provider's id). The citation event does not depend on
  `RecordMessageDeltas`, follows `RecordToolPayloads` (a title or a snippet can be personal data), and writes only the
  scheme and host of the address by default: a retrieval tool's citation can point at a signed address whose path or
  query is a credential. New key `Tracon:RunRecording:RecordCitationUrls` (default `false`) turns the whole address
  on; user information (`user:password@`) is never written. The context event carries counts only and is written with
  `RecordToolPayloads` off. A citation shown again by a stream is written once; the OpenAI Responses and Anthropic adapters
  were measured and do not repeat one. Neither event reaches the live run stream. **Every run that calls a model now carries one extra `Custom` event** (`tracon.context`, right after `RunStarted` for a streamed run): a client that counts a run's events, or reads the first `Custom` event as its own, should select by `customType`. New public members:
  `RunEventCustomTypes.Citation`, `RunEventCustomTypes.Context`, `TraconRunRecordingOptions.RecordCitationUrls`.

- **Embedding, image and speech calls and the steps of a workflow are spans of the run trace.** Before, the trace of a
  run held the agent, chat and tool spans and nothing else, so a slow query embedding, a 30-second image or a slow
  workflow step read as one opaque tool span or as a gap. The consumer's `IEmbeddingGenerator` (the `search_knowledge`
  tool, knowledge ingestion), the image generator and a Microsoft.Extensions.AI speech client served as a Tracon speech
  provider now run inside the library's OpenTelemetry ring on the `Tracon` source: an `embeddings` span, and a
  `generate_content` span for an image, a synthesis or a transcription, told apart by `gen_ai.output.type` (`image`,
  `audio`, `text`). A workflow Tracon builds from a stored definition that lists its `nodes` writes `workflow.session`,
  `workflow_invoke` and one `executor.process <step>` span per step; message sends and edge groups are off, because a run
  keeps at most `Tracon:Observability:MaxSpansPerRun` spans. A stored workflow of three steps (two agent steps and one
  function step) keeps 13 spans of the 200. A stored definition that names a pattern (`agentNames` with `Sequential`,
  `Concurrent`, `Handoff`, `GroupChat` or `Magentic`) writes the agents' own spans but no workflow span: the framework
  builds those graphs itself. A graph built in code is the consumer's: Tracon cannot add
  the ring to it, so the trace also listens to the framework's own `Microsoft.Agents.AI.Workflows` source, and a graph
  that calls `.WithOpenTelemetry()` shows its steps while a graph that does not changes nothing. The rings never set
  `EnableSensitiveData`; that switch stays yours, and `RecordSensitiveData` decides what the store keeps. A host that
  already wraps its own generator with `UseOpenTelemetry` sees two spans per call in its exporter, one on its own source
  and one on `Tracon`. An ingestion and the image and voice endpoints run outside any run, so their spans reach your exporter and never a
  stored trace. The rings also publish `gen_ai.client.operation.duration` and `gen_ai.client.token.usage` on the
  `Tracon` meter, as the chat ring already did. No new public type.

- **MCP OAuth works on two instances. Set `Tracon:Mcp:OAuthTokenStore` to `DistributedCache`.** The default, `Memory`,
  keeps today's behavior: a token lives in the memory of the process that obtained it, and only the instance that started an
  authorization can finish it. With `DistributedCache` the token is kept in your `IDistributedCache`, encrypted with
  `IContentProtector`; the key stays in configuration and the token is never written to the Tracon database. The
  provider's redirect may reach either instance: the instance that receives it leaves the code in the cache, the instance
  that started the flow takes it and exchanges it, and the instance that received the redirect shows the result. One
  instance at a time refreshes a token, through a lease in the lease store (`ISingletonLeaseStore`, a SQL provider for a
  lease that two instances share). The application does not start in this mode when no `IDistributedCache` is registered or
  content protection is off, and starts with a warning when the cache or the lease store lives in one process. The new
  `Tracon:Mcp:OAuthTokenSlidingLifetime` (default 30 days) sets how long a token stays in the cache without a read or a write. New public
  types: `McpOAuthTokenStore`, `TraconMcpOptions.OAuthTokenStore` and `TraconMcpOptions.OAuthTokenSlidingLifetime`. In this
  mode the MCP client reads the cache for each HTTP request it sends to the server, because a copy kept in the process
  could refresh with a refresh token the provider has already replaced. Without this setting, a callback that reaches the
  other instance now says that this instance may not have started the authorization and names the setting.
- **The audit trail's hash chain can be signed with a key.** Set `Tracon:Audit:ChainKeyId` to the id of a key whose
  base64 value is under `Tracon:AuditChainKeys:<id>` (at least 32 bytes). New entries are then hashed with HMAC-SHA256 and
  carry the key's id in `keyId`; the key value is read from configuration at start and never written to the database, a
  log, the diagnostics report or a response. A bad key stops the host and the message names the configuration key, not the
  value. `GET /api/audit/verify` checks each entry with the key it names and answers `UnknownKey` for a key the host does not
  hold (a key was removed, or the row's `key_id` was changed by hand; never `Valid`: put the key back and compare the id with your key history), `Broken` for an unsigned entry after a signed one (a downgrade), and counts the
  entries written without a key in `unkeyedEntries`. Entries written before the key was set stay valid and are counted. To
  rotate, add a new key under a new id, point `ChainKeyId` at it and restart, keeping the old value. Turning the key off keeps
  writing entries unsigned and logs one warning per tenant. It protects against someone who can write to the audit table
  but cannot read the host's configuration; it does not anchor the chain outside the database.
- **The rate limit can hold for the whole deployment.** `Tracon:RateLimit:Scope` is new. `Instance` (the default) is
  today's behavior: each process counts in its own memory, so two instances with a `PermitLimit` of 100 admit 200 requests
  per window. `Deployment` counts in the database instead, through the new `IRateLimitCounterStore` (the three SQL
  providers register one that writes the `rate_limit_counters` table), so the limit and its `Tenant` partition hold across
  every instance. One setting covers both limiters, the one on the endpoint groups and the one on the inbound trigger accept
  endpoint, and it costs one database write for each counted request. `QueueLimit` above `0` together with `Deployment`
  stops the application at startup, because a database counter has no waiting line. Without a SQL provider the counter is
  still in the memory of the process and the application logs a warning at startup. If the counter store fails, the
  instance applies its own in-memory limit for that request, the limit is never lifted and no request is refused for it, and
  the new `tracon.rate_limit.store_failures` counter (tag `tracon.rate_limit.limiter`: `endpoint` or `trigger`) counts it.
  The stored window never moves back, so keep the instances' clocks synchronised: a clock that jumps far ahead holds the
  partition's counter until real time catches up, and the application logs a warning when the stored window is more than one
  window ahead of an instance's own.
  To count somewhere else (Redis, for example), implement `IRateLimitCounterStore`; `RateLimitCounterStoreContract` checks
  the atomic count with 64 parallel callers. See [Rate limits across instances](/guides/multiple-instances/#rate-limits-across-instances).
- **The management API and the AG-UI endpoint keep a rate-limit counter each**, with either scope. This was already so and
  is now written down: an AG-UI request does not spend the management API's window.
- **The console's Audit screen has a Verify chain button** that runs the walk and shows the status, the entries checked,
  the unsigned entries and the first failing entry.
- **A tenant can have a tool policy: the tools its runs may call.** `ITenantToolPolicyStore` (in memory by default, and on
  PostgreSQL, SQL Server and SQLite through a new `tenant_tool_policies` table) holds two closed lists per tenant: the
  tool names it may run and the permissions it holds. A call passes when its tool name is listed or the permission its
  tool declares is granted; two empty lists refuse every tool. It is a gate **in front of** `IToolAuthorizationHandler`,
  not a replacement: a call needs both to allow it, and a call the policy refuses is not shown to your handler. It
  covers the tools the handler is asked about (tools registered in code, MCP tools, the `file_access_*` tools of a
  session workspace, `search_knowledge` and `search_conversation_memory`). A refused call is an ordinary tool result
  (`This tool is not enabled for this tenant.`) recorded with `authorizationDenied`, and a store that cannot be read
  refuses the call and logs an error. The policy is read on every call, so a saved policy applies to the next one.
  A tenant with no policy passes, so nothing changes until one is saved. Manage it with `GET`, `PUT` and `DELETE
  /api/tenants/{tenantId}/tools` (`SecurityAdmin`, and platform authority for another tenant; saves and deletes are
  audited as `tenant_tool_policy.save` and `.delete`) or in the tenant panel of the console. The new migration adds one
  table on each provider and changes no existing table.
  A store of your own implements `ITenantToolPolicyStore` and passes the new `TenantToolPolicyStoreContract` in
  `Tracon.Testing.Contracts.Xunit`: `TenantToolPolicyStoreContract.A_policy_is_deleted_whatever_case_the_caller_uses`,
  `TenantToolPolicyStoreContract.A_policy_saved_under_a_different_case_replaces_the_existing_one`,
  `TenantToolPolicyStoreContract.A_tenant_is_matched_case_insensitively`,
  `TenantToolPolicyStoreContract.Concurrent_saves_for_one_tenant_leave_one_whole_policy`,
  `TenantToolPolicyStoreContract.Deleting_the_policy_removes_it`,
  `TenantToolPolicyStoreContract.Each_list_is_kept_apart_from_the_other`,
  `TenantToolPolicyStoreContract.Saved_policy_is_read_back_with_both_lists_in_order`,
  `TenantToolPolicyStoreContract.Tenant_with_no_saved_policy_has_none`,
  `TenantToolPolicyStoreContract.Two_empty_lists_are_a_policy_and_not_the_absence_of_one`,
  `TenantToolPolicyStoreContract.Upsert_replaces_the_existing_policy` and
  `TenantToolPolicyStoreContract.Upsert_returns_the_canonical_tenant`.
- **`Tracon:Tools:RequireTenantPolicy` makes a tenant with no tool policy run no tool.** Off by default. Turned on, a
  tenant without a policy row is refused on every tool the policy covers, and the default tenant needs its own row. The
  refusal text names the setting. Saving a policy is never blocked by it. It is the second answer to the
  `UncheckedToolCalls` decision of `RequireProductionProfile()`, next to registering your own handler; no new risk was
  added, so no host that started before stops. A value the binder cannot read (`yes`) stops the host at startup.
- **`Tracon:TenantProviders:RequireEgressPolicy` makes a tenant with no egress policy call no provider.** Off by default,
  so nothing changes until it is turned on. Turned on, a tenant without a policy row is refused on every path to a
  provider (the agent's model and its fallbacks, the summarizer and loop judge, image, speech, live voice and
  embedding), and a provider binding cannot be saved for it. The default tenant is a tenant too and needs its own row.
  The refusal is the same `TraconTenantProviderRefusedException`, and its message names the setting. An empty policy
  still forbids everything, and a tenant with a policy is judged by its list alone. Set it with the new
  `TraconTenantProviderOptions.RequireEgressPolicy`. It is the answer to the `UnrestrictedTenantEgress` decision above.
  A value the binder cannot read (`yes`) stops the host at startup instead of leaving the switch off.
- **A factory agent can declare its model: `AddAgent(name, model, factory)`.** Tracon builds the chat client for the model,
  with the tenant's own provider key and the tenant's egress policy, and hands it to the factory as
  `AgentFactoryContext.ChatClient` (the new `AgentFactoryContext` also carries `AgentName`, `Services` and `Model`). The
  declared model is the model of the agent in the catalog, so the run record names it and prices the run, the cost quota
  stops the agent, and `POST /api/agents/{name}/estimate` answers (it returned `400` for a factory agent). The factory can
  await: `Func<AgentFactoryContext, CancellationToken, ValueTask<AIAgent>>`. Before, a consumer who built an agent with
  code lost the tenant's key, the egress policy and the cost, and had to write an `IAgentSource` to win them back. A
  request field on the binding (`Temperature`, `TopP`, `TopK`, `MaxOutputTokens`, `Seed`, the two penalties,
  `StopSequences`, `ToolMode`, `AllowMultipleToolCalls`, `ReasoningEffort`, `ReasoningOutput`, `ResponseFormat`) is
  refused when you register, with the field named: the factory builds its own `ChatOptions`, so Tracon would ignore the
  value. Tracon keeps neither the agent nor the client between calls, because the client holds the key of one tenant. It
  warns once for the agent name when the agent does not use the client it was given.
- **A tool can take its dependency at setup: `AddTool(provider => tool)`.** The factory gets the host's service provider
  when Tracon builds its tool registry at start; `AddTool(provider => tool, options => …)` sets the approval, effect,
  permission and the other policy. A tool gets an empty provider at call time, so the way to give it a dependency used to
  be `services.AddSingleton(provider => new TraconToolRegistration(…))`. A factory that asks for `IToolRegistry`, or for a
  service that needs it, stops the host with an error that names the cause; without that check the host start did not
  finish. A factory that returns `null`, or a tool with an invalid name, stops it as well.
- **A guide for a team that built agents with `AddAIAgent`: [Move an agent from AddAIAgent to
  Tracon](https://tracon.dev/guides/from-maf-hosting/).** It maps `AddAIAgent`, `WithAITool`, `WithSessionStore` and
  `AddWorkflow` to the Tracon call for each, and says what the move keeps and gains. Tracon does not read `AddAIAgent`
  registrations.
- **`dotnet new tracon-api --model <name>` writes the chat model to `appsettings.json`.** The value lands in the
  provider's `DefaultModel` (Azure OpenAI: `DefaultDeployment`), JSON-encoded, so a name with a quote or a backslash
  stays a valid file. It is not a `secret`. The first answer now takes five commands and no code edit:
  `dotnet new install`, `dotnet new tracon-api --model`, `cd`, `dotnet user-secrets set`, `dotnet run`. The template
  README no longer asks for `dotnet user-secrets init`: the generated project file already carries its `UserSecretsId`.

- **A catalog entry can name the request fields its model rejects: `ModelDescriptor.RejectedSamplingFields`.** A model
  decides what it accepts, not the API surface: `openai` sends `stop`, and the three models of the OpenAI account used for
  the measurement answer it with `400 unsupported_parameter`.
  `Tracon:Providers:{Provider}:Models:{n}:RejectedSamplingFields` takes a name or a comma-separated list
  (`"StopSequences"`, `"Seed, StopSequences"`); a name that does not exist, and a number with bits no field owns, stop
  startup. A definition that sets a listed field for that model, on the primary model or on a `Fallbacks` link, does not
  compile; `POST /api/agents/validate` reports `sampling_field_rejected_by_model` at the field's path
  (`model.stopSequences`) and a save returns `400`. Left out (`None`), the catalog checks nothing, as before.
  `GET /api/models` returns the value as a string of names, and `Tracon.Client` has it as
  `ModelDescriptor.RejectedSamplingFields` (`string`). The sample catalog marks the three measured models.
- **A provider can state the value ranges its API surface accepts: `ISamplingSupport.SamplingLimits`.** The new
  `SamplingLimits` record (`MinSeed`, `MaxSeed`, `MaxStopSequences`) states no limit by default, and the member has a
  default body, so an `ISamplingSupport` implementation written earlier compiles and runs unchanged. A definition whose
  seed or number of stop sequences is outside a stated range does not compile, validation reports
  `sampling_value_out_of_range` at the field's path, and a save returns `400`; a `Fallbacks` link is held to the range
  of its own provider. `google` states a 32-bit seed and five stop sequences; `openai` (OpenAI's own service, not a
  compatible server) and `azure-openai` state four stop sequences; `anthropic` and compatible servers state none. The
  Google.GenAI client library does not check the seed: it narrowed `3000000000` to `-1294967296` and the run went on.
  The ranges are not part of `GET /api/models`.
- **A prompt-cache write is priced: it is the fourth term of a run's cost.** Anthropic bills a write to its
  prompt cache apart from plain input, by how long the entry lives (1.25 times the input price for five
  minutes, 2 times for one hour), and Tracon counted the written tokens at the plain input price.
  `RunUsage.CacheWriteInputTokens` and `RunUsage.CacheWriteLongInputTokens` now hold the tokens written with the
  short and the long time to live (both inside `InputTokens`, like `CachedInputTokens`), and
  `RunCost.CacheWriteCost` holds the charge, with the prices that produced it
  (`CacheWritePricePerMillionTokens`, `CacheWriteLongPricePerMillionTokens`). `RunCost.Total()` and
  `RunTreeCost.Total()` add four terms, and so do the cost quota, the webhook total, the statistics, the run
  tree, the read view `runs_v1` and the console. Set the prices with `CacheWriteCostPerMillionTokens` and
  `CacheWriteLongCostPerMillionTokens` on a catalog row (`Tracon:Providers:Anthropic:Models:*`) or a
  `Tracon:Pricing` entry (short keys `CacheWrite` and `CacheWriteLong`). A class with no price keeps its
  tokens in the full-price input charge, so a setup that sets none computes the cost it computed before, to the
  last digit, and no run falls to `Unknown`. `RunStatistics` gains the totals `CacheWriteInputTokens` and
  `CacheWriteLongInputTokens`. `Tracon.Anthropic` reads the split by time to live from the raw usage of every
  call, streamed or not; when an SDK or API leaves it out, the whole write counts under the time to live of
  `anthropic.promptCaching.ttl` (the short one when it sets none) and a warning is logged once.
- **`TraconUsageCounts`: provider-independent keys for cache writes.** A model provider that bills a cache
  write reports it by adding the tokens to `UsageDetails.AdditionalCounts` under
  `TraconUsageCounts.CacheWriteInputTokens` (and `CacheWriteLongInputTokens`, for a provider with a second time
  to live). Tracon reads them in one place, so every provider gets the same pricing.
- **`RunEventCustomTypes.OutputFiltered` (`tracon.output.filtered`): a run records when a provider's content filter
  stops an answer that already has text.** The run still completes with the partial answer. The payload holds the
  `provider` and never the answer, and it survives `RecordToolPayloads = false`. The agent run stream
  (`POST /api/agents/{name}/run`) sends the event as a `custom` frame right before `done`, next to
  `tracon.output.truncated`, with the same data `GET /api/runs/{id}/events` serves; a structured response gets the
  event too, and the structured-response validator still decides whether the document is usable. A filtered answer with
  no content at all still fails the run as `content_filtered`.
- `FakeModelProvider.CallsTool(name, arguments, inputTokens, outputTokens)` and
  `FakeModelProvider.CallsTools(inputTokens, outputTokens, calls)`: a turn that asks for tools reports its token usage,
  so a test of the token budget, the usage or the cost goes through the path a real provider takes.
- A skill script that the run tree's budget refuses writes a `script.denied` audit entry, as every other refused script
  does. The exception and its `run_budget_exceeded` identity are unchanged.

### Changed

- **The job worker's poll and MCP tool discovery wait on the registered `TimeProvider`, and read their interval again on every tick.** Both waited on the system clock whatever `TimeProvider` the host registered, so a test host that registered a fake clock could drive the worker's other timers (a job's time limit, its lease renewal, its cancel watch) but not the poll that leases the job, nor the refresh that lists the MCP tools. A host whose registered provider has manual timers (`FakeTimeProvider`) now advances them by `PollInterval` and `Tracon:Mcp:RefreshInterval`; a host on the system clock sees no difference. The interval is read from the options on every tick and pass, so an options monitor that changes `PollInterval` or `RefreshInterval` applies it to the next wait (a value a timer cannot take is ignored, and the loop keeps the interval it has). Nothing in Tracon reloads these options from configuration on its own.
- **The compatibility page now lists which platforms were run, and which were not.** *Verified platforms* gives, for PostgreSQL 12 to 18 (and 18 with the `tr-TR` ICU locale),
  SQL Server 2017, 2019 and 2022 (and 2022 with `Turkish_CI_AS`), SQLite 3.33 to 3.53 and the process cultures `tr-TR` and `de-DE`, the test project that proves the row, the result
  and the date; Windows, a Linux run on GitHub, PostgreSQL 11 and Azure SQL are listed as not verified. Two exceptions are written out: SQL Server 2017 applies every migration and passes
  all but two tests, which pin an error number that is 2019 and later; SQLite 3.35 applies every migration but fails two tests (a key revoked while its last use is written ends with
  `database is locked`; a stored `1e300` reads back one unit in the last place off) and 3.44 passes all of them; below 3.35 the migrations stop at `0030_job_handler_key`, which needs
  `ALTER TABLE … DROP COLUMN`. Nothing in the packages changed.
- **The run event stream tells a client that reconnects on its own how long to wait.** `GET /api/runs/{runId}/events` opens with a `retry: 3000` block, with or without
  `Last-Event-ID`, so every client of an instance that restarted waits the same, known time instead of the default of its own browser. The block carries no data and dispatches no
  event: `readSse`, the `Tracon.Client` stream readers and every reader that follows the SSE rules drop it. Before this release the `Tracon.Client` raw-frame reader
  (`<operation>StreamAsync`) yielded a block that only sets `retry:` as a frame of its own; it skips it now, like a keep-alive comment. The streamed answer of
  `POST /api/agents/{name}/run` and the workflow follow stream are not resumable and do not send it. The `: waiting` comment on every idle poll is unchanged.
- **A project pinned to `LangVersion` 9 can compile the files the source generator writes.** The wrapper the generator added for a `[TraconTool]` method used a file-scoped
  namespace, which is C# 10, so a project on C# 9 built against every Tracon package and failed on that one generated file (`CS8773`). It uses the block form now. The
  lowest language version the packages accept is 9 (the public API uses init-only setters); see *C# language version* in the compatibility reference.
- **The two SQL lease stores read their clock through `TimeProvider`.** `SqlSingletonLeaseStore` and `SqlJobStore` take the clock a host registers, as `SqlApiKeyStore`
  already did; a host that registers none keeps the system clock. Nothing changes by default. A lease is still stamped and compared with the clock of the node that
  runs the statement, and measurement on PostgreSQL 18 and SQL Server 2022 shows what that costs when two nodes disagree about the time: a single-executor lease is
  taken from its live owner only past two thirds of the lease in skew, and a **running job** is taken (as attempt 2, the first node's completion refused) by a node
  whose clock is ahead by more than the time left on the job lease. The measurement took a freshly leased job (five minutes by default); the owner renews every third of
  the lease, so the time left is between two thirds of the lease and all of it. Keep the clocks of every instance that shares a database synchronized.
- **The in-memory stores fold the tenant identifier the way the SQL stores do.** `Acme`, `acme` and `ACME` are one tenant in every store now, and the sessions, runs,
  definitions and other records the in-memory stores keep carry `acme`. Before, 28 in-memory stores compared the tenant as written, so a host that used them (the default of `AddTracon()`, `TraconTestHost`, a local job) saw
  a different tenant per spelling where the same code on PostgreSQL, SQL Server or SQLite saw one. The HTTP layer already resolved the tenant to the folded form, so a
  request through the endpoints is unchanged; the difference shows in code that calls a store with a tenant it spelled itself. The in-memory job store counts the leases of
  a tenant for `TenantCap` by the folded form, and its comparison no longer ignores case, since the stored value is already folded.
  A custom `ITenantContext` that returns a spelling the rule would change (`Acme`) now gets `acme` back in the records, where the in-memory stores used to return its
  own spelling; the services that compare a record's tenant with the context's as text (`ConversationBranchService`, `RunReplayService`, `ChildAgentInvoker`) refuse the
  record, as they already did with the SQL stores. Return the folded form from the context.
- **The knowledge-base errors name the package that supplies the store.** The `501` answer of the knowledge
  endpoints, the `TraconException` of the knowledge service and the compile error of an agent that asks for semantic
  search now say "the `Tracon.PostgreSql` package, `UsePostgreSql()`". The status code and the title are unchanged. The
  production warning about non-persistent storage and the warning about a session workspace on the in-memory file store
  name the package of each `Use*` call they suggest.
- **Every example reads the provider key from `Tracon:Providers:*`.** The XML examples, the package READMEs and the
  guides showed `Configuration["OpenAI:ApiKey"]`, a key nothing binds. They now use the section overload
  (`GetSection(OpenAIProviderOptions.SectionName)`) or the key `Tracon:Providers:OpenAI:ApiKey`. Pages that call
  `UsePostgreSql` now name the `Tracon.PostgreSql` package, and the root README no longer says "two lines".
- **Dependencies move to the newest releases of the runtime stack.** Microsoft Agent Framework `1.25.0` to `1.26.0`
  (Hosting `1.26.0-preview.261023.1`, Hosting.OpenAI `1.26.0-alpha.261023.1`), the .NET platform packages `10.0.13` to
  `10.0.14`, `Anthropic` `12.58.0` to `13.3.0`, `Google.GenAI` `1.26.1` to `1.27.0` and `Azure.Core` `1.63.0` to `1.64.0`.
  Anthropic 13 is the one that changes behavior; see Upgrading. The test and build tools stay where they are.
- **`SamplingFields` has two more members, `Temperature` and `TopP`,** so a provider can say that its API surface does not
  send them (the `anthropic` provider does not). `GET /api/models` and the configuration value of
  `RejectedSamplingFields` accept the two names as well.
- **`POST /v1/responses` reports a cut-off or filtered answer as `incomplete`.** Microsoft Agent Framework wrote
  `status: "completed"` (and `response.completed` on a stream) for every finish reason. An answer the model cut off at
  its output token limit or its context window now has `status: "incomplete"` and
  `incomplete_details.reason: "max_output_tokens"`; an answer a content filter stopped after it wrote text has the
  reason `content_filter`. The message item that was cut is `incomplete` too, and a stream ends with
  `response.incomplete` in place of `response.completed`, as OpenAI's own stream does. Only the answer's final model
  call decides, so a tool turn or an earlier loop iteration that hit the limit changes nothing, and the status does not
  depend on run recording. **Upgrade note:** a client that took `status == "completed"` to mean a whole answer now sees
  the truth for these answers. The OpenAI Python SDK's `stream.get_final_response()` now raises `RuntimeError` when the
  stream ends with `response.incomplete`, as it already does against OpenAI's own incomplete stream: read the `response`
  of the last event of the stream instead.
- **The `custom` frames of the agent run stream come from a list of three types.** `tracon.output.filtered` joins
  `tracon.output.truncated` (always) and `tracon.quota.threshold` (with `PublishThresholdToRunStream`). A client that
  reads frames by name and ignores an unknown `customType` needs no change; a client that assumed the list holds two
  types should handle a third.
- `FakeModelProvider.CallsTool(string, object? = null)` is now two overloads, `CallsTool(string)` and
  `CallsTool(string, object?)`. Existing calls compile and bind as before, and the compiled signature of the
  two-argument call is unchanged.
- The description of `GET /api/runs/{runId}/tools` and the documentation of `ToolInvocationRecord.Duration` no longer
  say that a run that does not stream has no duration (no schema change).
- Diagnostic and validation message text no longer names decisions, phases or documents. The `TRC0007` message, the
  instance-method error of `AddToolsFrom`, the two schema-name validators (`TraconPostgreSqlOptions.SchemaName` and
  `TraconSqlServerOptions.SchemaName`) and the descriptions of the `tracon.run.cost`, `tracon.judge.cost` and
  `tracon.judge.score` instruments say the reason in a clause instead. The codes, severities and metric names are unchanged;
  a test or an alert that matches the old message text needs the new text.

- **Documented: a change to the configuration needs a restart.** Tracon binds its options when the host starts and registers no `IOptionsChangeTokenSource`, so a configuration provider that reloads (a file with `reloadOnChange`,
  a vault) changes `IConfiguration` and not the options Tracon reads. Several readers look an option up on every call, which looked like live reconfiguration; it never was. The configuration reference says so now, and so does the
  XML documentation of `TraconOptions`, `TraconModelConcurrencyOptions` and the options that claimed a reload reached them (`TraconWorkflowOptions`' recovery settings, the workflow run timeout ceiling, the circuit breaker's
  `Enabled`). A host that registers its own change token source for a Tracon options type gets an unsupported half of it: the monitor sees the new value, the model concurrency limit keeps the semaphore of its first call, and
  a new value that fails validation makes every later read of that options type throw (every model call, for `Tracon:ModelConcurrency`). `ConfigurationReloadTests` pins all of it, so a release that supports reload has to
  decide each reader and change the documentation.

### Removed

- `Tracon.Abstractions`, `Tracon.AGUI`, `Tracon.Anthropic`, `Tracon.AspNetCore`, `Tracon.Azure`, `Tracon.Client`,
  `Tracon.Core`, `Tracon.Google`, `Tracon.Mcp`, `Tracon.OpenAI`, `Tracon.PostgreSql`, `Tracon.SqlServer`, `Tracon.Sqlite`,
  `Tracon.Testing`, `Tracon.Testing.Contracts.Xunit`, `Tracon.UI`, `Tracon.Voice`, `Tracon.Workflows`: the `net8.0` and
  `net9.0` targets. Microsoft support for .NET 8 and .NET 9 ends on 2026-11-10; the `1.0.0-preview.3` notes announced the
  drop for the first release after that date, and it no longer waits for it. Every package now targets `net10.0` only,
  and the meta package `Tracon` carries one `net10.0` dependency group.

### Fixed

- **In-memory state that is kept per tenant is removed when its window is over, so it no longer grows with the number of tenants a caller makes up.** The tenant identifier comes from a request, and
  six maps kept one entry for every identifier they ever saw: the hourly sampling budget and the score window of online evaluation (an entry whose hour is over, or that holds no score, counts for
  nothing), the in-memory counter behind `RateLimitScope.Deployment` (an entry whose window has ended restarts at one anyway), the slot counts of the live voice sessions and of the voice connection
  limit (the entry stayed at zero after the last slot was given back), the warnings the rate limiter writes when its counter store fails (one a minute for each partition), and the audit chain's
  key-off warning (one line for each tenant; it now names the first 1024 tenants and says once that more were left out). The limits themselves do not change: a window that is still open keeps
  counting, and a tenant is not given a second budget for the hour by the removal.

- **The AG-UI endpoint writes one text message for an answer a model streams in chunks that carry no message id.** A model provider that streams a word at a time without a message id on the chunks (the example host's echo model is one; so is a provider written against the minimum of `IChatClient`) produced a `TEXT_MESSAGE_START` ... `TEXT_MESSAGE_END` pair for every chunk, so the official AG-UI JavaScript client assembled seven assistant messages for one seven-word answer and a front end drew seven bubbles. A chunk without an id now belongs to the message before it, unless its role changes; a chunk that has an id keeps it, which is what a provider that sets one always got. Nothing changes for a front end that already read one message per answer. `Tracon.AGUI` only.
- **A skill script or a voice request still running when the host stops no longer ends with an `ObjectDisposedException`.** The script runner's concurrency limiter and the speech client's request limit were disposed with their owner, so the slot a running script or request gave back at its end met a disposed semaphore and the exception replaced the script's result or the request's response. Both limits are disposed with the last slot returned, as the job worker's and the workflow runner's are.
- **A workflow run that is cancelled while its row is being opened is closed `Canceled` instead of staying `Running`.** The write that opens a workflow run's row ran before the block that closes the run on every way out. A cancel that arrived after the store took the row and before the run's first event (the caller left, an operator cancelled, the run's deadline passed) ended the run with an exception that nothing caught: the row stayed `Running` and its trace span stayed open until run reconciliation (off by default) closed it as `Failed`. Agent runs were not affected. The write is inside the block now, and a cancel at any await point of a workflow run (run store, checkpoint store, event sink, agent turns) is swept by a test.
- **Disposing a `WorkflowRunner` no longer breaks the runs still in flight.** The runner's concurrency semaphore was disposed with it, so a run that ended after the host's container disposed the runner threw `ObjectDisposedException` from the block that also closes the run: the caller got the exception and the row stayed `Running`. The runner's slots are disposed with the last slot returned.
- **A retention sweep over every target no longer stops at the first target that fails.** A store that refused one table (or a policy that could not be read) ended the sweep, so the targets after it were never cleaned in that pass, and the job that retried the sweep stopped on the same target again. Every target is swept now and the failure is raised at the end: the failure itself when one target failed, an `AggregateException` naming the targets when several did. The job still fails and is retried. A stop still ends the sweep at once.
- **A telemetry lane that cannot be built no longer fails the run.** The run goes on without its spans, a `Warning` names the failure, and the next run tries again. The framework's telemetry agent only throws for a missing argument, so nothing is known to have reached this path; the guard keeps the rule that observability never breaks the work it observes.
- **A job worker no longer leaves a faulted task behind when the host stops while a leased job still waits for its previous attempt.** A job released for another attempt is leased again at once, and the new lease takes a slot and then waits for the previous attempt to finish on this worker. A stop in that window ended the worker's loop and disposed its slot pool before the waiting attempt returned its slot; the `Release()` met a disposed semaphore and the exception sat in a task nobody awaited (a unit test run of the host reported it as exit code 7). The slot pools are now disposed with the last slot returned, and the worker waits for every slot, one held by a waiting attempt included, before it ends.
- **The SQL stores return the canonical tenant in the workflow definitions, agent definitions, egress policies and attachment descriptors a call returns, whichever spelling the caller wrote.** `SaveAsync("ACME", ...)` returned `ACME` while the row held `acme`, and a read answered `acme`, so a caller that compared the returned tenant with its own as ordinal text got a different answer from a save than from the next read. On SQL the attachment store also handed the external content storage the caller's spelling (two spellings of one tenant became two places) and wrote the content before it refused a tenant outside the allowed characters, which left bytes no row points at. Both are fixed; the in-memory stores already returned the canonical form. A custom store passes the new contract tests listed above.
- **A job enqueued and a webhook delivery created under another letter case of the tenant return the canonical tenant.** `SqlJobStore.EnqueueAsync` and `SqlWebhookStore.CreateDeliveryAsync` built the returned record from the spelling the caller wrote, as the four stores above did; the next read answered with the folded form. `JobStoreContract.A_job_queued_under_another_spelling_is_returned_and_read_under_the_canonical_tenant` and `WebhookStoreContract.A_record_written_under_another_spelling_is_returned_and_read_under_the_canonical_tenant` are new contract tests a custom store has to pass.
- **A quota consumption whose agent name is empty is counted once.** The empty name is the tenant-wide counter; the SQL stores and the in-memory store added the consumption to "the agent's counter" and to the tenant-wide counter, which are one row, so every run of it counted twice. New contract test `QuotaStoreContract.A_consumption_that_names_no_agent_is_added_to_the_tenant_wide_counter_once`.
- **On SQL Server a webhook subscription to `run.completed` is no longer found for `Run.Completed`.** The event search compared the JSON array element under the column collation; the other stores matched exactly. New contract test `WebhookStoreContract.Event_name_matches_by_its_characters_letter_case_included`.
- **The in-memory quota, webhook, retention policy, run score and run stores no longer lose a concurrent writer.** Each looked a record up and wrote it in a second step: writers of one scope each added a record (quota rule, webhook subscription, retention policy, run score), and two writers of one run each wrote back a copy missing what the other knew (the user or the labels of a run started twice). A save over a quota rule or a webhook subscription also replaced the creation time, where the SQL upserts keep it. A custom store passes the new contract tests `QuotaStoreContract.Saving_over_a_rule_keeps_its_identifier_and_its_creation_time`, `WebhookStoreContract.Saving_over_a_subscription_keeps_its_identifier_and_its_creation_time`, and the concurrent-writer tests (`Writers_that_save_one_scope_at_the_same_time_leave_one_rule` and its siblings in the webhook, retention policy, run score and run contracts).
- **A provider that quotes a tenant's own key back in its error text no longer puts the key into a log line, a span status or the stored trace.** When a call is made with a tenant's own provider key (BYOK) and the provider's error body quotes that key, the SDK puts the body into the exception it throws. Five loggers wrote that exception with its inner exceptions (`Microsoft.Extensions.AI.OpenTelemetryChatClient`, `Tracon.ModelProvider`, `Tracon.RunRecordingAgent`, `Tracon.AgentEndpoints`, `Tracon.OpenAICompat`), the `chat` span carried it as its status description, and with `Observability.RecordSensitiveData` on the trace store kept it, so the key reached the database. Tracon now replaces the key the call was made with by `[redacted]` in all of those: the exact value, as it is, escaped for JSON and encoded for a URL. The rest of the provider's text, the exception types, the HTTP status and the stack stay. Retry, fallback and the circuit breaker still decide from the original exception. **Not covered, and stated on the *Observability* page:** the shared key you register at startup (`UseOpenAI(apiKey)` and the others; a provider that quotes it still puts it into the log, and into the trace store when `RecordSensitiveData` is on), a part of a key that the provider quotes itself (OpenAI shows the first eight and last four characters of a refused key), a key shorter than eight characters, a log writer that serializes inner exceptions property by property, and the errors of speech and live voice providers.
- **A value too long for its SQL Server column answers `400 Value too long for the store`, not `503 Persistence store unavailable`.** `PUT /api/tenants/{slug}`, `POST /api/approvals/rules`, `POST /api/runs/{runId}/feedback`, `PUT /api/evals/{name}` and `PUT /api/mcp-servers/{name}` each stored a field their own validation did not bound. See *Upgrading*.
- **An agent run no longer leaves a telemetry `Meter` behind.** With `Observability.Enabled` on (the default), every `IAgentCatalog.ResolveAsync` built a new Microsoft Agent Framework `OpenTelemetryAgent`, and nothing disposed it. Each one made a `Meter`, an `ActivitySource` and four histograms, and the runtime keeps a meter in a process-wide list until it is disposed, so the live managed heap grew by about 6.5–8.2 KiB per run with the tenant count fixed (three soak runs, 9 of 9 plateaus rising; the same load with `Observability.Enabled = false` stayed flat). The agent is no longer built per run. A pool of lanes lends one telemetry agent to a run for the length of the call and takes it back when the call or the stream ends, however it ends. A lane is built for what the chat client says about itself (provider, default model, host and port of the endpoint), because the framework's agent reads those once and writes them on every span and metric, so a run borrows a lane of its own identity. The number of meters is, for each such identity, the runs of that identity in flight at the busiest moment, and no longer the number of runs. The `invoke_agent` span, its attributes and the `gen_ai.client.*` metrics are unchanged.
- **`?bucket=` with a number that names no member answers `400` instead of `500`.** `GET /api/evaluation/scores/summary` and `GET /api/stats/timeseries` accepted `?bucket=99` (the binder takes any integer for an enum) and a store `switch` threw once any row existed.
- **`POST /api/runs/{runId}/replay` and `POST /api/sessions/{id}/branch` answer `400 Agent compilation failed` when the agent cannot be compiled** (a tenant provider binding whose configuration key has no value, an egress policy that no longer allows the provider), as running the agent does. Both answered `500`.
- **`PUT /api/quotas` refuses a `maxCost` of 10 000 000 000 or more with `400`.** The money columns hold ten digits before the point; PostgreSQL and SQL Server answered `503 store unavailable`, the in-memory and SQLite stores kept the value.
- **A free-form JSON field set to `null` is stored as `[]`** (`EvalSuite.Checks`, job and schedule payloads, case scores). SQL Server's `ISJSON(N'null')` is `0`, so `PUT /api/evals/{name}` with `"checks": null` answered `503` there.
- **The OpenAI-compatible conversation responses always carry `metadata`, `first_id` and `last_id`** (`null` when empty), as the published schema requires.
- **A blank (whitespace-only) `model`, `metadata.entity_id`, OAuth callback `state` or MCP tool name is refused like an empty one.** `{"model": " "}` on `/v1/chat/completions` and `/v1/responses` and `?state=%0C` on the MCP OAuth callback answered `500`.
- **A streamed agent run whose caller leaves right after the last frame is closed `Canceled`**, not left `Running` with its trace open. The closing writes of a streamed run ran after the last frame with the caller's own token; a client that disconnected in that window made the store refuse the write. The buffered path already closed such a run `Canceled`.
- **Orphan run reconciliation no longer drops the continuation of the runs claimed after a run whose continuation failed.** The claim had already closed every row, so a run the loop skipped was not orphaned any more and no later scan looked at it.
- **A live voice session whose history store fails still writes its session record, and a shutdown closes every open voice session.** A history write that failed with an exception the writer does not name skipped the record, the only trace a session that produced cost leaves; one session whose close threw stopped the shutdown loop, and kept its slot at the tenant's limit for the life of the process.
- **The model provider circuit breaker measures its break on the monotonic clock.** A wall clock stepped back one hour kept a provider rejected one hour longer; a step forward closed the break early.
- **The quota usage gauge no longer freezes when the rules of one tenant cannot be read.** The failure left the loop and the whole cache kept its last value; the failing tenant now keeps its previous samples and the others update.
- **The periodic background loops and the single-executor guard use the registered `TimeProvider`.** Five loops (approval expiry, canary evaluation, model provider health, run heartbeat and run reconciliation) created their timers on the system clock, and so did the single-executor guard of those loops and of workflow request expiry, so a host that registers a fake clock could not drive them.
- **`GET /api/runs/{id}/events` with `Last-Event-ID: 9223372036854775807` no longer replays the whole run**; the stream ends like any resume past the last event (the next id overflowed to the smallest `long`).
- **`POST /v1/chat/completions` with `stream: true` honors `stream_options.include_usage`.** One chunk with an empty `choices` list and the usage of the run follows the finish chunk, before `[DONE]`. The OpenAI .NET SDK sends the option on every stream and used to receive no usage.
- **`POST /v1/responses` answers (JSON and stream) carry `model`**, the name of the agent, as OpenAI's API always does; an OpenAI client read `null`.
- **Event-stream responses send `Cache-Control: no-cache,no-store,no-transform`.**
- **SQL stores: `IApiKeyStore.CreateAsync` and `IRunStore.StartRunAsync` return the tenant in the folded form the row holds**, as every read does.
- **SQL idempotency store: a reservation that races the release of the same key no longer fails with a `TraconException` (a `500` for the client).** The row that made the insert conflict can be gone when it is read; the reservation now starts again, up to five times.
- **SQL audit log: the hash chain is computed over the entry as the row stores it** (tenant folded, `U+0000` replaced), so a chain written under another letter case of the tenant, or with a NUL character in a field, verifies. Chains written before with such input still report `Broken`, as they did; `VerifyChainAsync` lists the first failing entry.
- **`TRC0102` no longer reads the `Provider` property of a type that is not a model binding** (`ToolCallUsage { Provider = "openai" }`); only `ModelBinding` and `ModelFallback` count. A library consumer built with warnings as errors failed on it.
- **A read-only working tree no longer adds ten seconds and twenty warnings to a build that writes the `AGENTS.md` copy.** The copy retried a write that cannot succeed ten times; it does not retry now.
- **A consumer's directory named `AGENTS.md` or `.claude/skills/tracon/SKILL.md` no longer stops the build with `CS1504`.**
- **`IRunInputStore.GetAsync` on PostgreSQL, SQL Server and SQLite returned the spelling the caller asked with in `RunInputRecord.TenantId`**, not the folded form the row is stored
  in: a read for `TEST` returned a record that said `TEST`. The row found was the right one, since the query folds the tenant; only the field the record was built from was not.
- **Options with a fraction or a limit stop the host at start when the value cannot mean what it says.** `Observability:SuccessSampleRatio`, `Preflight:ReserveRatio` and `OnlineEvaluation:SampleRate` refuse a value outside 0 to 1 and NaN (`10` used to persist every span); `ModelConcurrency:MaxConcurrentCallsPerProvider` below 1 is refused instead of being raised to 1, so a `0` meant as "no limit" no longer serialises every provider. Limits that had no range rule now name the key and stop the host: Knowledge (chunk size and overlap, dimensions, results; they used to fail at the first upload), Idempotency, AsyncRun (a `MaxAttempts` of 0 failed every queued run without running it), Approvals, Webhooks (a negative `SignatureTolerance`, `MaxExtraHeaders`), the Voice limits and durations, and `Workflows:MaxSuperSteps` below 1 (every run stopped at step 1 with a message about a loop that did not exist). The Skills limit messages name the key. Checked under the invariant, tr-TR and de-DE cultures: the binder reads numbers invariantly, so `0,5` is refused in every culture and never read as 5.
- **The workflow editor refuses an hour above 23 and a minute or second above 59 in a timeout field**, as the server does now (`48:00:00` passed the screen and the server then refused it with a message that named a .NET type).
- **The resource and prompt lists of an MCP server are read page by page too** (see the limits above): a server that never ended either list filled memory, as the tool list did.
- **The tool list of an MCP server is read page by page and stops at `Tracon:Mcp:MaxToolsPerServer`.** The client collected every page before the limit was looked at, so a server that kept handing out a next cursor filled memory until the connection timeout (3 GB in ten seconds) and a list of 30 000 tools held 112 MB for a refresh that kept 100. The read stops at the first tool past the limit, logs the same warning and keeps the first tools; a server that sends nothing but tools nobody can use still costs one connection timeout. A single page is still read whole. A tool with a header annotation (`x-mcp-header`) that is not valid is left out, as before, and a call still sends the `Mcp-Param-*` headers its list declared.
- **An `Idempotency-Key` request no longer copies a body that declares no JSON content type.** The idempotency filter read and copied every keyed body about five times before the endpoint refused or ignored it (a 29 MB body with no `Content-Type` cost 151.8 MB; the routing already answers `415` for a wrong type, but not for none). It now reads only a JSON body, bounded by `MaxJsonBodyBytes`, and copies it once (a 7.9 MB keyed JSON body cost 39 MB, now 8 MB). The fingerprint is unchanged.
- **A store that cannot answer is `503` whatever the driver calls it.** A connection pool that ran dry (SqlClient raises `InvalidOperationException`) or a connect that timed out (`TimeoutException`) was wrapped by the agent catalog as `400 Agent compilation failed` and answered `500` elsewhere; one shared check (a `DbException`, a timeout, an exhausted pool) now maps every store failure to `503`. The callers of the catalog (jobs, workflows, A2A, MCP) see the raw exception, as they already did for a `DbException`; a queued run whose first attempt hits one is retried.
- **An attachment upload or list, a speech clip and a generated image answer `400` to a `sessionId` longer than 200 characters or with surrounding whitespace**, as the run endpoint does. A 201-character id was a `500` on SQL Server.
- **PostgreSQL migration `0038` keeps the same row when two provider spellings were written at the same instant, whatever the locale of the database.** Its comparison of equal timestamps followed the database locale. The migration changed in place, with the same recreate cost as the other migrations changed in this round.
- **A provider's `Retry-After` above 60 seconds is no longer obeyed.** The client libraries behind OpenAI, OpenAI-compatible, Azure OpenAI and Anthropic waited as long as the header said, with no ceiling: a day-long value held the run until the caller gave up, and a value past about 49 days or a far-future HTTP date made the retry loop fail with `ArgumentOutOfRangeException` instead of the 429. Such a header is now ignored and the client's ordinary backoff applies, so the run fails with the provider's 429 or falls back to the next model. A delay of 60 seconds or less is obeyed as before.
- **A workflow run that was answered no longer ends without its output now and then.** After a human answer the run set the answer going and then asked the framework for the status of the run
  to decide whether to watch it again. The work the answer released can be over within a millisecond, and then the status said the run was idle and the output event, which the framework had
  already queued, was never read: the run was recorded as completed with no output (about one answer in a hundred under load). The runner now watches again when the run is idle and nothing
  was read since the answer. Measured against the framework alone, with no Tracon code, before the change.
- **One schedule the job worker cannot dispatch no longer stops the schedules behind it.** The worker walks the due schedules of every tenant in one
  list, a store gives that list in no particular order, and an exception thrown for one row left the loop, so every row behind it waited for
  the next tick and, when the row failed the same way each time, for as long as it stayed. The failure is now caught per row, logged with the
  schedule's name, and the other due schedules dispatch. A cron expression that never fires was the one way to reach this and `CronExpression`
  already refuses it; the row-level catch is what keeps any other failure from coming back as the same outage.
- **The script concurrency limiter no longer keeps an entry for every tenant identifier it has seen.** The per-tenant slot was created on first use and
  never removed, so the memory it held grew with the number of distinct tenant identifiers. With header-based tenancy and no
  `AllowedTenants` list, a caller chooses the identifier. A tenant is now tracked only while a script of it holds or waits for a slot.
- **The MCP OAuth authorization flow no longer carries the request that started it.** The flow keeps running after the request answers, for as long as the
  administrator takes to consent, and it was started inside the request. Its tenant, audit actor and trace therefore stayed attached to a callback of the
  SDK long after the request ended. It now starts on a clean execution context, as the connection to an MCP server already did.
- **A store that is down while an agent is compiled answers `503`, not `400 Agent compilation failed`.** Reading a tenant's provider binding or egress
  policy happens while the agent is compiled, and the catalog wrapped any exception it did not recognize as "the agent source failed". A
  database error therefore told the caller their agent was broken, and the caller went to fix an agent that was fine. A `DbException` now
  passes through the catalog unwrapped (it is still logged and counted on `tracon.agent_source.failures`), and the host answers `503` as it does for any
  other store call. An exception that is not a database error is wrapped as before.
- **One failed write of a run's record no longer leaves the run `running`.** Run recording gives up on its first store failure, and that
  included the closing write: a dropped connection or a deadlock victim on any event left the record unfinished after the caller had its
  answer, and only reconciliation (off by default, and five minutes late when on) ever closed it. The closing write is now tried again
  after a short pause, whether an earlier write was lost or the closing write itself failed. Events are not retried: a run that lost one
  still loses the stream after it, and `tracon.run.recording_failures` still counts the run once. A refusal that is the store's own verdict
  (no such run, another tenant's run) is not asked twice.
- **A quota period ends at the right instant in zones that change the clock at local midnight.** Paraguay, Chile and Greenland start
  daylight saving time at midnight, so that midnight never shows on a clock. The end of the daily and monthly period (the instant a
  refused call is told to retry at) was computed from the offset after the change and came out an hour early: still inside the
  old date. The instant is now checked against the zone's own conversion, the one the counter's date key uses. What a call is
  counted against was not affected; only the reported end of the period.
- **A token count below zero from a model provider is dropped, not priced.** A provider, a proxy in front of one, or a compatible server of
  your own can report `-5` prompt tokens. The count was recorded as it came, and a price times a negative count is a negative cost,
  which the quota, the budget and the cost total read as money handed back. It is now treated as not reported, the same state a provider
  that sends no usage leaves; the embedding usage of a knowledge search and the token counts of an image generation ignore a negative
  count too.
- **A request without a JSON `Content-Type` answers `415`, not an empty `500`.** Thirty-three operations read their body by hand and let
  the framework's "not a known JSON content type" exception out, so `curl -X POST .../api/agents` with no body at all, or with
  `Content-Type: text/plain`, was a `500` that named nothing. They now answer the `415` that the definition import and the AG-UI
  endpoint already gave, as `application/problem+json`; the two OpenAI-compatible routes (`/v1/chat/completions`, `/v1/responses`)
  answer it in their own error shape. A body with the right content type but no content is still the `400` it was.
- **A `null` inside a list or a map of a request body is a `400` that names the property.** The API already refused `"approvals": null`
  for a field that is not nullable, but never looked inside a collection: `{"nodes": [null]}` on a workflow and `[null]` on the eval
  cases reached the handler as a list holding a null and failed with a `500`. The rule is written once, from each property's nullable
  annotation, so it covers every collection in every request: `string?` and `int?` elements still take a null.
- **An eval check definition that cannot be read is a `400`.** A `checks` entry that is a number, an array or `null`, a `minLength`
  that is not a whole number, and a `values` or `tools` list that holds anything but strings made the suite save fail with a `500`.
- **A path segment that is only whitespace answers `404`.** Twenty-six operations (sessions, workflows, schedules, triggers, webhooks,
  tenants' egress, tools and providers, MCP servers, conversations) handed it to a store whose argument guard refuses a blank name,
  and the guard's exception was a `500`. Nothing is stored under a blank name; operations that already checked it keep their answer.
- **A quota period ends at the right instant where a zone skips a whole date.** Kiribati (1994), Samoa and Tokelau (2011) moved across
  the date line and have no 1994-12-31 or 2011-12-30. The end of the daily period before the missing date was computed twenty hours
  early, because the search for the first instant of the next date gave up after four hours. It now looks as far as a missing
  date needs. Only instants in those zones and years were affected; what a call is counted against was not.
- **The egress guard no longer lets a private address through when it is written as an IPv4-compatible IPv6 address ending in
  `.1`.** The guard decodes the IPv6 notations that carry an IPv4 address (mapped, compatible, translated, 6to4) and judges the
  decoded address. The compatible form skipped every address whose last byte is 1, which was meant only for `::1`, so
  `::192.168.1.1`, `::10.0.0.1` and `::127.2.173.1` were judged public. The form is deprecated and an ordinary stack does not
  route it, so the reach was narrow; the verdict now matches the IPv4 address in every notation. Webhook, MCP and model provider
  targets all go through this check.
- **A bracket in a file path or glob matches itself on SQL Server.** SQL Server reads `[abc]` in a `LIKE` pattern as a character
  class, and the escape the agent file store used covered `\`, `%` and `_` but not `[`. A search under `/n[1]` also found the
  files under `/n1`, and a glob of `[a].md` matched `a.md`. The store now escapes the opening bracket too; PostgreSQL and SQLite
  take the escaped bracket as the bracket itself. The new contract test is
  `AgentFileStoreContract.A_bracket_in_a_directory_or_a_glob_matches_itself_only`.
- **A list that is paged by offset returns each row once when several rows share the sort time.** The session, attachment, audit
  log, job, eval run, webhook delivery, retention run and voice session lists were ordered by one time column. Rows that share
  the time have no order of their own, and PostgreSQL may order them differently for the page that starts at 0 and the page that
  starts at 7: a session was listed on two pages and another on none (45 sessions updated at one instant, pages of 7: it failed
  every time). A batch that writes many rows at one instant is the ordinary way to get such a tie. The eight queries of each SQL
  provider now sort by the time and then by `id`. The new contract test is
  `SessionStoreContract.Paging_through_sessions_with_the_same_update_time_returns_each_one_once`.
- **`tracon health`, `eval`, `export` and `import` warn when the token would travel over plain `http` to another machine.** A
  bearer token is a credential for the whole installation, and an `http://` address on another machine sends it where anyone
  on the path can read it. The command now says so on standard error and still runs, because a plain-http address inside a
  trusted network, or behind a proxy that ends TLS, can be a decision. Loopback and `https://` addresses do not warn, and the
  token is never part of the message.
- **A call to a callable agent no longer leaves a timer and a token registration behind for each update the child streams.**
  The wait raced the child against a timer for the whole wait limit and against the caller's token, and never cancelled either
  one when the child won. A child that streamed 200 updates left 200 timers running until the limit (201 created, 201 running
  when the call ended, measured) and 200 registrations on the caller's token until the run ended. The online eval judge raced the
  same way, once per judge. The loser is now cancelled as soon as one task wins.
- **Two writes of one run's input that arrive at once no longer fail on SQL Server.** A second write of a run's input is ignored. The
  SQL Server statement checked "is there a row" and inserted in two steps without a lock, so two writers both found none, both
  inserted, and the loser failed with a duplicate key (24 writers at once: it failed every time). The lookup now holds an update
  lock on the key range. PostgreSQL and SQLite already ignored the second write. The new contract test is
  `RunInputStoreContract.Writes_of_one_run_that_arrive_at_once_are_all_ignored_but_the_first`; a custom store that checks and
  then inserts without a lock fails it.
- **A currency label is stored in upper case by the SQL stores.** The cost queries upper-case the currency column in SQL so that
  `usd` and `USD` are one currency, and that function follows the locale of the database: under a Turkish locale
  `UPPER('inr')` gives a dotted capital I (U+0130) before `NR` while `INR` stays `INR`, so one currency became two and the cost total of a run tree left the amount
  out. The label is now folded to upper case, in the invariant culture, when it is written (run cost, tool call and late tool
  usage, non-token cost, voice session cost), so the query's `UPPER` finds nothing to change in any locale. The in-memory
  stores already count the labels without regard to case.
- **An instance stops acting as the single executor once it cannot prove its lease for a whole lease duration.** The guard of
  `Tracon:SingletonExecution` kept reporting that it held the lease after a renewal threw, and kept doing so until a renewal
  worked again. An instance cut off from the database therefore went on running the periodic work (reconciliation, approval
  expiry, canary evaluation, workflow request expiry, the model health probe) after another instance had taken the lease over.
  Now the lease counts from the start of the last renewal the store confirmed, and `IsHeld` turns false by itself when
  `LeaseDuration` has passed, also while a store call hangs. One failed renewal still does not give the lease up: it is inside
  the margin a third of the lease leaves.
- **A session identifier over 200 characters is refused with `400` on the run and Responses endpoints.** The `sessions` table of
  SQL Server holds `nvarchar(200)`, so `POST /api/agents/{name}/run` with a longer `sessionId`, or `POST /v1/responses` with
  a longer `conversation` or `previous_response_id`, ended in a truncation error and a `500` there while PostgreSQL, SQLite
  and the in-memory stores accepted it. The AG-UI endpoint already refused it; the three surfaces now agree.
- **`GET /api/jobs` and `GET /api/evals/{name}/runs` clamp `skip` and `take`, and a knowledge search refuses a `top` below 1.**
  The two lists passed both values to the store as they came: a huge `take` returned every row, and a negative `skip` is an
  error in the `OFFSET` of PostgreSQL. They now clamp like the other list endpoints (`skip` at 0 or more, `take` from 1 to
  200). `POST /api/knowledge/{collection}/search` sent a `top` of 0 or less to the vector store as a `LIMIT`, where
  PostgreSQL refuses a negative one and the host answered `500`; it now answers `400`. A larger `top` is still allowed.
- **The items of a conversation never list the message of the exception a tool threw.** `GET /v1/conversations/{id}/items`
  listed `Exception.Message` as the output of a failed tool call when the message history held the exception object. A
  stored session does not (the object is not serialized), but a custom `ChatHistoryProvider` that keeps live objects returns
  one, and its message can carry a connection string or a path. The output is now the result the model was given in its
  place, or empty; the item is still `incomplete`.
- **`POST /v1/conversations` reads at most 65 536 characters of body.** It read the whole body into a string, so the only
  limit was the server's (30 MB for Kestrel by default) for a body that holds sixteen short `metadata` pairs. A larger body
  answers `400` ("Body is too large") without being parsed.
- **A tenant taken off `AllowedTenants` stops its API keys too.** The list was judged for a tenant named by the header or the
  claim, and an API key skipped it: a key bound to a tenant that was not on the list kept working on every endpoint and on the
  voice conversation socket. The key's tenant is now judged with the same rule and the same canonical form, and a key of a
  tenant outside the list answers `403` ("Tenant rejected"). A list that is empty, or tenancy that is off, changes nothing.
- **A U+0000 character in a value no longer fails the write on PostgreSQL.** PostgreSQL cannot hold U+0000 in a `text` value
  (`22021`) or the escape `\u0000` in a `jsonb` value (`22P05`), and SQL Server, SQLite and the in-memory stores accept
  both, so a model reply, a tool result or a request body that carried one wrote fine on three stores and failed on
  PostgreSQL: an event's text, a job's payload and item input, and a schedule's payload were refused (four of six writes
  measured). Every SQL store now writes U+FFFD in its place, and in a JSON text the escape `\u0000` becomes `\uFFFD`; an
  escaped backslash followed by the characters `u0000` is left alone. The in-memory stores keep the value as it came.
  Three contract tests check that a store accepts it, keeping the value or replacing it:
  `RunStoreContract.An_event_whose_text_holds_a_NUL_character_is_stored`,
  `JobStoreContract.A_job_whose_payload_and_input_hold_a_NUL_character_is_stored` and
  `JobScheduleStoreContract.A_schedule_whose_payload_holds_a_NUL_character_is_stored`. A custom store that refuses the
  character fails them.
- **A fallback chain's shared tool ledger recognises a call whose arguments arrive in another order.** The ledger keeps a tool
  call that completed on one link, so the next link does not run it again. It compared the arguments as text in the order the
  model wrote them, so a charge that ran on the primary link ran again when the fallback model listed `amount` before
  `customer`. The same tool with the same argument names and values, in any order, is now answered from the ledger; a
  different value, or the same number written another way, is a new call and runs. The ledger of a replay or a continuation
  still compares the text as it was recorded.
- **One tenant's silent MCP servers no longer hold every tenant's refresh.** A refresh connected the servers one after another
  while it held the catalog's refresh gate, so each server that accepted a connection and never answered added its
  `ConnectionTimeout` (30 seconds by default) to the manual refresh, to the end of every OAuth authorization, and to the
  background discovery of all tenants. Three such servers made a 2-second timeout last 6 seconds; the pass now takes about 2.
  At most eight connections are tried at the same time.
- **A definition package with a long chain of references no longer ends the process.** The import (a dry run included, which
  the Operator role may call) ordered agents with a recursion as deep as the longest chain. A package of about 8 000 agents,
  each naming the next as its shared instructions and well under the 8 MB limit, overflowed the stack, which cannot be caught.
  The ordering is iterative now.
- **A skill script that never reads its standard input no longer outlives its timeout.** The runner writes the arguments to the
  script's standard input. A script that did not read them blocked the write once the arguments outgrew the pipe (4 MB
  measured), and the timeout came out as an `OperationCanceledException` before the process was killed, so the script kept
  running. The write is now inside the guard that kills the process.
- **The quota period ends at the earlier midnight on a day a zone repeats it.** In a zone whose midnight can occur twice
  (Havana, Santiago) the period end, and so the `Retry-After` of a refused call, was an hour late on that one day of the year.
  What a call is counted against was not affected.
- **`Tracon:Observability:RecordSensitiveData=false` did not keep the system instructions and every tool call's arguments
  and result out of the span store when the host had turned on the content switch of its own exporter.** A host that set
  `OTEL_INSTRUMENTATION_GENAI_CAPTURE_MESSAGE_CONTENT=true` found `gen_ai.system_instructions`,
  `gen_ai.tool.call.arguments` and `gen_ai.tool.call.result` in `GET /api/runs/{id}/trace`, because the store filter kept
  only keys that do not contain `message`, `prompt` or `completion`, and those three keys contain none of them. The
  filter now drops them and the framework's `executor.input` and `executor.output`; token counts stay. A host that never
  set the variable was not affected. Spans already stored keep what they were written with: a host that ran with the
  variable on should review its retained traces.
- **The run trace of a workflow held its root span and nothing else.** The framework runs a graph on a task it starts, and
  that task carried no ambient span, so every agent, chat and tool span of every step was a root of its own trace and
  no run owned it. They are now spans of the workflow's run.
- **A server's tools are listed on every instance when singleton execution is on.** See the first entry under "Upgrading".
- **A token is forgotten when its server is deleted.** The refresh that notices a deleted server removes the token this
  process held a cache for (in the distributed cache, the shared entry); a disabled server keeps its token, and a tenant the
  refresh did not read is not treated as deleted. Before, a token stayed in memory until the process stopped.

- **The documentation of `RateLimitPartitionKind.Global` no longer claims a limit shared across the whole deployment.** It
  was shared by every caller of one instance only; the whole deployment shares it with `Tracon:RateLimit:Scope` set to
  `Deployment`, which is new in this release.

- **The OAuth authorization of an MCP server completes.** `POST /api/mcp-servers/{name}/oauth/start` opened the
  provider's consent page, but the callback always answered "The authorization session was not found or has expired"
  and no token was obtained. The MCP SDK puts a `state` of its own into the authorization address and refuses an answer
  that carries another; the provider sends that value back, and Tracon looked the flow up by a `state` it had made
  itself. The flow is now found by the SDK's value, and the `state` that `oauth/start` returns is that value.
- **An MCP server whose endpoint or OAuth client id changes no longer sends its old access token to the new endpoint.**
  The SDK sends a valid cached token to whatever endpoint it connects to, and Tracon kept the token under the server's
  name. A changed endpoint or client id now starts with no token, and the server asks for authorization again.
- **`DELETE /v1/conversations/{id}` deletes the attachments of the conversation.** It deleted the session and the copy
  of its content that conversation memory keeps, and left the files uploaded to it: nothing could name them any more, so
  they stayed until retention. `DELETE /api/sessions/{id}` already deleted them; both endpoints, and the Microsoft Agent
  Framework session store that Tracon registers, now delete through one path. The order is the copy of the content, the
  attachments, then the session record, so a step that fails leaves the record in place and a repeat call finishes.
  `DELETE /api/sessions/{id}` used to delete the record first and the attachments after it: a failure in between left
  attachments no later call could reach.
- **The agent editor of the console no longer rounds a seed it cannot hold exactly.** A seed above 9007199254740991 in
  absolute value went out as another number (`9007199254740993` as `9007199254740992`), and a stored seed of that size
  came back rounded and was written back the same way. The editor checks the text before it makes a number: it shows the
  error in English and Turkish, marks the field invalid, and keeps Validate, Save and the Try again button of a failed
  request off until the seed is replaced or cleared. The HTTP API still takes a 64-bit seed.
- The tool rows and the tool events of a run that does not stream (a queued run, a run that continues an approval, an
  agent step of a workflow, the non-streaming form of the `/v1/*` endpoints) survive a failure. Before, a run that
  ended in an exception wrote no row and no event for the calls it had made: the budget refused its next model call,
  the provider failed, somebody cancelled it, and `GET /api/runs/{runId}/tools` was empty exactly when the trail was
  needed. A call that goes through the tool chain is now noted in memory where it happens, and the run writes it when
  it closes after a failure or a cancellation. A run that answered keeps the rows its messages hold, so a call that
  only the chain noted (a fallback model link was answered from the result of the first) gets no second row. A process
  that stops while a run is open leaves no row for these calls. A hosted tool, the framework's own tools and an agent
  called as a tool are still read from the messages of the answer.
- A tool row of a run that does not stream carries its `duration`, and its `createdAt` is the moment the call settled.
  Before, `duration` was empty and every row of the run carried the moment the run was written.
- **A replay that plays tools back or runs none no longer runs the harness's web search.** `ReplayTools` and `NoTools` took a
  hosted tool out of the tool list by its name, but `HarnessSettings.EnableWebSearch` turns the same provider-run tool on
  without a name, so the replay of a harness agent searched again and the search was billed again. Both modes now turn it
  off; `LiveTools` keeps it.
- **`EvalExpectedToolCall.Arguments` keeps a `default` `JsonElement` as a `null` value in every store.** The SQL stores
  already wrote it as `null`; the in-memory store kept the value that cannot be serialized, so a response that carried it
  failed to write.
- **`toolCallArgsMatch` still compares an object or an array argument for an eval item that an `IEvalEvaluatorFactory`
  copied.** A copy lost the exact form of the expectation, the expected object arrived as its JSON text, and the call
  carried the object, so equal values reported `args mismatch`. That path now compares the text, as the framework's own check
  does: key order and white space decide there.
- **`UseOpenAISpeech` with `UseAsDefault` says so when the default stays with another registration.** The default is
  registered with `TryAdd`, so a host that called `UseVoice` first kept speaking with ElevenLabs while the option said
  OpenAI, and nothing was written. A warning now names it at startup; call `UseOpenAISpeech` before `UseVoice`, or leave
  `UseAsDefault` off. The registration rule is unchanged.
- **The AG-UI endpoint checks the client-side tools of the definition the run takes.** It read the current definition before
  the experiment assigned a version, so an experiment arm that carried a client-side tool the front end had not declared
  started a run that waited for a result nobody could produce, and a current version that carried one the arm did not
  refused a run that needed nothing. The check now runs inside the turn preparation, with the assigned version.
- **A callable agent in tool mode can no longer take the name of a task list, agent mode or file memory tool.** The rule
  checked the agent's own tools, skill tools, `search_knowledge` and the session workspace, but not the tools a harness agent
  has by default (`todos_*`, `mode_get`, `mode_set`) or file memory adds (`file_memory_*`). An agent named `todos-add` made two
  tools of one name and the model provider refused the request with `400`. Validation and the compiler now report it.

- **The A2A agent card gives its interface address as an absolute URL.** `supportedInterfaces[].url` was the path (`/tracon/a2a/support`), built once when the endpoint was mapped. A client that resolves it against the card's own URL
  worked; the official A2A JavaScript SDK (`@a2a-js/sdk` 1.3.0) builds its transport from the value as written and failed on its first call with `Invalid URL`. The card is built per request now: the scheme, host and
  path base the request arrived on (`Request.Scheme`, `Request.Host`, `Request.PathBase`), so a host behind a reverse proxy that runs `UseForwardedHeaders` (`XForwardedHost`, `XForwardedProto`) and `UsePathBase`
  gets the public address, and a host that does not gets the address of the connection. Tracon reads no forwarding header itself: a header the host does not honour changes nothing. A request with no `Host` header gets the
  path as before. The response carries `Vary` on `Host`, `X-Forwarded-Host` and `X-Forwarded-Proto` so a cache that keys on the URL does not serve one caller's address to another. The card is answered by Tracon and no longer by the
  SDK's `MapWellKnownAgentCard`; its name, route and cache headers are unchanged. An application that is reachable under any host name sets `AllowedHosts`, as it does for any absolute address it builds. A card with a fixed
  public address independent of the request (a `PublicBaseUrl` option) was not added: nothing in the options surface asked for it, and it can be added later without breaking anything.

## [1.0.0-preview.6] - 2026-10-27

Agents and workflows gain the controls a production host needs before the surface
freezes. The new `Tracon.AGUI` package lets a front end that speaks the AG-UI protocol
run a Tracon agent. An agent keeps files in a workspace for each session and remembers
what its user said in earlier sessions, a tool can ask the user for input in the middle
of a call (MCP elicitation), and a workflow waits for a tool approval that a person
answers. A callable agent can be pinned to a version, and a version number is never
given twice for one name. A spent call-tree budget or tenant cost quota now stops a tool
call, a function node or an agent step before it starts; a workflow continuation keeps
the budget of the runs before it; and a configuration value Tracon cannot read stops the
host at startup. The release adds three migrations to each SQL provider and breaks
source in several places: read Upgrading from 1.0.0-preview.5 first.

### Upgrading from 1.0.0-preview.5

- **Migrations.** PostgreSQL `0063`–`0065`, SQL Server `0051`–`0053` and SQLite `0050`–`0052`.
  With `AutoApplyMigrations` (the default) they run at startup. They add the `expected_tool_calls`
  column of an eval case, an index on the agent files of a tenant by path, and the
  `agent_definition_version_counters` table, which starts the counter of each agent at the version
  it holds today. No column is dropped. An agent that was deleted and created again before the
  upgrade keeps only the numbers it holds now (see the `IAgentDefinitionStore` entry, Changed).
- **Public types changed.** Each entry below (Changed) says what to do:
  - `AgentDefinition.CallableAgentNames` and `AgentDefinitionRequest.CallableAgentNames` are
    `CallableAgents`, a list of `CallableAgentReference`; a body that still carries
    `callableAgentNames` is refused with `400`.
  - `ModelBinding.AllowConcurrentToolCalls` is `ModelBinding.ToolLoop.AllowConcurrentToolCalls`; a
    body that still carries the old key is refused with `400`.
  - `EvalCase.ExpectedTools` and `EvalCaseDraft.ExpectedTools` are `ExpectedToolCalls`; a body that
    still carries `expectedTools` is refused with `400`.
  - `IJobStore.LeaseAsync` takes one `JobLeaseRequest`.
  - A custom `IAgentDefinitionStore`, `IRunStore` or `IJobStore` must pass the new cases of
    `AgentDefinitionStoreContract`, `RunStoreContract` and `JobStoreContract`.
  - `AddSpeechSynthesizer(name, null)` and `AddSpeechTranscriber(name, null)` no longer compile.
  - `Tracon.Client`: `EvalCaseInput.SourceKind` and `EvalCasePromotionRequest.SourceKind` are
    `EvalCaseSource`, `VoiceSessionRecord.EndReason` is `VoiceSessionEndReason`, and the empty
    `SourceKind2` class is gone (see the nullable enum entry, Fixed).
- **Behavior changes most likely to reach an upgrade**, each described in its entry below:
  - A configuration value Tracon cannot read stops startup, and so does a list or map key that
    holds one value. Start the application once: the keys it names were ignored until now.
  - The eight `tracon.*` metrics that carried `tracon.tenant.id` carry it only when
    `Tracon:Observability:IncludeTenantTag` is `true`.
  - A tool call, a workflow function node or an agent step does not start once the call-tree budget
    or the cost quota of the tenant is spent, and a workflow continuation starts from what the runs
    before it spent.
  - An `IToolAuthorizationHandler` that denies `ToolEffect.External` now also closes
    `search_knowledge` and `search_conversation_memory`.
  - `/v1/chat/completions` refuses a model setting the agent owns, tool-call history and unknown
    roles with `400`.
  - With strict session ownership, an A2A message needs a caller identity to own the conversation.
  - `agentReview` and `toolCallArgsMatch` are built-in names; a criterion or check registered in
    code under either name is refused.
  - An `IAgentDecorator` with an `Order` above 0 no longer sees `TraconRunOptions`.
- Pin every Tracon package to the same exact version. The pre-release Microsoft Agent Framework
  hosting packages stay pinned at `1.25.0-preview.261014.1`, `Microsoft.Agents.AI.Hosting.OpenAI` at
  `1.25.0-alpha.261014.1` and `A2A.AspNetCore` at `1.0.0-preview3`. The Model Context Protocol SDK
  moves to `2.3.0`.

### Added

- **A callable agent can be pinned to a version of the callee: `CallableAgentReference.Version`.** An entry of
  `AgentDefinition.CallableAgents` with a version runs that stored version in every call mode (background tasks, one tool
  for each agent), its child run row records that version, and, for an agent kept in the database, the description the model reads comes from it. An entry with
  no version runs the callee's current version, as before. A caller that a workflow step or an approval resumption pins to
  a version of its own now runs the callee versions *that version* names: a parent pinned to version 1 no longer calls a
  child at version 3 unless version 1 says so. A new version of a pinned callee leaves the compiled caller as
  it is. A name converts to a reference without a version (`CallableAgents = ["editor"]`), so a list of names stays a list of
  names. A save, `POST /api/agents/validate` and the compile refuse a version below 1, a version the callee does not have, a
  pin on an agent defined in code and a name listed twice: the codes are `callable_agent_version_invalid`,
  `callable_agent_version_not_found` and `callable_agent_duplicate`, each with the path of the entry. A pin that stopped
  holding after the save fails the caller by name before its first model call. The agent editor of the console shows a
  *Version of … to run* select under each checked agent that has versions and keeps a pin it saved; the version compare
  shows a change of pin. The OpenAPI document and both generated clients carry the new schema.
- **`WorkflowAgentStepOptions.AgentVersionParameter` lets a run choose the version of an agent step in a graph built in code.**
  Set it to the name of a run parameter declared in `WorkflowRegistrationOptions.Parameters`. The step reads the value
  each time it runs: empty or absent runs the agent's current version, a whole number from 1 runs that version and the
  step's run row records it. A value that is not a whole number from 1, a name the workflow does not declare, a version the
  agent does not have and an agent defined in code fail the step by name before its model call; the error for a value that
  is not a number never repeats it. It cannot be set together with `AgentVersion` (`GetWorkflowAgentStep` throws
  `ArgumentException`). A run that continues after an answer reads the value the run started with: a number keeps its
  version through the wait, an empty value still means the current version when each step runs.
- **`openai.strictJsonSchema` and `azure-openai.strictJsonSchema` ask OpenAI to enforce the response schema while the
  model generates.** Set either to `true` in `ModelBinding.ProviderSettings` (`OpenAIProviderNames.StrictJsonSchemaSetting`,
  `AzureOpenAIProviderNames.StrictJsonSchemaSetting`; `SupportedSettings` is no longer empty). The request then carries
  `strict: true` on the response format of an agent whose `ResponseFormat` is a JSON schema. It reaches the response
  format only: the tools of the agent stay non-strict, and a fallback link, which receives no provider settings, is not
  strict. The setting is off by default. `openai` reads it on both API surfaces and on every `UseOpenAICompatible()`
  registration; a server that does not know strict mode can refuse the request.
- **`POST /api/agents/validate` returns the response schema each provider's client sends.** The report has a new field
  `responseSchemas`, one entry per binding and per fallback link (`provider`, `model`, `strict`, `schema`), for providers
  that implement the new `IResponseSchemaSupport` (`OpenAI` and `Azure`). Three warnings name what differs from the schema you
  wrote, each with JSON Pointers into it: `response_schema_optional_required` (an optional property that does not allow
  `null` is sent as required), `response_schema_keyword_not_enforced` (a bound such as `maxLength` or `pattern` is sent
  as description text) and `response_schema_strict_unsupported` (strict mode only: the parts OpenAI refuses with HTTP 400,
  which are `oneOf`, `allOf`, `if`, `then`, `dependentRequired`, `dependentSchemas` and an object without
  `additionalProperties: false`). The last one is reported for `openai` and `openai-responses` on `api.openai.com`, the
  only service measured, and it is a warning so that it never stops a definition from being saved. The OpenAPI
  document and both generated clients carry the new field and the three codes.
- **`IResponseSchemaSupport` and `ResponseSchemaShape`.** An optional interface of an `IModelProvider`: given a binding and
  the author's schema, it returns the schema the provider's client sends (`EffectiveSchema`), whether the request is strict,
  and the three lists above. A provider that does not implement it adds nothing to the report.

- **`UseGoogleSpeech` registers a Gemini text-to-speech model as the named provider `gemini`,** with
  `GoogleSpeechOptions` (`Model`, `Name`, `MaxCharactersPerRequest`, `MaxDialogueCharactersPerRequest`,
  `DialogueSpeakerMode`, `Voices`, `UseAsDefault`). One instance speaks one voice (`POST /api/voice/speak` with
  `"provider": "gemini"` answers a WAV file) and voices a dialogue of exactly two voices through
  `GetDialogueSynthesizerAsync`. It shares the client of `UseGoogle` and the provider name `google`, so one egress rule
  covers chat, images and speech, and a tenant with its own Google key is served with that key on the client its chat
  requests use. The speaker format of a dialogue depends on the model generation, and the wrong one answers `400`: set
  `DialogueSpeakerMode` to `PartMetadata` (the `gemini-3.8` models, the default) or `TextLabels` (`gemini-2.5-flash-preview-tts`,
  `gemini-3.1-flash-tts-preview`). The audio is a WAV file or raw 16-bit PCM (`OutputFormat` `wav` or `pcm`); a stream
  is raw PCM. Character timing is refused, and billing is an estimate.
- **`SpeechProviderDescriptor.Dialogue` and `GET /api/voice/providers` report whether a speech provider can voice a
  dialogue.** The value is the provider's `SpeechDialogueCapabilities` (most voices, most characters, whether it reports
  timing) or `null`. A caller no longer learns it from the failure of `GetDialogueSynthesizerAsync`. The OpenAPI document
  and both generated clients carry the new field.
- **`SpeechDialogueCapabilities.Check(request, providerName)` is the rule a dialogue implementation applies before it calls
  its service.** The built-in ElevenLabs client and the Gemini synthesizer use it, with the same messages; a
  synthesizer of your own calls it to refuse an empty turn, a turn without a voice, too many voices or too many
  characters, and never to split or cut the dialogue.

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

- **Another agent can be the stop criterion of a loop: `agentReview`.** A `LoopCriterion` with `Kind = "agentReview"` names
  a reviewer agent of the catalog in the new `ReviewerAgent`, and optionally a version in the new `ReviewerAgentVersion`.
  After each iteration the reviewer reads the request, the iteration number and the draft (the last assistant message),
  and answers with `{"approved": true|false, "feedback": "..."}`: `true` stops the loop, `false` sends the feedback to
  the agent as the next input. The reviewer runs as a child run of the run under review, through the same door every
  sub-agent call uses, so the tenant check, the depth limit, the run and token budget, the quota, the wait limit and the
  refusal of a tool that waits for approval all apply. A refused call, a failed call and an answer that is not that
  object are failures of the criterion, and `OnCriterionError` decides what follows; an answer that cannot be read is
  never an approval. The shape is the reviewer agent's own response format, not an option the loop sets on the call.
  Validation reports `loop_reviewer_missing`, `loop_reviewer_self`, `loop_reviewer_version_invalid` and
  `loop_reviewer_not_found`, and warns with `loop_reviewer_verdict_unknown` when the reviewer has no JSON schema with a
  boolean `approved` and a string `feedback`. The console's Loop panel lists the agents and takes the version.
  `GET /api/loop-criteria` lists the kind. See [Another agent as the reviewer](https://tracon.dev/concepts/agents/#another-agent-as-the-reviewer).

- **The `LoopIterationCompleted` event says who decided.** Its payload has a new `criteria` list: every criterion
  evaluated in that iteration, in order, with its `kind` and `decision` (`stop`, `continue` or `failed`). An `aiJudge`
  entry names the `judge` (`provider` and `model` of the call, the fallback model when the judge's own model failed over),
  and an `agentReview` entry names the `reviewer` (`agentName`, `agentVersion` and the `runId` of its child run). The event
  text names the reviewer too. The judge's reasoning, the reviewer's answer, the draft and the feedback are not written.
  The `loop.iteration-completed` frame of the native stream carries the same data. A reader that ignores a field it does
  not know needs no change.

- **The run stream of an agent with a loop says where each iteration ends.** `POST /api/agents/{name}/run` already
  streamed every iteration, but nothing in the `update` frames marked the border, so a client that read the stream
  could take the first draft for the answer. After each iteration the loop evaluated, the stream now sends a
  `loop.iteration-completed` frame, between the last `update` of that iteration and the first of the next. Its data is
  the recorded `LoopIterationCompleted` event, byte for byte what `GET /api/runs/{id}/events` serves. The answer is the
  iteration the last frame closes when it has `continued: false`; when it has `ceilingReached: true`, the answer is the
  iteration after it, because the turn at the ceiling is not evaluated. The `update` after a frame with `continued: true`
  is the loop's feedback to the model (`role` `user`), not the model's text. The event's `payload` is `null` when
  `RecordToolPayloads` is off. An agent without a loop, an event the store could not record and a sub-agent's loop send
  no frame. Recorded event timestamps now have whole microseconds, so the frame and `/events` agree on every store. The console playground draws a divider at each frame. A client that
  ignores a frame name it does not know needs no change. See [Streaming](https://tracon.dev/http-api/#streaming).

### Changed

- **`AgentDefinition.CallableAgentNames` is now `CallableAgents`, a list of `CallableAgentReference` (breaking).** Rename the
  initializer; a name converts to a reference, so `CallableAgentNames = ["support"]` becomes `CallableAgents = ["support"]`.
  `AgentDefinitionRequest.CallableAgentNames` is `CallableAgents` too, and over HTTP the field is `callableAgents`, a list of
  `{ "name": ..., "version": ... }` objects. A request body that still carries `callableAgentNames` is refused with `400`
  and names the new field, so it cannot drop the callable agents in silence. The validation path of a tool name that another
  tool already has moved from `callableAgentNames[i]` to `callableAgents[i].name`. `AgentDescriptor.CallableAgentNames` (the
  catalog's list of names) is unchanged. **Upgrade note:** no migration. A stored definition that holds the old key reads back
  as entries with no version, and takes the new shape the next time it is saved; a custom `IAgentDefinitionStore` must keep
  the version of each entry (`AgentDefinitionStoreContract` now checks it).

- **`IAgentDefinitionStore` never gives a version number twice for one name.** `SaveAsync` returns one more than the highest
  version the name ever had, including the versions of a definition that was deleted; `DeleteAsync` still removes the
  definition and every one of its versions and keeps only the number. A custom `IAgentDefinitionStore` must keep that number
  too: `AgentDefinitionStoreContract` has eight more cases that a store which restarts at 1 now fails:
  `AgentDefinitionStoreContract.SaveAsync_after_DeleteAsync_continues_the_numbering`,
  `AgentDefinitionStoreContract.The_numbering_survives_every_delete_not_only_the_first`,
  `AgentDefinitionStoreContract.DeleteAsync_of_a_name_that_never_existed_does_not_start_a_numbering`,
  `AgentDefinitionStoreContract.RollbackAsync_after_a_recreate_continues_the_numbering`,
  `AgentDefinitionStoreContract.RollbackAsync_for_a_deleted_agent_throws_and_keeps_the_numbering`,
  `AgentDefinitionStoreContract.One_tenants_delete_does_not_move_another_tenants_numbering`,
  `AgentDefinitionStoreContract.Concurrent_first_saves_of_a_new_name_get_distinct_versions` and
  `AgentDefinitionStoreContract.Concurrent_saves_of_one_name_get_distinct_versions_across_a_delete`. The PostgreSQL, SQL Server and SQLite
  migrations add the `agent_definition_version_counters` table (`0065`, `0053` and `0052`), one row of name and number per
  agent and no agent content, and start each existing agent's counter at the version it holds today. **Upgrade note:** an
  agent that was deleted and created again before the upgrade cannot be recovered; its earlier numbers were never kept, so its
  counter starts at the highest number it holds now. During a rolling upgrade an older process may still save an agent without
  touching the counter: the next number is one more than the larger of the counter and the agent's own version, so such a
  save never collides with the history it wrote.

- **A configuration value that cannot be read stops the host at startup.** A key that held text its type could not read
  (`Tracon:Tools:DefaultMaxOutputBytes = "10MB"`, `Tracon:RunRecording:RecordToolPayloads = "nope"`,
  `Tracon:RateLimit:Enabled = "yes"`) was skipped and the shipped default stayed, with no message. 25 of those defaults are
  the loose side, so a typo left a limit open or a protection off. Now every number, flag, time span and enum name in the
  `Tracon:*` sections and in the package sections (`Mcp`, `Voice`, `Voice:Live`, `Voice:Conversation`, the providers, the
  SQL stores) must be readable, and so must the numbers of a model catalog entry and of a price. The host refuses to start
  and the message names the full key and quotes the text: `Tracon:Tools:DefaultMaxOutputBytes is '10MB', which is not a
  32-bit whole number.` Every unreadable key of the same options type is listed in one message. **Empty text, or only white
  space, is still "not given"** and keeps the default. Code that assigns the option after binding still wins, so a typo you
  have overridden in a `Configure` callback does not stop the host. **Upgrade check:** start the application once; the
  keys it names are the ones that were ignored until now.
- **A list key that holds one value is refused.** `Tracon:Skills:Scripts:EnvironmentAllowList=PATH` was ignored and the
  default list stayed. The message now says to write `…:EnvironmentAllowList:0`. A JSON `[]` and a key with no text keep
  the default list, as before; to empty a list, clear it in code. This covers every list key (`Lanes`,
  `HttpSchedulableHandlerKeys`, `DeniedTerms`, `AgentNames`, `AllowedMediaTypes`, `ThresholdPercents`, `RetryDelays`,
  `Columns`).
- **An entry of a list or a map that cannot be read is left out and named by its position or name** (`Quotas:ThresholdPercents:1`,
  `Scheduling:MaxConcurrentJobsPerLane:media`). It used to be bound as `0` (or as an undefined value) so that the options
  validator would complain about the stand-in; the message now quotes what you wrote.
- **A decimal no longer takes a thousands separator.** `Tracon:Pricing:…:Input = "1,5"` was read as fifteen. It is now an
  unreadable value; write `1.5`.
- **`Tracon:Voice:Live:DelegationMode` is refused by the same rule** instead of throwing while the options are bound:
  the host stops at startup with an `OptionsValidationException` that names the key and the one mode, `Client`.
- **An address key that is not an absolute address is refused by the same rule.** `Tracon:Voice:Endpoint`,
  `Tracon:Providers:OpenAI:Live:Endpoint` and `Tracon:Mcp:OAuthCallbackBaseUri` ignored text such as
  `gateway.example.com/v1` and kept the default, so speech and live sessions went to the provider's public address. The
  host now stops and names the key: `… is 'gateway.example.com/v1', which is not an absolute address such as
  https://example.com/.`
- **A map key that holds one value is refused.** `Tracon:Scheduling:MaxConcurrentJobsPerLane=5`,
  `…:MaxDurationByHandlerKey=00:10:00` and `…:LaneByHandlerKey=media` name no lane or handler and were ignored. The
  message now says to write `…:MaxConcurrentJobsPerLane:<name>`.

- **The knowledge search and the on-demand conversation memory search run through the tool chain.** `search_knowledge`
  (`MemorySettings.EnableVectorSearch`) and `search_conversation_memory` (`ConversationMemorySearch.OnDemand`) were added
  to a run as bare functions, so your `IToolAuthorizationHandler`, your `IToolArgumentsValidator`, the tool timeout and
  output limit, and the tree budget never saw them, and the refusal of a tenant's egress policy reached the model as "Error:
  Function failed." Now both pass the chain a tool registered in code passes. **An authorization handler that denies
  `ToolEffect.External` calls now also closes both searches:** it is asked with the tool name and `Effect = External`,
  because the text of the query leaves the process, and the model reads your reason as the result. A validator that
  rejects a tool it does not know rejects them too. Neither tool is in `IToolRegistry`, so `GET /api/tools` does not list
  them; write their names in your policy as fixed text. A spent budget stops the search before the query is embedded; the
  whole call, the embedding included, ends at `Tracon:Tools:DefaultTimeout` (30 seconds by default); and a hit reaches the model as text, trimmed at
  `Tracon:Tools:DefaultMaxOutputBytes` like any tool result. The call line of `search_knowledge` in
  `GET /api/runs/{id}/tools` now has a `usage`: unit `tokens`, the input token count of the embedded query as the provider
  reported it, and no `cost`, so it adds to no budget, quota or webhook total; the run's `nonTokenCost` counts it under
  `unsummedItemCount`. An embedding has no price in Tracon yet. See
  [Embedding Tracon](https://tracon.dev/guides/embedding/#which-tools-reach-the-tool-authorization-handler).
- **A tool call does not start once the call tree's budget is spent.** The budget (`Tracon:AgentGraph:MaxTotalTokens`,
  `MaxTotalCost`, `MaxDuration`) already refused the next model call, a new agent step and a workflow function node. A tool
  call was the one start it did not read, so when one model turn asked for two paid tools and the first passed the ceiling,
  the second still ran: its money and its side effect were gone, and the model call that would have read its result was
  refused. Now the second tool does not run. Its result is the limit, the setting to raise and "The tool call did not
  start."; a streaming run (the HTTP run is one) records the call as failed; and the run ends at its next model call as
  before (`Failed`, `run_budget_exceeded`). All three limits stop a tool call, `MaxTotalTokens` included, since a model reads its
  result; a function node is still stopped by cost and time only. The check covers tools registered in code, session
  workspace tools, MCP tools, the knowledge and conversation memory searches and skill scripts, and it runs before
  your `IToolAuthorizationHandler`. A tool that has started
  is never stopped, so the call that crosses the ceiling is the last one that starts; with
  `ToolLoop.AllowConcurrentToolCalls` on, the tools of one turn start together and all of them start. A hosted tool runs
  inside the model call and is not checked. A host that sets no ceiling still has the default `MaxTotalTokens` of 200,000:
  a tool that a turn asks for after the tree has passed it no longer runs. See
  [Bound multi-agent trees](https://tracon.dev/guides/reliability/#a-spent-budget-stops-a-tool-call-from-starting).
- **`agentReview` is now a built-in loop criterion kind, so a criterion registered in code under that name is refused.**
  `AddLoopEvaluator("agentReview", ...)` (in any casing) throws `TraconException` at startup, the way the other four
  built-in names do. A definition that named such a kind now means the built-in one; rename your criterion
  before you update.
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
  the shared contract tests (`RunStoreContract`) have three new cases,
  `RunStoreContract.ClaimOrphanedRunsAsync_closes_a_Queued_row_whose_heartbeat_is_stale`,
  `RunStoreContract.ClaimOrphanedRunsAsync_does_not_touch_a_Queued_row_with_a_fresh_heartbeat` and
  `RunStoreContract.A_run_promoted_from_Queued_to_Running_keeps_the_heartbeat_it_sent_while_it_waited`, and
  `ClaimOrphanedRunsAsync_never_touches_a_Queued_row` is now
  `RunStoreContract.ClaimOrphanedRunsAsync_never_touches_a_Queued_row_that_has_no_heartbeat`.
- **Breaking for implementers (preview):** `IJobStore.LeaseAsync(owner, leaseDuration, lanes, cancellationToken)` is now
  `IJobStore.LeaseAsync(JobLeaseRequest request, cancellationToken)`. `JobLeaseRequest` carries `Owner`, `LeaseDuration`,
  `Lanes` and the new `TenantCap` (`JobTenantCap`: the handler keys and `MaxLeasedPerTenant`); a new option is a new
  property, not a new parameter. An `IJobStore` you implement takes the request and, when `TenantCap` is set, skips a ready
  job of a handler key in the cap while its tenant holds that many live leases on those keys (an expired lease does not
  count). A caller reads `request.Lanes` where it read `lanes`. `JobStoreContract` has eleven new cases for the cap:
  `JobStoreContract.Lease_without_a_tenant_cap_gives_one_tenant_the_whole_queue_in_age_order`,
  `JobStoreContract.Lease_with_a_tenant_cap_leases_up_to_the_cap_for_one_tenant`,
  `JobStoreContract.Lease_with_a_tenant_cap_skips_a_tenant_at_the_cap_and_leases_the_oldest_job_of_another`,
  `JobStoreContract.Lease_with_a_tenant_cap_counts_a_running_job`,
  `JobStoreContract.Lease_with_a_tenant_cap_counts_both_workflow_handler_keys_together`,
  `JobStoreContract.Lease_with_a_tenant_cap_does_not_count_a_job_of_another_handler_key`,
  `JobStoreContract.Lease_with_a_tenant_cap_does_not_count_a_lease_that_has_expired`,
  `JobStoreContract.Lease_with_a_tenant_cap_does_not_count_the_leases_of_another_tenant`,
  `JobStoreContract.Lease_with_a_tenant_cap_still_honors_the_lane_filter`,
  `JobStoreContract.Lease_with_a_tenant_cap_still_leases_a_job_of_another_handler_key_of_a_tenant_at_the_cap` and
  `JobStoreContract.Lease_with_a_tenant_cap_that_names_no_handler_key_or_no_room_is_refused`.

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

- **A run's events and its end are stamped with the `TimeProvider` its start came from.** A run's start came from the
  registered `TimeProvider` and its end and events from the system clock. With your own `TimeProvider` registered (a test
  clock, for example) a run seemed to last from the fake start to today, and the time a workflow continuation inherited
  from the run it continues was that gap. With the default clock nothing changes.
- **A version number of a stored agent could name another content after the agent was deleted and created again.** The
  numbering restarted at 1, so a pin to version 3 (a workflow step's `agentVersion`, a loop reviewer, an experiment arm) or
  a tool approval that waited on version 2 reached the new agent's third or second save a few edits later, and ran its
  text instead of failing. A run on an approval, for example, was resumed with the new agent's instructions and completed.
  The numbering now continues, so the old number names no version and the run stops before any step with `pins agent 'x' to
  version N, but the agent has no version N` (a workflow) or fails (an approval resume). Version numbers a run recorded
  before the delete are not reachable any more, as they were not before. Needs the migration of the **Changed** entry above.

- **A data subject erasure (`DELETE /api/data-subjects/{id}?dryRun=false`) did not return on SQLite.** The audit
  entry is written before the erasure commits, from inside its transaction. On SQLite that transaction holds the
  database's single write lock, so the audit write from a second connection waited until its command timed out,
  and the audit log retried the wait up to 50 times. On SQLite the audit entry is now written on the erasure's own
  transaction, and the two commit or roll back together. PostgreSQL and SQL Server are not changed. An `IAuditLog`
  you register in place of Tracon's own still writes on its own connection.

- **OpenAI and Azure OpenAI never received the response schema as written, and nothing showed it.** The Microsoft.Extensions.AI
  client rewrites the schema on every request, strict or not: it closes every object, lists every property in `required`
  (an optional property becomes required and its type does not gain `null`), and moves 22 keywords (`maxLength`, `pattern`,
  `minimum`, `minItems` and others) out of the schema into the property's `description`. An optional `nickname` the model did
  not know came back as `""`. The behavior is not changed; the validation report now shows it (see above).

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
