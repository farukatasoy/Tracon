# AgentPrism.Cli

The `agentprism` global tool. Applies migrations as a separate deployment
step, reads model provider health over HTTP, and runs an eval suite as a CI
quality gate.

```bash
dotnet tool install -g AgentPrism.Cli
agentprism --help
```

## Commands

| Command | Reaches | What it does |
|---|---|---|
| `agentprism migrate --provider <postgres\|sqlserver\|sqlite> --connection <connection-string>` | Database, directly | Applies pending migrations. Runs before the application ever starts |
| `agentprism migrate status --provider ... --connection ...` | Database, directly | Lists pending migration names. Writes nothing |
| `agentprism health --url <base-url> [--token <token>] [--json]` | HTTP, through `AgentPrism.Client` | Reads model provider health |
| `agentprism eval --url <base-url> --suite <name> [--token <token>] [--agent-version <n>] [--min-pass-rate <0..1>] [--max-failures <n>] [--timeout <seconds>] [--poll-interval <seconds>] [--json]` | HTTP, through `AgentPrism.Client` | Triggers a suite, polls it to completion, applies an optional quality gate |

`--connection` and `--token` can come from the `AGENTPRISM_CONNECTION` and
`AGENTPRISM_TOKEN` environment variables instead — useful in a CI/CD step
where a literal secret in a command line would show up in shell history and
process listings. Neither is ever read from a configuration file, and neither
is ever printed back.

`--url` is the application root **plus** the `MapAgentPrism` prefix, for
example `http://localhost:5080/agentprism` for the default prefix, or
`http://localhost:5080/control` for an app that called
`MapAgentPrism("/control")`.

## `eval`: running a suite as a CI gate

An eval run is processed by the background job queue, so `eval` polls
`GET /api/evals/runs/{id}` (default every 5 seconds) until the run reaches a
terminal state, or `--timeout` (default 30 minutes) runs out.

With neither `--min-pass-rate` nor `--max-failures` given, there is no
quality gate: the command exits `0` as soon as the run finishes, whatever the
result. With one or both given, **all** given thresholds must hold — the
suite must satisfy `--min-pass-rate` (`Passed / Total`) **and**
`--max-failures` (`Failed <= n`) when both are present, not either one.

A suite with no cases cannot be triggered at all — the server rejects it with
`400` before any run exists, so `eval` exits `2`, not `3`. The
`--min-pass-rate` math still guards `Total == 0` defensively (it fails rather
than reading a division by zero as "100% passed"), in case a future server
path ever hands back a completed run with no cases.

Triggering needs the `RunsWrite` API key scope; polling needs `EvalsRead`. A
key missing either one gets a `2` with the missing scope named in the error —
never the server's response body.

## Exit codes

| Code | Meaning |
|---:|---|
| `0` | Ran and passed the gate (or no gate was given) |
| `1` | Argument error (missing/invalid flag, unknown command) |
| `2` | Could not run: connection failed, HTTP error, timed out, or (`eval` only) the run itself ended `Failed`/`Cancelled` |
| `3` | (`eval` only) Ran, but missed the quality gate |

`migrate`, `migrate status`, and `health` never return `3`.

## Why `migrate` talks to the database directly, not over HTTP

The moment `migrate` matters most is before the application has ever started
— there is no HTTP endpoint to call yet, and adding one would open a new,
unauthenticated-by-necessity attack surface for a database-writing operation.
`migrate` and `migrate status` reference `AgentPrism.PostgreSql`,
`AgentPrism.SqlServer`, and `AgentPrism.Sqlite` directly instead. This adds
weight to the **tool's own** package, not to a consumer's dependency graph —
a global tool is not referenced, it is installed and run standalone.

## Links

- Guide: <https://agentprism.doayen.web.tr/guides/cli/>
- `AgentPrism.Client` (the package `health` and `eval` are built on): <https://agentprism.doayen.web.tr/api/>
