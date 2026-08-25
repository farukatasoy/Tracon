---
title: Write your own judge
description: Implement a safe deterministic or model-backed IRunJudge and verify it with AgentPrism's executable contract suite.
---

`IRunJudge` scores a completed production run. A judge can be deterministic or
model-backed. It is registered as a singleton and can receive concurrent calls, so
keep per-run state local to `JudgeAsync`.

```csharp
public sealed class ResponseQualityJudge : IRunJudge
{
    public string Name => "response-quality";

    public ValueTask<RunJudgment> JudgeAsync(
        RunJudgeContext context,
        CancellationToken cancellationToken = default)
    {
        var score = context.Output.Length >= 40 ? 90 : 45;
        return new(new RunJudgment { Score = score, Reason = "Output-length rule." });
    }
}
```

Register it on the same chain `AddAgentPrism()` returns:

```csharp
agentPrism.AddRunJudge<ResponseQualityJudge>();
```

Repeating `AddRunJudge<TJudge>()` for the same implementation type has no effect
— the container creates and owns one singleton. Two other overloads exist for a
judge that needs constructor arguments or per-instance configuration:
`AddRunJudge(IRunJudge judge)` registers a configured instance you own, and
`AddRunJudge(Func<IServiceProvider, IRunJudge> factory)` registers a
container-owned factory result; unlike the generic overload, different configured
instances or factories are all preserved side by side — only their `Name` values
must stay unique. All three are singletons: the judge must be thread-safe because
evaluations can overlap, including when a timed-out call finishes after a retry
has already started.

## Runtime contract

`Name` must be stable, unique without case sensitivity, at most 64 characters,
and contain only letters, digits, `.`, `_`, or `-`. AgentPrism uses it in metric
tags and writes scores with `judge:{Name}` as the source and author.

A judge can be called again for the same run when a different judge makes the
online-evaluation job retry. Make side effects idempotent. Return a score from 0
to 100, or `null` when no decision is possible. Do not return `0` for an unknown
result. Scores outside that range are rejected and do not retry. `Reason` is
optional and is stored at a maximum of 4000 characters.

The cancellation token is the call budget. AgentPrism applies `JudgeTimeout` to
each judge call, which defaults to 60 seconds. Propagate a real cancellation. Do
not throw `OperationCanceledException` for your own timeout.

`JudgeTimeout` is a **real wait cutoff**, not only a cooperative cancellation
request: if a judge ignores its token and keeps running past the deadline,
AgentPrism still returns a `judge_timeout` failure at that point instead of
waiting indefinitely. The judge body itself is not killed — it can keep running
in the background and complete later with a success or a fault — but a late
result is discarded: it writes no score, no summary, and no metric, and a late
fault is only logged, never left as an unobserved task exception.

`RunJudgeContext.TenantId` is the authoritative tenant. The context contains the
run input, non-empty output, and distinct tool names. It does not include tool
arguments or results, intermediate steps, run errors, message identifiers, or
session history.

## Starting a run or model call

If a judge starts an AgentPrism run, set `AgentPrismRunOptions.Kind` to
`RunKind.Eval`. This keeps synthetic traffic out of sampling and cost statistics.
AgentPrism also suppresses sampling while `JudgeAsync` runs, but that scope does
not cover work that outlives the call.

A model-backed judge must use `IModelProviderRegistry.CreateSetupChatClientAsync`.
That path applies the tenant's egress policy but uses the setup credential, so a
tenant BYOK credential is never charged for control-plane evaluation.

## Verify your implementation

Add the contract package to your test project:

```bash
dotnet add package AgentPrism.Testing.Contracts.Xunit --prerelease
```

```csharp
using AgentPrism.Testing.Contracts.Judges;

public sealed class ResponseQualityJudgeTests : RunJudgeContract
{
    protected override ValueTask<IRunJudge> CreateJudgeAsync()
        => new(new ResponseQualityJudge());
}
```

The inherited tests verify identifier validity, score bounds, blank-output
handling, and concurrent calls. They do not test pipeline behavior such as retries,
timeouts, persistence, or HTTP responses.

## Read next

- [Evaluation](/concepts/evaluation/) — online sampling and manual scoring
- [Configuration](/reference/configuration/) — `JudgeTimeout` and sampling gates
- [Testing](/guides/testing/) — test package guidance
