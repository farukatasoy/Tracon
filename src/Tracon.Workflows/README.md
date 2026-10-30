# Tracon.Workflows

Workflow execution engine for Tracon. It chains catalog agents together
with ready-made patterns, records every run in the `runs` table, and makes
runs resumable from checkpoints.

## Setup

```csharp
builder.AddTracon()
       .UseOpenAI(apiKey)
       .UsePostgreSql(connectionString)
       .UseWorkflows();
```

`UsePostgreSql` comes from the `Tracon.PostgreSql` package; add it with `dotnet add package Tracon.PostgreSql --prerelease`. Without a storage package every store is in memory.

## Workflows defined from the UI

A workflow definition is a **graph**, not code: it only carries the names of
catalog agents and a pattern.

| Pattern | What it does |
|-------|----------|
| `Sequential` | Agents run in order; each output feeds the next input |
| `Concurrent` | Agents run at the same time; the results are merged |
| `Handoff` | The first agent starts the work, handing off to others as needed |
| `GroupChat` | A round-robin manager distributes the turn |
| `Magentic` | A manager agent builds a plan, tracks progress, and replans |

```http
PUT /tracon/api/workflows/review
Content-Type: application/json

{
  "kind": "Sequential",
  "agentNames": ["researcher", "writer", "editor"]
}
```

## Workflows defined in code

A free-form graph — custom `Executor` types, conditional edges, sub-workflows
— is defined only in code:

```csharp
builder.AddWorkflow("custom-graph", services =>
{
    var start = ExecutorBindingExtensions.BindAsExecutor<string, string>(
        input => input.ToUpperInvariant(), id: "uppercase");

    return new WorkflowBuilder(start).WithName("custom-graph").Build();
});
```

A graph built in code writes no spans unless its builder calls `.WithOpenTelemetry()`. With the call, its
steps (`executor.process <id>`) join the trace of the run, next to the agent and chat spans; without it the
trace is what it was. Pass no `ActivitySource`: the run trace listens to the framework's own source and to
`Tracon`, and a source of your own is not read.

## Answering a request once

A graph that asks a person through a `RequestPort` waits as `AwaitingInput`, and
`POST /api/workflows/runs/{runId}/respond` continues it in a new run. By default every
answer opens a new run (`WorkflowResponseMode.Branch`). A workflow that must act on one
decision per request takes `SingleAnswer`:

```csharp
builder.AddWorkflow("publish-approval", services => BuildApprovalGraph(services), options =>
{
    options.Description = "Asks an editor before publishing.";
    options.ResponseMode = WorkflowResponseMode.SingleAnswer;
});
```

The same answer given twice — a double click, a retried call — follows the run the first
one started; a different answer is refused with `409`. A stored definition sets
`responseMode` the same way. Every answer that starts a run is written to the audit
trail as `workflow.respond` before the run starts, and refused when that write fails.
`GET /api/workflows/runs/{runId}/responses` lists who answered and which run each answer
started; the answer itself is kept only as a SHA-256.

## Listing waiting requests and limiting how long they wait

`GET /api/workflows/requests` lists the human requests that the workflow runs of a tenant wait
on, one item per request: `state=pending` (the default) hides the answered ones, and each item
names the workflow, the expiry and the run to answer. A workflow can give its requests a time
limit, as `requestTimeout` on a stored definition or in code:

```csharp
builder.AddWorkflow("publish-approval", services => BuildApprovalGraph(services), options =>
{
    options.RequestTimeout = TimeSpan.FromDays(7);
});
```

When the time passes, the run that waits is closed (`Failed` with the error type
`WorkflowRequestExpired`, or `Completed` when every request was answered), and the request
event, checkpoints and run input row it kept can be deleted by retention. A run that fails this
way publishes `run.failed` to the webhooks subscribed to it. A request without a limit waits
without limit.

## A tool that needs approval

An agent in a workflow can call a tool that needs approval. Where a ready-made pattern hosts the
agent (a stored `Sequential`, `Concurrent`, `Handoff`, `GroupChat` or `Magentic` definition with `agentNames`, or a
code graph that binds the agent with `GetWorkflowAgent`), the workflow waits: the request has the form `ToolApproval`, and its prompt
names the tool and adds what the registered `IToolApprovalPresenter` says about the call. Answer
it on the same endpoint as any other request, with `approved`, an optional reason in `text`, and
optionally `remember` to save a rule for that agent and tool:

```bash
curl -X POST "$TRACON/api/workflows/runs/$RUN/respond" \
  -H "Content-Type: application/json" \
  -d '{"requestId": "'"$REQUEST"'", "approved": true, "text": "checked", "remember": true}'
```

The answer carries a decision and nothing else: the tool call comes from the request the workflow
recorded, so a `data` value is refused with `400`. The decision is written to the audit trail as
`approval.decision` before the workflow continues. An agent node of a `Sequential` node list and a
step bound with `GetWorkflowAgentStep` cannot wait, and neither can a `Magentic` participant: a tool that
needs approval fails the run there. A tool approval is answered once whatever the workflow's response mode.

## Graphs built in code: the same node capabilities

A graph with a way back (a rejected draft returns to its writer) or a human gate is built in
code with `AddWorkflow` and MAF's `WorkflowBuilder`. Two helpers hand its steps what a stored
chain's steps have:

```csharp
builder.AddWorkflow("review-and-revise", services =>
{
    var writer = services.GetWorkflowAgentStep("review-and-revise", "writer", step => step.Timeout = TimeSpan.FromMinutes(3));
    var validate = services.GetWorkflowFunction("validate");
    var render = services.GetWorkflowFunction("render", step => step.Timeout = TimeSpan.FromMinutes(5));

    return new WorkflowBuilder(writer)
        .AddEdge(writer, validate)
        .AddEdge(validate, render)
        .WithOutputFrom(render)
        .WithName("review-and-revise")
        .Build();
},
options =>
{
    options.RunTimeout = TimeSpan.FromMinutes(5);
    options.Parameters = [new AgentParameter { Name = "productionId", Kind = AgentParameterKind.Text, Required = true }];
});
```

- `GetWorkflowFunction` binds a function registered with `AddWorkflowFunction`: its time limit, retry,
  the budget gate, a cost line that names the node and the strict read of its settings
  (`step.Settings`, or typed with `step.SetSettings(value)`).
- `GetWorkflowAgentStep` binds a catalog agent as a plain step with its own timeout and version pin. Use
  it, not `GetWorkflowAgent`, for a step a message can return to over a back edge. `step.AgentVersion` fixes
  the version for every run; `step.AgentVersionParameter` names a run parameter that carries it, read each time
  the step runs (empty runs the current version).
- `WorkflowFunctionOptions.MaxConcurrency` limits how many calls of a function run at once in the
  process, across workflows.
- `WorkflowRegistrationOptions.RunTimeout` is held to `Tracon:Workflows:MaxRunTimeout` when the host
  starts.

The guide <https://tracon.dev/guides/review-and-revise-workflow/> builds a full graph with a bounded
revision loop.

## Running

```http
POST /tracon/api/workflows/review/run
{ "message": "Review the Q3 report" }
```

The response is SSE. Every frame carries a `RunEvent`; the first frame reports
the run id. Every agent called inside the workflow opens its own `runs` row
and attaches under the workflow row — `GET /api/runs/{id}/tree` returns the
whole tree.

A streamed run belongs to its connection: a client that stops reading cancels it.
To run in the background, send `Prefer: respond-async`. The caller is checked the
same way, the answer is `202 Accepted` with the run id, and a worker runs the
workflow; read its events from `GET /api/runs/{id}/events`. The resume and respond
endpoints queue the same way, and `Idempotency-Key` is accepted on all three only
together with `Prefer: respond-async`.

```http
POST /tracon/api/workflows/review/run
Prefer: respond-async
Idempotency-Key: q3-review
{ "message": "Review the Q3 report" }
```

## Function nodes and their settings

A `Sequential` workflow can mix functions registered in code in with its agents. Each
node takes what the node before it passes on: an agent node takes and passes on a
`List<ChatMessage>`, and a run starts by sending the user's message as one. A save that
breaks this is a `400` naming both nodes; without the check, Microsoft Agent Framework
would drop the message and the run would end with no output. A function that takes
**settings** serves several workflows with a different configuration in each:

```csharp
builder.AddTracon()
       .UseWorkflows()
       .AddWorkflowFunction<List<ChatMessage>, AudioClip, TtsSettings>(
           "tts",
           provider => (messages, settings, context, cancellationToken) =>
               provider.GetRequiredService<ITextToSpeech>()
                       .SpeakAsync(messages[^1].Text, settings.Voice, settings.LoudnessLufs, cancellationToken),
           options => options.Description = "Text to speech");

public sealed record TtsSettings
{
    public required string Voice { get; init; }

    public double LoudnessLufs { get; init; } = -16;
}
```

```http
PUT /tracon/api/workflows/narrated
{"kind":"Sequential","nodes":[
  {"name":"writer","kind":"Agent"},
  {"name":"tts","kind":"Function","settings":{"voice":"alloy","loudnessLufs":-14}}]}
```

The code declares the shape and an operator fills in values, in the console or over
HTTP. A save reads the settings into `TtsSettings`: an unknown field, a missing
`required` member or a value of the wrong type is a `400` that names the node and the
member. The same read repeats when a run starts, so settings saved against an older
shape fail the run by name instead of running with a default. A change applies to the
next run; a run that waits to be resumed keeps the version it started with (see
[Versions](#versions-rollback-and-pinning-an-agent-step)). **Settings are stored in the definition and shown to everyone who can read
the workflow: never put a secret in them** - name its configuration key instead.
`GET /tracon/api/workflows/functions` lists each function with the JSON schema of its
settings.

## Reporting what a node spent beyond tokens

A function node that speaks, renders or transcribes spends something that is not a token.
Tracon cannot see that work, so the node reports it with `context.ReportUsageAsync`:

```csharp
builder.AddTracon()
       .UseWorkflows()
       .AddWorkflowFunction<List<ChatMessage>, string>(
           "narrate",
           provider => async (messages, context, cancellationToken) =>
           {
               var text = messages[^1].Text;

               // ... synthesize the speech with your provider, then report what it billed.
               await context.ReportUsageAsync(new ToolCallUsage
               {
                   Unit = ToolUsageUnits.Characters,
                   Quantity = text.Length,
                   Cost = text.Length * 0.00003m,
                   Currency = "USD",
                   Provider = "elevenlabs",
                   Model = "eleven_multilingual_v2",
               });

               return text;
           });
```

Every call is one item, written to the run at once and not cancelled with it: a run that
fails or is cancelled keeps what its node had spent, and a retried node keeps the items of
its failed attempts. `GET /tracon/api/runs/{runId}/usage` lists them in the order reported.
The call returns `false` instead of throwing outside a Tracon workflow run, for an invalid
item (an empty `Unit`, a negative quantity or amount) and after the run holds 1,000 items;
report once per step, not per chunk.

Tracon never prices the work: `Cost` is the amount the node computed. Amounts in
`Tracon:Pricing:Currency` add up into the run's `nonTokenCost` (and `treeNonTokenCost`
for the run and every run below it), a total beside the token cost that the call-tree
budget (`Tracon:AgentGraph:MaxTotalCost`), the tenant's cost quota and the `run.completed`
webhook count. A quantity is never added across units, and an item with no amount, or in
another currency, is listed and counted as not added: unknown, not free. A run that
reported nothing has no `nonTokenCost`, which is not zero.

The same budget stops a function node from **starting**. Once `Tracon:AgentGraph:MaxTotalCost`
or `Tracon:AgentGraph:MaxDuration` is spent, the next function node does not run and the run
ends `Failed` with `run_budget_exceeded`. A node that is already running is never stopped, so
a tree can pass its ceiling by as much as its last node spends. The token budget does not stop
a node. A node that spends nothing and must still run after a costly one, such as a step that
stores what the budget just paid for, opts out in code with
`options.StartsWhenBudgetIsSpent = true`; an operator cannot set it in a definition.

## Parameters

A stored workflow can declare run parameters (`WorkflowDefinition.Parameters`, the
same `AgentParameter` an agent declares). A run supplies values; each agent step is
bound with the values **its own schema** names, and a function node reads them with
`context.GetRunParameters()`:

```csharp
builder.AddTracon()
       .UseWorkflows()
       .AddWorkflowFunction<List<ChatMessage>, List<ChatMessage>>(
           "narrate",
           provider => (messages, context, cancellationToken) =>
           {
               var voice = context.GetRunParameters()["voice"];
               return ValueTask.FromResult<List<ChatMessage>>(
                   [new ChatMessage(ChatRole.User, $"{voice}: {messages[^1].Text}")]);
           });
```

```http
POST /tracon/api/workflows/podcast/run
{"message":"Episode 12","parameters":{"tone":"formal"}}
```

A missing required value, an undeclared name or an over-long value is a `400` before
the stream opens. The run records its final values, and a resume or an answer to a
waiting request uses them again. A workflow defined in code declares its schema in
`WorkflowRegistrationOptions.Parameters`.

## Versions, rollback and pinning an agent step

Every save of a stored workflow appends a version, and the old ones stay readable:

```http
GET  /tracon/api/workflows/podcast/versions
GET  /tracon/api/workflows/podcast/versions/2/diff/3
POST /tracon/api/workflows/podcast/rollback
{"version":2}
```

A rollback writes version 2's content as a new version; it never rewrites the history. It is
judged like a save, so content that names a function that is no longer registered, or an
agent version that no longer exists, is a `400` and nothing is written. A version number is
never reused: deleting a workflow keeps its history, and saving the name again continues
the numbering.

A run's row records the definition version it was compiled from (the `agentVersion` field of a
workflow run). `resume` and `respond` compile that version.

A step in a `nodes` chain can pin the version of its agent:

```json
{"kind":"Sequential","nodes":[
  {"name":"writer","kind":"Agent","agentVersion":4},
  {"name":"tts","kind":"Function"}]}
```

The step runs version 4 of `writer` while the agent is edited, and its run row records
version 4. Only an agent stored through the management API has versions: an agent defined
in code cannot be pinned, nor can a version below 1 or one the agent does not have. A step
with no `agentVersion` runs the agent's current version each time. A pin does not reach into
the agent's own sub-agents or skills.

## Timeouts

A function can be registered with a timeout. When it passes, the handler's token is
cancelled and the run ends `Failed` with the error type `workflow_function_timeout`;
the run stops waiting even for a handler that ignores its token, so pass the token on:

```csharp
builder.AddTracon()
       .UseWorkflows()
       .AddWorkflowFunction<List<ChatMessage>, List<ChatMessage>>(
           "render-audio",
           provider => (messages, context, cancellationToken) => RenderAsync(messages, cancellationToken),
           options => options.Timeout = TimeSpan.FromMinutes(2));
```

A stored definition can give a node its own `timeout`, which wins over the
registration's; on an agent node of a node list it replaces
`Tracon:AgentGraph:ChildDeadline` for that step. A definition's `runTimeout` shortens
`RunTimeout`, or lengthens it up to `MaxRunTimeout`. Passing `null` as the third
argument of `AddWorkflowFunction` is ambiguous between the description and the options
callback; name the argument (`description: null`).

## Checkpoints

A checkpoint is written at every super-step. A run that stopped partway
through can be resumed from where it left off:

```http
GET  /tracon/api/workflows/runs/{runId}/checkpoints
POST /tracon/api/workflows/runs/{runId}/resume
```

Durable resuming requires `UsePostgreSql()`; in an in-memory setup,
checkpoints are limited to the lifetime of the process.

A resume compiles the workflow definition **version the run recorded**, not the current
one, so an edit made while the run waited changes nothing for it. To carry the run over a
fix, name the edited version:

```http
POST /tracon/api/workflows/runs/{runId}/resume
{"definitionVersion":3}
```

A version whose graph differs from the checkpoint's fails the run with "the graph's
structure differs"; a version the workflow does not have is a `400`.

A run that a job started — a queued run, or an item of a `tracon.workflow` batch —
can be resumed by that job after a crash, a deploy or a graceful stop:

```csharp
builder.AddTracon()
       .UsePostgreSql(connectionString)
       .UseWorkflows(options =>
       {
           options.ResumeInterruptedRuns = true;
           options.MaxResumeAttempts = 1;
       });
```

When the job is leased again, a run that had finished is left alone, and an
interrupted one continues from the latest checkpoint in its chain as a new run whose
`ContinuedFromRunId` names it. The step that was running when the process stopped
runs again, so a function step with side effects must be idempotent. A run bound to an
HTTP connection is never resumed automatically, and neither is a run its job's time
limit stopped (error type `job_max_duration_exceeded`): the limit ends the work.

## Limits

| Setting | Default | What it does |
|------|-----------|----------|
| `MaxConcurrentRuns` | 4 | The number of workflows running at the same time, shared by every tenant. A run that waits for a slot reads `Queued` |
| `MaxConcurrentRunsPerTenant` | not set | The most runs one tenant has going at once, from 1 to `MaxConcurrentRuns`. A tenant's run waits for its tenant slot before it takes a shared one, and a worker leases queued workflow jobs of a tenant under the limit first (a soft count across processes) |
| `RunTimeout` | 10 min | The maximum duration of a single run, the wait for a slot included. Cooperative: checked between super-steps, so a node without a timeout of its own that ignores its cancellation token finishes and the run stops at the next boundary |
| `MaxRunTimeout` | not set | The longest run timeout a definition may set; unset, a definition may only shorten `RunTimeout` |
| `MaxSuperSteps` | 100 | Infinite-loop protection |
| `MaxNodeSettingsLength` | 16384 bytes | The largest JSON a function node's settings may be when a workflow is saved |
| `EnableCheckpointing` | `true` | Checkpoint writing |
| `ResumeInterruptedRuns` | `false` | A job resumes the workflow run it was running when its process stopped |
| `MaxResumeAttempts` | 1 | The continuations one interrupted run may get, from 0 to 100. A queued run's job gets two more attempts than this; the last one closes the chain |

This package is **not AOT compatible**: the execution engine uses reflection.
Other Tracon packages are not affected.

## Licence

PolyForm Small Business 1.0.0 - free below 100 people and 1,000,000 USD
(2019, inflation adjusted) revenue; a commercial licence applies above that. Terms
ship in the package as LICENSE.md. Details: <https://tracon.dev/reference/licensing/>

## Links

- Guide: <https://tracon.dev/concepts/workflows/>
- Capability map: <https://tracon.dev/capabilities/>
- API reference: <https://tracon.dev/api/>
