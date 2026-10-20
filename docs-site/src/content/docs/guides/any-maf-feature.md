---
title: Use any Microsoft Agent Framework feature
description: Reach a Microsoft Agent Framework feature that Tracon does not model, and see which Tracon rings each way keeps and which it loses.
---

Does Tracon block a Microsoft Agent Framework (MAF) feature that it does not model?
No. Every MAF feature runs under Tracon when your code builds the agent. The real
question is what the agent loses on the way. This page puts the ways in order, lightest
first, and says what each one keeps and loses.

Every code block on this page comes from `samples/Tracon.Samples.MafFeatures`. That
sample builds against the packed packages. Its test project checks five claims: the
decorator sees the tool calls of every door, the custom source uses the tenant's own
key, the custom source records cost, the egress policy reaches the custom source and
not the factory agent, and an agent extension adds a context provider to an agent defined
as data without losing the tenant's key or cost.

## The coverage ladder

Four levels say how far a feature reaches into Tracon:

| Level | Meaning |
|---|---|
| Unavailable | You cannot use the feature, or using it turns off the run record or the tenant boundary. |
| Runs | You use the feature from code. The run of the agent is recorded. Some Tracon rings can be lost. |
| Recorded | The events, usage, and cost of the feature enter the run record and the span. The tenant boundary applies. |
| Managed | You turn the feature on with data: a definition, the HTTP API, or the console. Tracon validates it, versions it, and puts it under roles and the audit trail. |

The rule is: code defines, data selects. Anything that executes is registered in
code — a tool, a context provider, middleware. A definition picks it by name. A feature
that you wire in from code reaches **Runs** at once. It does not reach **Managed** by
itself.

## Pick the lightest door

Start at the top of the diagram and stop at the first door that fits.

```mermaid
flowchart TD
    accTitle: Which door reaches a framework feature
    accDescr: A decision tree. A workflow graph goes to a code workflow. A Microsoft.Extensions.AI type that Tracon reads from the container is registered in the container. Agent middleware, tool middleware, or a message context provider goes in an agent decorator. A framework option or a full context provider on an agent that a definition describes goes in an agent extension. Anything else needs an agent that code builds. If you need the tenant key, the egress policy, or cost, use a custom agent source. If you do not, use a factory agent.
    START["A framework feature you want to use"] --> GRAPH{"Is it a workflow graph?"}
    GRAPH -->|yes| WF["Code workflow<br/>AddWorkflow"]
    GRAPH -->|no| DI{"Does Tracon read the type<br/>from the container?"}
    DI -->|yes| REG["Register it<br/>in the container"]
    DI -->|no| MW{"Is it agent middleware, tool middleware,<br/>or a message context provider?"}
    MW -->|yes| DEC["Agent decorator"]
    MW -->|no| OPT{"Is it a framework option or a full context provider,<br/>on an agent that a definition describes?"}
    OPT -->|yes| EXT["Agent extension"]
    OPT -->|no| KEY{"Do you need the tenant key,<br/>the egress policy, or cost?"}
    KEY -->|yes| SRC["Custom agent source"]
    KEY -->|no| FAC["Factory agent"]
```

| Door | Use it when | What you keep | What you lose |
|---|---|---|---|
| Container registration | Tracon reads the type from the container | Everything | Nothing |
| Agent decorator | The feature is agent middleware, tool middleware, or a message context provider | Everything: the model rings, the tenant key, the egress policy, cost | A full context provider |
| Agent extension | The feature is a framework option or a full context provider, and a definition describes the agent | Everything a decorator keeps, plus the definition: console editing, versions, validation | The options the definition owns are protected. A tool that your provider adds skips the tool chain |
| Factory agent | Code builds the agent and you need none of the three below | The run record, the model rings (through the registry), the wrapped tools | The tenant key, the egress policy, cost |
| Custom agent source | Code builds the agent and you need the tenant key, the egress policy, or cost | All of the above, plus the tenant key, the egress policy, and cost | Console editing and the features the compiler wires |
| Code workflow | The feature is a graph | The graph, in full | Events that Tracon does not map |

## Register it in the container

Some Microsoft.Extensions.AI types need no agent at all. You register the type, and
Tracon uses it where it needs one. An `IEmbeddingGenerator` serves knowledge search, an
`ITextToSpeechClient` or `ISpeechToTextClient` serves voice, and an `IEvaluator` serves
evaluation. Register the type with the extension of its own package, then bind it as the
page of the feature says. Each page also says what the feature records:

- [Knowledge and RAG](/guides/knowledge/) — the embedding generator
- [Voice and live conversation](/guides/voice/) — speech clients
- [Evaluation and experiments](/concepts/evaluation/) — evaluators

## Wrap the agent with a decorator

An `IAgentDecorator` wraps every agent that the catalog resolves. A decorator can add
MAF middleware with `AIAgentBuilder`. It can add run middleware, function-invocation
middleware that sees each tool call, or a `MessageAIContextProvider`. The decorator
wraps the agent that the catalog already built, so the agent keeps every Tracon ring.

```csharp
public sealed class ToolCallLogDecorator(ToolCallLog log) : IAgentDecorator
{
    public int Order => 100;

    public AIAgent Decorate(AIAgent agent, AgentDescriptor descriptor)
        => agent.AsBuilder()
            .Use(async (innerAgent, context, next, cancellationToken) =>
            {
                log.Record(descriptor.Name, context.Function.Name);
                return await next(context, cancellationToken).ConfigureAwait(false);
            })
            .Build();
}
```

Give the decorator an `Order` above `0`. Run recording sits at `0` and hands the agents
inside it a plain `AgentRunOptions`. MAF function middleware accepts that type. A
decorator outside run recording receives Tracon's own run options, and the middleware
rejects them.

The decorator sees every agent. To act on one agent only, check `descriptor.Name`. The
sample tests run this decorator against three agents: one defined as data, one from a
factory, and one from a custom source. It sees the tool call of all three.

A decorator cannot add a full context provider, one that adds tools or instructions.
`UseAIContextProviders` takes only message providers. For a full provider on an agent that
a definition describes, use an agent extension. For an agent that code builds, use one of
the doors after it.

The sample registers the decorator together with the log it writes to:

```csharp
builder.Services.AddSingleton<ToolCallLog>();

return builder.AddAgentDecorator<ToolCallLogDecorator>();
```

See [Write your own agent decorator](/guides/write-your-own-agent-decorator/) for the
full contract.

## Add an option or a provider with an agent extension

An agent extension is code that you register once and a definition selects by name. It
reaches the options that Tracon builds the agent from, so it can set a framework option or
add a full `AIContextProvider`. The agent still goes through the definition compiler. It
keeps the model rings, the tenant's own key, the egress policy, and cost, and the console
can still edit and version it.

The sample adds a provider that puts a policy notice into the instructions of every model
call:

```csharp
return builder.AddAgentExtension(
    ExtensionName,
    context =>
    {
        // Add after the providers Tracon built; the compiler refuses a change to them.
        var options = context.ChatAgentOptions!;
        options.AIContextProviders = [.. options.AIContextProviders ?? [], new PolicyNoticeProvider()];
    },
    description: "Adds the refund policy to the instructions of every model call.");
```

```csharp
private sealed class PolicyNoticeProvider : AIContextProvider
{
    protected override ValueTask<AIContext> ProvideAIContextAsync(InvokingContext context, CancellationToken cancellationToken = default)
        => new(new AIContext { Instructions = Notice });
}
```

The sample puts the name in the definition of the agent. A stored definition does the same
with `extensionNames`, and the console offers the registered names in the agent editor:

```csharp
ExtensionNames = [PolicyNoticeExtensionRegistration.ExtensionName],
```

What to know about this door:

- **`Configure` runs when the agent is compiled, not on every run.** The compiled agent is
  cached for each tenant, agent name, and definition, so the runs that follow share it. Two
  runs that start before the cache holds the agent can each compile it. A run with parameter
  values, and an agent of a tenant that holds its own provider key, is not cached and runs
  the extension each time.
- **The extension gets one options object.** `ChatAgentOptions` is set for a chat agent and
  `HarnessAgentOptions` for a harness agent. `context.Use` adds a wrapper around the
  compiled agent, inside Tracon's own wrappers and decorators.
- **Some options are protected.** A definition or a tenant and security decision owns the
  tool chain, the tool approval settings, the instructions and sampling settings (including the seed,
  top-k, stop sequences, penalties, tool mode, and `AllowMultipleToolCalls`), the chat
  history provider, file access, the working-directory skill scan, file memory, web search,
  compaction, the loop, and the sub-agent tree. Some other options are protected because
  they change a protected setting indirectly. `UseProvidedChatClientAsIs` removes the
  framework's default chat client stack, and with it the approval binding and the per-call
  history writer. `AllowConcurrentInvocation` turns on concurrent tool calls in the tool loop
  that Tracon builds. For concurrent tool calls, set `ToolLoop.AllowConcurrentToolCalls` in the model
  binding of the definition. `ChatOptions.RawRepresentationFactory` replaces the model,
  sampling, and provider settings of the definition on the wire. Two options are protected
  because they do not work under Tracon. `EnableMessageInjection` and
  `RequirePerServiceCallChatHistoryPersistence` act on each model call only when they are under
  the tool loop. Tracon's tool loop is inside the model client, so these layers are above it. An
  injected message does not reach the running turn, and the history is written once per turn.
  An extension that changes a protected option fails the compilation with
  an error that names the extension and the option. Tracon does not undo the change. The
  provider list is append-only. The other options are yours, for example
  `ThrowOnChatHistoryProviderConflict`. The check covers the options object. It does not look inside a
  provider or a wrapper that the extension adds, so a provider you add can still read files
  or load skills by itself. Register only extension code that you trust, as you do for a tool.
- **A provider can add tools, and Tracon does not run them through the tool chain.**
  Authorization, approval, and the timeout apply to the tools that the tool registry gives
  an agent. A tool that your own provider adds skips them. The run record still shows the
  call.
- **A replay that plays tools back does not run the extension**, for the same reason.
- **Model options for one run follow the same rule.** Code can run a catalog agent with
  `ChatClientAgentRunOptions`. The run may add instructions and tool declarations, and it may
  turn parallel tool calls off (`AllowMultipleToolCalls = false`), which a handoff step does. A
  model id, a sampling setting, a tool mode, a response format, a chat client factory, or a tool
  that runs throws `ArgumentException` before the run starts. No HTTP endpoint takes model options for one run. To change a run, change the
  definition, run an experiment, or declare a parameter.

## Build the agent in a factory

`AddAgent(name, factory)` takes a factory that returns any MAF `AIAgent`. Tracon records
the run and applies its decorators. The agent skips the definition compiler, so the
factory has to ask for each piece it wants to keep:

```csharp
// The registry builds the model rings: run budget, circuit breaker,
// fallback, response cache, content guards, and the 'chat' span. This
// synchronous call applies no tenant key and no egress policy.
var chatClient = services.GetRequiredService<IModelProviderRegistry>().CreateChatClient(model);

// The registry returns the wrapped tool: authorization, validation,
// timeout, truncation, and approval stay on.
if (!services.GetRequiredService<IToolRegistry>().TryGet(SearchOrdersToolRegistration.ToolName, out var searchOrders))
{
    throw new InvalidOperationException($"The tool '{SearchOrdersToolRegistration.ToolName}' is not registered.");
}

return chatClient.AsAIAgent(new ChatClientAgentOptions
{
    Name = AgentName,
    ChatOptions = new ChatOptions
    {
        Instructions = "Answer questions about orders. Look orders up with the search tool.",
        Tools = [searchOrders],
    },

    // Tracon's history store, so the console and /v1/conversations show the
    // turns. With SQL storage, leaving this out leaves both of them empty.
    ChatHistoryProvider = services.GetService<ChatHistoryProvider>(),

    // Any framework provider or option is now yours.
    AIContextProviders = [new TodoProvider(new TodoProviderOptions())],
});
```

What a factory agent keeps, and how:

| Piece | Kept? | How |
|---|---|---|
| Run row, events, tool-call record, usage, cancellation, webhooks, error class, input record, replay | Yes | Automatic. Run recording wraps every agent. |
| `invoke_agent` span and saved approval rules | Yes | Automatic. |
| Sessions, `/v1/*`, A2A and MCP publication, being called as a sub-agent | Yes | Automatic. |
| The model rings | Yes | Build the chat client with `IModelProviderRegistry.CreateChatClient`. |
| The tool chain: authorization, validation, timeout, truncation, approval flag | Yes | Take the tool from `IToolRegistry.TryGet`. |
| History in the console and in `GET /v1/conversations/{id}/items` | Yes | Pass the `ChatHistoryProvider` from the container. |
| The tenant's own provider key and the egress policy | **No** | The factory is synchronous. Use a custom agent source. |
| Cost, model breakdown, and cost quota | **No** | The descriptor of a factory agent names no model. Use a custom agent source. |

Three facts about this door:

- **The history provider matters on SQL storage.** With the in-memory store, the turn
  shows in the console and in `/v1/conversations/{id}/items` whether or not the agent
  uses the provider from the container. With a SQL store, both stay empty unless the
  agent uses it. The sample passes it.
- **The factory runs on every resolve.** Keep it cheap, and keep state out of it.
- **Tracon warns about a bypassed registry.** If the chat client of a factory agent did
  not come from `IModelProviderRegistry`, Tracon writes one warning for that agent name
  the first time the catalog resolves it. An agent that exposes no chat client gets no
  warning.

## Build the agent in a custom source

A factory is synchronous, and its descriptor names no model. An `IAgentSource` has
neither limit. Its `ResolveAsync` is asynchronous, so it can call
`CreateChatClientAsync`, which applies the tenant's own provider key and egress policy.
Its `ListAsync` returns a descriptor that names the model, and run recording prices the
run from that model. The sample describes its agent like this:

```csharp
// The model named here is what run recording prices. Leave it out and the
// run is recorded with no model and no cost.
private AgentDescriptor Descriptor => new()
{
    Name = AgentName,
    Description = "A custom-source agent that answers questions about invoices.",
    Origin = AgentDefinitionOrigin.Custom,
    SourceName = SourceName,
    Model = model,
    ToolNames = [SearchOrdersToolRegistration.ToolName],
};
```

`ResolveAsync` then builds the agent:

```csharp
// The asynchronous call applies the tenant's own provider key and the tenant's
// egress policy, on top of the model rings.
var chatClient = await models.CreateChatClientAsync(model, cancellationToken).ConfigureAwait(false);

if (!tools.TryGet(SearchOrdersToolRegistration.ToolName, out var searchOrders))
{
    throw new InvalidOperationException($"The tool '{SearchOrdersToolRegistration.ToolName}' is not registered.");
}

return chatClient.AsAIAgent(new ChatClientAgentOptions
{
    Name = AgentName,
    ChatOptions = new ChatOptions
    {
        Instructions = "Answer questions about invoices. Look orders up with the search tool.",
        Tools = [searchOrders],
    },
    ChatHistoryProvider = history,
});
```

The sample tests run this agent for a tenant that has its own key. The chat client is
built with that key, and the run record carries the model and a cost. The same tests run
the factory agent for the same tenant: its client is built with the setup key, and its
run record has no model and no cost. They also set an egress policy that forbids the
provider. The custom source is refused. The factory agent still runs.

A custom source does not run the definition compiler. It owns the whole shape of the
agent. Do not cache an agent whose client carries a tenant's own key across tenants.
[Write your own agent source](/guides/write-your-own-agent-source/) explains the
lifecycle, the priority, and the contract tests.

## Build the workflow in code

`AddWorkflow(name, factory)` runs a MAF `WorkflowBuilder` graph. Every builder feature
runs, and Tracon records a root run. Tracon maps the started, output, error, warning,
super-step, executor, request, and agent-response-update events into the run record. It
does not map the other events. Examples are the plan and ledger events of a Magentic
workflow and an event class that you define. Tracon writes those to the log as a warning
and does not record them in the run.

If you write executors with MAF source-generated handlers (`[MessageHandler]`), add the
`Microsoft.Agents.AI.Workflows.Generators` analyzer package to your own project.
Tracon does not reference it for you.

See [Workflows](/concepts/workflows/) for the registration key, the helpers that give a
code graph the time limit, retry, and budget of a stored one, and the full recording
model.

## What you cannot win back today

These losses have no door that removes them:

- **A protected option on an agent defined as data.** An agent extension can set any
  framework option that the definition and the tenant do not own. The tool chain, the chat
  history provider, the approval settings, the file access and skill-scan options, and the model
  settings stay with Tracon as options. A feature that needs one of them needs an agent that your code builds. That agent is code, so the
  console cannot edit it.
- **The tool chain for a tool that your own provider adds.** An extension's provider can
  add a tool, and that tool skips authorization, approval, and the timeout. Register the
  tool in the tool registry instead, and name it in the definition.
- **Tracon's managed skills, compaction, MCP resources, and loop recording on a
  code-built agent.** The definition compiler wires skills with their script gates,
  context compaction with its record, MCP tools and resources, loop recording, and the
  budget of the agent tree. It does this only when it compiles a definition. A factory
  agent or a custom-source agent gets none of them from Tracon.
- **A file store that is scoped to the tenant and the agent.** The agent file store in
  the container is not scoped to an agent. The in-memory default has no tenant prefix
  and no agent prefix. The SQL store is scoped to the tenant, but all agents share it.
  Do not give the store from the container to an agent that serves more than one
  tenant. Scope it yourself.
- **Everything a definition gives.** A code-built agent cannot take parameterized,
  per-culture, or shared instructions. It has no versions, experiments, rollback, or
  validation when you save, and you cannot edit it in the console.
- **The tenant key, the egress policy, and cost on a factory agent.** A custom agent
  source gets all three.

## Checklist

Answer these five questions before you write a factory agent:

1. Can a decorator or an agent extension do the job? If the feature is middleware, a
   message provider, a framework option, or a full context provider, one of them can, and
   you keep every ring.
2. Do you need the tenant's own key, the egress policy, or cost? If yes, use a custom
   agent source.
3. Does the factory build its chat client with `IModelProviderRegistry`? If not, the
   model rings are off, and Tracon logs a warning.
4. Does the agent take its tools from `IToolRegistry` and its history from the
   container? If not, tool policy is off, and the console may show an empty history.
5. Can the agent live as code? Without console editing, versions, and rollback, and
   without Tracon's skills, compaction, and MCP resources, is it still the agent you
   want?

## Read next

- [Write your own agent decorator](/guides/write-your-own-agent-decorator/) — the decorator door in full, with ordering rules
- [Write your own agent source](/guides/write-your-own-agent-source/) — the source contract, priority, and tenancy
- [Agents and definitions](/concepts/agents/) — where the catalog gets its agents
