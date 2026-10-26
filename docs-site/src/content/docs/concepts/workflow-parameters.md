---
title: Workflow parameters
description: Declare run parameters on a workflow, bind them into each agent step, read them in a function node, and have a resume or an answer reuse them.
sidebar:
  order: 8
---

How do one workflow definition serve many tones, voices or customers without being copied? A workflow declares run
parameters, a run supplies the values, and every step receives the part of them it declared.

```mermaid
flowchart LR
    accTitle: How a run's parameter values reach the steps
    accDescr: The caller supplies values with the run. The workflow's schema checks them before the stream opens. Each agent step is bound with the names its own schema declares, and a function node reads the full set. The run records its final values so a resume or an answer to a waiting request uses them again.
    CALL["Run request<br/>with values"] --> CHECK{"Workflow schema<br/>check"}
    CHECK -->|"invalid"| BAD["400 before the stream"]
    CHECK -->|"valid"| RUN["Run records<br/>the final values"]
    RUN --> AGENT["Agent step:<br/>its own names only"]
    RUN --> FN["Function node:<br/>GetRunParameters()"]
    RUN --> AGAIN["Resume or answer<br/>reads them back"]
```

A stored workflow can declare run parameters, the same `AgentParameter` an agent
declares (name, kind, `Required`, `DefaultValue`, `Description`). The values a run
supplies reach every agent step, so one definition serves many tones, voices or
customers without being copied:

```bash
curl -sS -X PUT http://localhost:5081/tracon/api/workflows/podcast \
     -H 'Content-Type: application/json' -d '{
       "kind": "Sequential",
       "agentNames": ["podcast-writer", "podcast-editor"],
       "parameters": [
         { "name": "tone",  "kind": "Text", "required": true },
         { "name": "voice", "kind": "Text", "defaultValue": "alloy" }
       ]
     }'

curl -N -X POST http://localhost:5081/tracon/api/workflows/podcast/run \
     -H 'Content-Type: application/json' \
     -d '{"message":"Episode 12","parameters":{"tone":"formal","voice":"nova"}}'
```

- **The workflow's schema is the contract.** A missing required value, a name the
  workflow does not declare, or a value over `MaxParameterValueLength` is refused with
  `400` before the stream opens, with the same body an agent run gives
  (`missingParameters`, `unknownParameters`, `tooLongParameters`). The runner checks
  again, so a caller that skips HTTP gets the same refusal.
- **A step receives the values its own schema names.** `podcast-writer` declares only
  `tone`, so it never sees `voice`; `podcast-editor` declares both. A name the step does
  not declare is dropped for that step, and a value the workflow supplies wins over the
  step's own default. The step is bound the way a direct run of that agent would be
  (its instructions, its recording, its tool-approval gate), and its own run row records
  the values it was bound with.
- **Every participant is checked before the first model call.** If an agent in the
  workflow needs a required parameter that has no default and that the workflow does not
  supply, the run ends `Failed` without a single model call, and the error names the
  agent and the parameter. Saving the workflow does not check this, because the agents
  can change after it is saved.
- **A function node reads the values** with `context.GetRunParameters()`, a read-only
  dictionary with the defaults applied:

```csharp
tracon.AddWorkflowFunction<List<ChatMessage>, List<ChatMessage>>(
    "narrate",
    services => (messages, context, cancellationToken) =>
    {
        var voice = context.GetRunParameters().GetValueOrDefault("voice", "plain");
        return ValueTask.FromResult<List<ChatMessage>>([new(ChatRole.User, $"[{voice}] {messages[^1].Text}")]);
    });
```

  Called outside a workflow run it throws `InvalidOperationException` instead of
  returning an empty set, so a forgotten setup is not read as "no values".
- **Resume and respond use the same values.** The run records its final values, and a
  resume from a checkpoint or an answer to a waiting request reads them back and passes
  them on to the run it opens. If the version the run continues with declares parameters
  but the run recorded none, the resume is refused — the agents would otherwise continue
  under their defaults, halfway through the work. That happens when
  `RunRecording:RecordRunInput` is off, so a workflow with parameters that waits for a
  human needs it on. The values are read against the schema of the version the run
  continues with, which by default is the one it recorded (see
  [Versions, rollback and pinning an agent step](/concepts/workflows/#versions-rollback-and-pinning-an-agent-step)).
  When you name a newer version, a name its schema no longer declares is dropped and a
  required name it gained since is refused.
- **A workflow defined in code declares its schema in its registration**
  (`WorkflowRegistrationOptions.Parameters`), checked, queued and recorded like a stored one's.
  Without a schema every value sent is refused.
- **A parameter can choose the version of an agent step built in code**
  (`step.AgentVersionParameter`; see [below](#let-a-run-choose-the-version-of-an-agent-step)).

A workflow row stores no message: the values are the only input it records.

## Let a run choose the version of an agent step

A step of a graph built in code can pin its agent in the graph (`step.AgentVersion = 4`), or read the version
from a run parameter, so one graph serves every version:

```csharp
tracon.AddWorkflow(
    "episode",
    services =>
    {
        var writer = services.GetWorkflowAgentStep("episode", "writer", step => step.AgentVersionParameter = "writerVersion");

        return new WorkflowBuilder(writer).WithOutputFrom(writer).WithName("episode").Build();
    },
    options => options.Parameters = [new AgentParameter { Name = "writerVersion", Kind = AgentParameterKind.Number }]);
```

```bash
curl -N -X POST http://localhost:5081/tracon/api/workflows/episode/run \
     -H 'Content-Type: application/json' \
     -d '{"message":"Episode 12","parameters":{"writerVersion":"2"}}'
```

- The step reads the value every time it runs. The factory of a graph built in code runs before a run's
  parameters are bound, so it cannot read them; the step does.
- An empty value, or no value, runs the agent's current version. A whole number from 1 runs that version,
  and the step's own run row records it.
- Declare the parameter optional, with no default, when a run may leave it out. A required one is refused
  with `400` at the start of a run that omits it.
- A value that is not a whole number from 1, a parameter name the workflow does not declare, a version the
  agent does not have and an agent defined in code (it keeps no history) fail the step by name before its
  model call. The error for a value that is not a number, and for a name the workflow does not declare,
  names the parameter and never repeats the value; a version the agent does not have is named by its
  number. Earlier steps have already run.
- `AgentVersion` and `AgentVersionParameter` exclude each other: setting both throws when the graph is built.
- A run that continues after a human answer, or a resume, reads the value its first part started with. A
  number keeps that version through the wait, so an edit made while the run waited does not change the
  version its later steps run. An empty value still means the current version, resolved when each step
  runs, so a later step runs an edit made during the wait.
- Whoever may start the run chooses the version, and any stored version of the agent can be chosen. Pass the
  value from your own code, not from an end user's input, unless every version is meant to be reachable.
- A stored workflow has no such setting: a node's `agentVersion` is fixed (see
  [Pin an agent step to a version](/concepts/workflows/#pin-an-agent-step-to-a-version)), and a run chooses the
  version of the whole workflow with `definitionVersion`.

## Read next

- [Workflows](/concepts/workflows/): the patterns, function nodes, versions and recovery these values flow through.
- [A workflow that a person can send back](/guides/review-and-revise-workflow/): parameters on a graph built in code.
