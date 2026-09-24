---
title: Write your own judge
description: Implement a safe deterministic or model-backed IRunJudge and verify it with Tracon's executable contract suite.
---

:::note[Preview packages]
Tracon is published as `1.0.0-preview.3`. Use `--prerelease` for discovery or
pin the exact version for reproducible builds.
:::

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

        return new(new RunJudgment
        {
            Scores =
            [
                new JudgeScore
                {
                    Name = Name,
                    Kind = RunScoreKind.Numeric,
                    Value = score,
                    Comment = "Output-length rule.",
                },
            ],
        });
    }
}
```

Register it on the same chain `AddTracon()` returns:

```csharp
tracon.AddRunJudge<ResponseQualityJudge>();
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
and contain only letters, digits, `.`, `_`, or `-`. Tracon uses it in metric
tags and writes scores with `judge:{Name}` as the source and author.

A judge can still be called again for the same run: manual re-scoring always
reruns it, and a queued job retry (triggered when a *different* judge fails)
reruns every judge that has not yet written a score for that run — a judge
that already wrote one is skipped on that retry. Make side effects idempotent.

A judgment carries a **list** of named scores, and each one becomes its own score
row. Return an **empty** list when no decision is possible — that writes nothing.
Do not return `0` for an unknown result; a null `Value` records "measured nothing",
and an empty judgment records "did not measure".

Each score states its own `Kind` and must stay inside that kind's range: `Binary`
is 0 or 1, `Stars` is 1 to 5, `Numeric` is 0 to 100, and `Categorical` carries a
`TextValue` instead of a `Value`. Names must satisfy the same rule as `Name` and
must be distinct within one judgment. A judgment that breaks any of these is
rejected as a whole, before the first row is written, and does not retry.
`Comment` is optional and is stored at a maximum of 4000 characters.

The score named exactly after the judge is its **headline** score. Only that one
feeds the online-evaluation average and the `tracon.judge.score` histogram,
both of which are defined on the 0-100 scale; a judge that reports several metrics
on several scales gives them other names, and they are stored and queryable
without distorting that average.

The cancellation token is the call budget. Tracon applies `JudgeTimeout` to
each judge call, which defaults to 60 seconds. Propagate a real cancellation. Do
not throw `OperationCanceledException` for your own timeout.

`JudgeTimeout` is a **real wait cutoff**, not only a cooperative cancellation
request: if a judge ignores its token and keeps running past the deadline,
Tracon still returns a `judge_timeout` failure at that point instead of
waiting indefinitely. The judge body itself is not killed — it can keep running
in the background and complete later with a success or a fault — but a late
result is discarded: it writes no score, no summary, and no metric, and a late
fault is only logged, never left as an unobserved task exception.

`RunJudgeContext.TenantId` is the authoritative tenant. The context contains the
run input, non-empty output, and distinct tool names. It does not include tool
arguments or results, intermediate steps, run errors, message identifiers, or
session history.

## Starting a run or model call

If a judge starts a Tracon run, set `TraconRunOptions.Kind` to
`RunKind.Eval`. This keeps synthetic traffic out of sampling and cost statistics.
Tracon also suppresses sampling while `JudgeAsync` runs, but that scope does
not cover work that outlives the call.

A model-backed judge must use `IModelProviderRegistry.CreateSetupChatClientAsync`.
That path applies the tenant's egress policy but uses the setup credential, so a
tenant BYOK credential is never charged for control-plane evaluation.

## Verify your implementation

Add the contract package to your test project:

```bash
dotnet add package Tracon.Testing.Contracts.Xunit --prerelease
```

```csharp
using Tracon.Testing.Contracts.Judges;

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
