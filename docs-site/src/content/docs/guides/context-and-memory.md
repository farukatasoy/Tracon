---
title: Context and memory
description: Control conversation history, compaction, working memory, MCP resources, and persistent knowledge without mixing their responsibilities.
---

“Memory” is not one store. Tracon separates conversation continuity, context
budgeting, working state, fixed resources, semantic knowledge, and what one user told
an agent before. Choose each layer for the question it answers.

## Mental model: six different jobs

| Layer | Question it answers | Main API |
|---|---|---|
| Session history | What did this conversation already say? | `AgentRunRequest.SessionId` |
| Compaction | Which old context still fits in the next model call? | `CompactionSettings` |
| Working memory | What files, todos, or text should this agent manage during work? | `MemorySettings` |
| MCP resources | Which fixed remote resources enter every run? | `AgentDefinition.McpResourceUris` |
| Knowledge search | Which durable external facts are relevant to this question? | `MemorySettings.EnableVectorSearch` |
| Conversation memory | What did this user tell this agent in an earlier session? | `MemorySettings.EnableConversationMemory` |

```mermaid
flowchart LR
    accTitle: Agent context assembly
    accDescr: Session history is compacted, then combined with working memory, fixed MCP resources, knowledge results, and the user's conversation memory before the model request.
    Q["Run request"] --> H["Session history"]
    H --> C["Compaction"]
    W["Working memory"] --> X["Model context and tools"]
    R["MCP resources"] --> X
    K["Knowledge search tool"] --> X
    U["Conversation memory"] --> X
    C --> X
    X --> M["Provider model"]
```

A session preserves continuity. Compaction reduces what the model sees. It does not
delete the persisted run record. File memory and knowledge search add capabilities; they
do not replace session history.

These six layers all shape what already made it into the conversation. A tool's
[output size limit](/concepts/tools/#output-size-limit) works earlier, at the source:
it bounds a single tool result before that result ever becomes context to compact. The
two are complementary, not competing — a tool limit caps one call's contribution,
compaction manages the accumulated history afterward.

## Session history starts with `sessionId`

A run with no `sessionId` is sessionless. The next request does not receive its chat
history. Reuse a session id when turns must build on each other:

```bash
curl -N -X POST http://localhost:5081/tracon/api/agents/research/run \
  -H "Authorization: Bearer $TRACON_TOKEN" \
  -H 'Content-Type: application/json' \
  -d '{"sessionId":"case-4182","message":"Summarize the customer request."}'

curl -N -X POST http://localhost:5081/tracon/api/agents/research/run \
  -H "Authorization: Bearer $TRACON_TOKEN" \
  -H 'Content-Type: application/json' \
  -d '{"sessionId":"case-4182","message":"Now list the unresolved questions."}'
```

Without a SQL package, session history is in memory. `UsePostgreSql()`,
`UseSqlServer()`, or `UseSqlite()` replaces it with the corresponding durable store.

## Add compaction and working memory

Compaction and memory work on a plain chat agent. A `HarnessSettings` value is not
required. Add the harness only when you also need its execution policy.

```csharp
tracon.AddAgent(new AgentDefinition
{
    Name = "research",
    Instructions = "Investigate the request. Keep a concise evidence trail.",
    Model = new ModelBinding
    {
        Provider = OpenAIProviderNames.ChatCompletions,
        Model = "your-current-model-name",
        MaxOutputTokens = 2_048,
    },
    Harness = new HarnessSettings
    {
        MaxContextWindowTokens = 64_000,
        MaxOutputTokens = 2_048,
        MaximumIterationsPerRequest = 12,
        HarnessInstructions = "Update the todo list before the final answer.",
    },
    Compaction = new CompactionSettings
    {
        Strategy = CompactionStrategyKind.Pipeline,
        TriggerTokens = 48_000,
        MinimumPreservedTurns = 3,
        MinimumPreservedGroups = 6,
        SummarizationModel = new ModelBinding
        {
            Provider = OpenAIProviderNames.ChatCompletions,
            Model = "your-lower-cost-summary-model",
            MaxOutputTokens = 1_024,
        },
    },
    Memory = new MemorySettings
    {
        EnableFileMemory = true,
        EnableTodo = true,
        EnableTextSearch = true,
    },
});
```

The compiler checks conflicts before the run. For example, it rejects
`Memory.EnableFileMemory = true` together with `Harness.DisableFileMemory = true`.
The same rule applies to compaction and todo tracking, to `SkillNames` together
with `Harness.DisableAgentSkillsProvider = true`, and to
`Harness.EnableWebSearch` together with `Harness.DisableWebSearch`.

A harness does not add file memory, skills or web search on its own. File memory
needs `Memory.EnableFileMemory`, skills need `SkillNames`, and web search needs
`Harness.EnableWebSearch`. The harness never reads skills from the server's working
directory and never writes memory files there.

The provider bills each call of the harness's web search. Set
`Tracon:Pricing:HostedTools:{provider}:WebSearch:PerCall` and the calls join the run's cost, the budget,
the quota, and the `run.completed` webhook, as the calls of a [hosted tool](/guides/hosted-tools/) do. An
agent cannot also select a registered hosted web search: the model would receive two.

## Choose a compaction strategy

| Strategy | What it does | Required input |
|---|---|---|
| `None` | Sends no compaction policy | None |
| `SlidingWindow` | Drops the oldest turns | At least one trigger |
| `Truncation` | Truncates excluded message groups | At least one trigger |
| `ToolResult` | Shortens tool-call and tool-result groups | At least one trigger |
| `Summarization` | Replaces older groups with a model summary | At least one trigger |
| `ContextWindow` | Evicts or truncates against a known window | `MaxContextWindowTokens` |
| `Pipeline` | Runs ToolResult, then SlidingWindow, then Summarization | At least one trigger |

`TriggerTokens`, `TriggerMessages`, and `TriggerTurns` are combined with **OR**. The
first threshold reached starts compaction. `ContextWindow` manages its own trigger and
does not require one of those fields.

Defaults are explicit:

| Setting | Default |
|---|---|
| `CompactionSettings.Strategy` | `None` |
| `MinimumPreservedTurns` | 2 |
| `MinimumPreservedGroups` | 4 |
| `ContextWindow.MaxOutputTokens` | Agent model limit, then 4096 |
| Summarization prompt | MAF default |
| Summarization model | Agent setting, then application `UtilityModel`, then the agent model |

:::caution[No compaction can become a run failure]
When `Compaction` is absent or uses `None`, Tracon sends no compaction strategy.
A long session eventually exceeds the provider's context window. Set a measured
trigger below the real model limit and leave room for output and tool results.
:::

MAF currently marks its compaction and `AgentFileStore` APIs as evaluation features.
Tracon keeps the integration in one compiler boundary, but you should still test
context behavior when upgrading MAF packages.

## Understand the memory flags

All `MemorySettings` flags default to `false`.

`EnableFileMemory` adds MAF file memory, on a plain agent and on a harness agent
alike: without it a harness agent has no file memory. On a plain agent, `EnableTodo` adds todo
tracking. A harness already keeps todo tracking on unless
`Harness.DisableTodoProvider` is true. `EnableTextSearch` searches the registered
`AgentFileStore`. The default file store is in memory. `UsePostgreSql()`,
`UseSqlServer()`, or `UseSqlite()` replaces it with the corresponding persistent SQL
file store.

`EnableVectorSearch` is different. It adds the code-defined `search_knowledge` tool
over a persistent semantic knowledge base that you loaded. It does not remember
conversations. See [Knowledge and RAG](/guides/knowledge/).

`EnableConversationMemory` remembers what one user said to this agent in earlier
sessions. It needs setup in code. See
[Remember a user across sessions](#remember-a-user-across-sessions).

The console agent editor exposes harness settings, all compaction strategies, file
memory, todo tracking, text search, and conversation memory (one box and the search
choice). It does not currently expose vector search, the search limit of conversation
memory, or MCP resource URIs. Set those through code or the management HTTP API. Set
MCP resource URIs in code.

## Remember a user across sessions

A session keeps one conversation. Conversation memory keeps what a *user* said, and
gives it to the same agent in a later session. Ada tells the agent her favourite
colour in one session. In a new session, the agent finds that message and answers
"teal".

Two switches turn it on. Both are off by default.

1. **Setup, once, in code.** Register a `Microsoft.Extensions.VectorData.VectorStore`
   in the service collection, and call `UseConversationMemory`:

   ```csharp
   builder.Services.AddSingleton<VectorStore>(provider => new PostgresVectorStore(
       connectionString,
       new PostgresVectorStoreOptions
       {
           EmbeddingGenerator = provider.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>(),
       }));

   builder.AddTracon()
       .UseConversationMemory(options => options.VectorDimensions = 1536);
   ```

   `VectorDimensions` is the vector size of your embedding model. `CollectionName`
   defaults to `tracon_conversation_memory`. The store belongs to your application:
   you choose the connector, register it as a singleton, and the store turns each
   message into a vector with its own embedding generator. Tracon never creates a
   store and never calls the embedding model.

2. **A definition that asks for it.**

   ```json
   {
     "memory": {
       "enableConversationMemory": true,
       "conversationMemorySearch": "BeforeEachTurn",
       "conversationMemoryMaxResults": 3
     }
   }
   ```

If a definition asks for memory and the application has not done step 1, the agent
does not compile. The error names the missing call or the missing `VectorStore`.

### Who the memory belongs to

A record carries the tenant, the agent, the user, and the session it came from. A
search looks at the tenant, the agent, and the user, not the session. This is why a
new session still finds the old one.

| A run… | Memory |
|---|---|
| has a user and a session | reads and writes |
| has no user (an anonymous run) | neither reads nor writes |
| has no session id | neither reads nor writes — a record with no session could not be deleted with its session |
| belongs to another tenant, agent, or user | never sees the record |

The user is the one your [run attribution](/concepts/governance/) resolves, never a
field in the request body. Name a `sessionId` in the request; a request without one
opens no session and remembers nothing.

A sub-agent and a queued run remember for the same user and session as the run that
started them. **Important:** an agent step of a [workflow](/concepts/workflows/) remembers too, under
the workflow run's own session id. No session record carries that id, so deleting a
session and the sessions retention policy do not reach those records. A data subject
erasure deletes them only if the scope you return from `IDataSubjectResolver` names that id
(it is `sessionId` in the run record). Keep conversation memory off for agents that
workflows call, unless you accept this.

A user with the same `sessionId` as another user keeps a separate memory. Tracon
reads the scope from the run on every call, not from the session, so the first user
to name a session does not decide the scope for the next one.

### What is stored, and when it is searched

Tracon stores the text of the user's messages and of the agent's answers. It does not
store tool calls, tool results, or messages another provider added to the request.
The agent's own earlier answers are stored too, so an answer such as "I do not know
that yet" can come back in a later search.

`conversationMemorySearch` decides when the search runs:

| Value | Behavior |
|---|---|
| `BeforeEachTurn` (default) | Searches with the user's message before each turn, and adds what it finds as a message under a *Memories* heading. |
| `OnDemand` | Offers the model a tool named `search_conversation_memory`. The model decides when to search and what to ask for. |

`conversationMemoryMaxResults` limits one search to 1–20 records. The default is 3.
The `OnDemand` tool is not part of the agent's `ToolNames`: it needs no approval, and
it reads only the scope of the run that calls it. A tool of the agent with the same
name fails the build.

### Deleting memory

The records are a copy of the session's content, so they go when their session goes:

- `DELETE /api/sessions/{id}` deletes the records first, then the session. If the
  store cannot delete, the request fails and the session stays, so a repeat request
  finishes the work.
- A [data subject erasure](/concepts/governance/#data-subject-rights) deletes the
  records of the subject's sessions inside the erasure. A preview counts them under
  `conversation_memory`, and an export lists them.
- The [retention](/concepts/governance/#retention) policy for sessions deletes the
  records of an expired session before the session row.

The store can keep data for its own reasons, such as backups. Tracon cannot delete
that.

### Failures, cost, and limits

- If the store is down when a run starts, the run goes on without memory and the
  framework logs the error. If it is down when the run ends, the run's answer is
  kept and the messages are not remembered. Memory is context, not the answer.
- The store embeds each message. That model call is not on the run's usage and cost.
  Check the cost with your embedding provider.
- Search quality, and whether the filter on tenant, agent, and user is applied at
  all, belong to the connector. Test that two users do not see each other's records
  before you rely on it.
- **Check the connector against the abstractions version.** The Microsoft Agent
  Framework needs `Microsoft.Extensions.VectorData.Abstractions` 10.10 or newer.
  Connectors built against an older version, such as the
  `Microsoft.SemanticKernel.Connectors.*` packages (1.74 preview), call a member
  that no longer exists. NuGet raises no warning, and the failure is a
  `MissingMethodException` in the log at run time, with the agent answering without
  memory. `CommunityToolkit.VectorData.*` 1.0 connectors work.
- The vector store is not AOT-analyzed by Tracon. Tracon's own code and the framework
  provider publish with no Native AOT warning; your connector has its own result.

## Know the agent modes

A harness agent always runs in a mode. The mode provider is on unless
`Harness.DisableAgentModeProvider` is true. A plain chat agent has no modes.

Two modes come from MAF, and a new session starts in the first one:

| Mode | Behavior |
|---|---|
| `plan` | Interactive. The agent asks clarifying questions, discusses options, and waits for your approval before it proceeds. **This is the starting mode.** |
| `execute` | Autonomous. The agent carries the work out without stopping for approval. |

The provider adds two tools, `mode_set` and `mode_get`, so the model can read and
change its own mode. It also injects the current mode's instructions on every
invocation. Those instructions apply to every substantive request, including short
factual questions — a harness agent in `plan` mode can answer a simple question with
questions of its own.

You cannot define your own modes today. Tracon passes no mode options to MAF, so
you get these two. Set `Harness.DisableAgentModeProvider = true` to turn the provider
off, together with both of its tools.

## Add predictable MCP resources

Code-defined agents can name resources in `{server}:{uri}` form:

```csharp
tracon.AddAgent(new AgentDefinition
{
    Name = "release-reviewer",
    Instructions = "Review the release against the supplied policy.",
    Model = new ModelBinding
    {
        Provider = OpenAIProviderNames.ChatCompletions,
        Model = "your-current-model-name",
    },
    McpResourceUris =
    [
        "github:https://example.com/release-policy.md",
    ],
});
```

Call `UseMcp()` before compiling an agent that uses this field. Tracon reads these
resources at the start of each run. The defaults are 64 KiB per resource and 256 KiB
in total. Configure them with `TraconMcpOptions.MaxResourceBytesPerResource` and
`MaxResourceBytesTotal`.

The current `AgentDefinitionRequest` management contract does not expose
`McpResourceUris`. Define this field in code. Remote tools and server registrations
remain available through the MCP management surface.

Only resources declared by the server are read. An invalid reference, unreachable
server, or undeclared resource is skipped and logged. Content above the per-resource
budget is truncated; resources after the total budget is exhausted are skipped.

## Validate before saving

The validation endpoint compiles the same definition without writing it and without
calling a model:

```bash
curl -sS -X POST http://localhost:5081/tracon/api/agents/validate \
  -H "Authorization: Bearer $TRACON_TOKEN" \
  -H 'Content-Type: application/json' \
  -d '{
    "name": "research",
    "instructions": "Investigate the request.",
    "model": { "provider": "openai", "model": "your-current-model-name" },
    "compaction": {
      "strategy": "Pipeline",
      "triggerTokens": 48000,
      "minimumPreservedTurns": 3,
      "minimumPreservedGroups": 6
    },
    "memory": {
      "enableFileMemory": true,
      "enableTodo": true,
      "enableTextSearch": true
    }
  }'
```

A well-formed request returns `200` even when the report says `valid: false`. That
distinguishes an invalid definition from a transport failure.

## Troubleshooting

**The second turn forgot the first one.** Reuse the same non-empty `sessionId`. A
sessionless run carries no history to a later request.

**A compaction strategy fails to compile.** Every strategy except `ContextWindow`
needs at least one trigger. `ContextWindow` needs `MaxContextWindowTokens`.

**Context still overflows.** Lower the trigger. Reserve space for output, system
instructions, tool schemas, tool results, skills, and resources. A provider's nominal
window is not all available to conversation history.

**Summarization costs more than expected.** It is another model call. Set the agent's
`SummarizationModel`, or configure the application-wide `TraconOptions.UtilityModel`.
The summarizer reads the tenant's conversation, so it is treated like the agent's own
model: the tenant's egress policy applies to its provider, and it uses the tenant's
own key when one is bound.

**File memory disappears after restart.** The default `AgentFileStore` is in memory.
Register PostgreSQL, SQL Server, or SQLite for a built-in persistent implementation.

**Harness compilation reports a conflict.** Do not enable a capability in
`Compaction` or `Memory` while the matching `Harness.Disable...` flag is true.

**A harness agent asks questions instead of answering.** A new session starts in
`plan` mode, which is interactive by design. Tell the agent to switch, or set
`Harness.DisableAgentModeProvider = true`.

**An MCP resource fails to compile or is truncated.** Confirm `UseMcp()` ran, the
reference uses `{server}:{uri}`, and the resource stays within the configured byte
budgets.

**Vector search fails to compile.** It needs both an `IVectorSearchStore` and an
`IEmbeddingGenerator<string, Embedding<float>>`. The built-in store comes from
PostgreSQL.

**The agent does not remember the user.** Check four things. The request names a
`sessionId`. Your attribution resolves a user for the run. The earlier session was
stored under the same tenant and agent. The log has no `ChatHistoryMemoryProvider`
error — a `MissingMethodException` there means the connector is older than the
abstractions (see the warning in
[Remember a user across sessions](#remember-a-user-across-sessions)).

**An agent with conversation memory does not compile.** It needs
`UseConversationMemory` and a registered `VectorStore`. The message names the one
that is missing.

## In the reference

- [Agent management HTTP API](/http-api/agents/)
- [`HarnessSettings` API](/api/tracon.harnesssettings/)
- [`CompactionSettings` API](/api/tracon.compactionsettings/)
- [`MemorySettings` API](/api/tracon.memorysettings/)
- [`TraconConversationMemoryOptions` API](/api/tracon.traconconversationmemoryoptions/)
- [`TraconMcpOptions` API](/api/tracon.traconmcpoptions/)

## Read next

- [Sessions and conversations](/concepts/sessions/) — retain conversation state and understand session lifetime.
- [Agents and definitions](/concepts/agents/) — configure and version the definitions that the catalog resolves.
- [Tools, skills, and MCP](/concepts/tools/) — choose code tools, instruction skills, or remote MCP integration.
