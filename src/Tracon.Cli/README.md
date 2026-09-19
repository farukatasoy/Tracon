# Tracon.Cli

The `tracon` global tool. Applies migrations as a separate deployment
step, checks whether this build can still read the state already in the
database, reads model provider health over HTTP, runs an eval suite as a
CI quality gate, and writes the gate skill a coding agent's harness loads
before it writes Tracon code.

```bash
dotnet tool install -g Tracon.Cli --prerelease
tracon --help
```

## Commands

| Command | Reaches | What it does |
|---|---|---|
| `tracon migrate --provider <postgres\|sqlserver\|sqlite> --connection <connection-string>` | Database, directly | Applies pending migrations. Runs before the application ever starts |
| `tracon migrate status --provider ... --connection ...` | Database, directly | Lists pending migration names. Writes nothing |
| `tracon state-check --provider <postgres\|sqlserver\|sqlite> --connection <connection-string> [--sample <n>] [--json]` | Database, directly | Reports whether this build can read the stored session and workflow checkpoint state. Writes nothing |
| `tracon health --url <base-url> [--token <token>] [--json]` | HTTP, through `Tracon.Client` | Reads model provider health |
| `tracon eval --url <base-url> --suite <name> [--token <token>] [--agent-version <n>] [--min-pass-rate <0..1>] [--max-failures <n>] [--baseline <runId\|previous>] [--max-regressions <n>] [--timeout <seconds>] [--poll-interval <seconds>] [--json]` | HTTP, through `Tracon.Client` | Triggers a suite, polls it to completion, applies an optional quality gate — absolute, relative to a baseline run, or both |
| `tracon agent-skill [--format claude] [--output <directory>] [--force] [--json]` | The local file system | Writes `.claude/skills/tracon/SKILL.md`, so a coding agent reads the capability map of the installed version before it writes code Tracon already ships. An existing file is never touched without `--force` |

`--connection` and `--token` can come from the `TRACON_CONNECTION` and
`TRACON_TOKEN` environment variables instead — useful in a CI/CD step
where a literal secret in a command line would show up in shell history and
process listings. Neither is ever read from a configuration file, and neither
is ever printed back.

`--url` is the application root **plus** the `MapTracon` prefix, for
example `http://localhost:5080/tracon` for the default prefix, or
`http://localhost:5080/control` for an app that called
`MapTracon("/control")`.

## `agent-skill`: the procedure an agent reads first

The capability map and `Tracon.LocalReference.md` wait to be read, and a build
diagnostic arrives once the code is already written. This command writes the one
channel that speaks first: a short procedure the agent's harness loads when a
task mentions Tracon, saying to read the map of the installed version before
writing anything.

```bash
tracon agent-skill
```

One file, `.claude/skills/tracon/SKILL.md`, under `--output` (default: the root
of the repository you run it in, which is where the build looks for it and where
the harness loads it from). It is the only command here that changes your tree,
and it does so under the same rule the build uses for `AGENTS.md`: an existing
file is left alone, because you may have edited it, and the command says so and
exits `0`. `--force` overwrites it.

The file carries the capability map revision this tool was built from. Your
project's build compares that against the revision its installed packages ship
and reports a difference as `TRC0403`. Because the tool carries the revision it
stamps, update the tool before rewriting the file:

```bash
dotnet tool update -g Tracon.Cli --prerelease
rm .claude/skills/tracon/SKILL.md
tracon agent-skill
```

Only the layout above was measured to load, so only it is written; `--format`
rejects a name this version does not write rather than writing that file under
another name.

## `state-check`: asking the upgrade question before the upgrade

"Will my pending sessions still be readable after I upgrade?" is normally
answered in production, after the fact. `state-check` moves it earlier: it
runs against the database directly, so the new build can be pointed at a
production copy while the old one is still serving traffic.

It does two different things, and the difference matters:

- **It counts.** One aggregate query per table groups the rows by the
  Tracon schema generation stamped on them, across **every tenant** — an
  upgrade replaces the process for all of them at once. Each generation is
  then marked readable or not by the build running the command. This count
  covers every row.
- **It samples.** It then reads at most `--sample` rows of *each* generation
  (default 5) and tries to deserialize them through Microsoft Agent Framework.
  This is a **sample, not a survey**: a run with no failures says the rows
  that were read came back readable, never that all of them would.

Two kinds of row are reported as checked for structure only, and neither is a
failure:

- **Workflow checkpoints.** Their payload is Microsoft Agent Framework's own
  opaque blob with no decoder outside a running workflow, so only its stored
  shape is verified.
- **Sessions encrypted at rest.** With `AddContentProtection(...)` on, the CLI
  holds no key. Calling that unreadable would raise a false alarm about a row
  the application reads perfectly well.

`state-check` **writes nothing** — no row, no migration table entry, no lock.
It is safe against a live database, and interrupting it with Ctrl+C leaves
nothing half-done.

Note what the promise covers: this is about **Tracon's own envelope**
around the state. Whether one Microsoft Agent Framework version can read what
another wrote is Microsoft's compatibility surface, not Tracon's — which
is exactly why the decode step exists and why a failure names both the
recorded and the running framework version.

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

### The relative gate: `--baseline`

An absolute threshold cannot see a slide. With `--min-pass-rate 0.85` set, a
suite that drops from 95% to 90% still passes — nothing in the run's own
summary says that five cases which used to work now do not.

`--baseline` compares the finished run against an earlier run of the same
suite, case by case, through `GET /api/evals/runs/{id}/diff`. It takes either
an eval run id or the word `previous`, which means the newest **completed**
run of that suite before this one. `--max-regressions <n>` then says how many
cases may break: more than that and the command exits `3`, listing each
broken case on stderr. Given `--baseline` alone, the comparison is reported and
never fails the build — the ceiling is what turns a report into a gate.

Under `--json`, stdout stays a single parseable document: the comparison
summary goes to stderr alongside the broken-case lines.

Cases added to or dropped from the suite are their own buckets and are never
counted as regressions — adding a case moves the pass rate without anything
having broken, and a gate that confuses the two teaches the team to ignore it.

Two rules keep the gate honest:

- `--max-regressions` without `--baseline` is an **argument error** (`1`), not
  a silent no-op. A pipeline must never read a green exit code as "no
  regressions" when nothing was compared.
- If the comparison itself is impossible — the baseline's per-case results
  have aged out of the `eval_case_results` retention window, it never
  completed, or it measures a different suite — the command exits **`4`**, not
  `3`. A lost history needs a different fix than a broken case, and the server
  answers `409` there rather than an empty diff that would read as "nothing
  changed".

On a suite's very first run, `--baseline previous` finds nothing to compare
against. That is written to stderr and the gate is **skipped**, not failed:
otherwise every new suite's first CI run would go red for no reason.

## Exit codes

| Code | Meaning |
|---:|---|
| `0` | Ran and passed the gate (or no gate was given) |
| `1` | Argument error (missing/invalid flag, unknown command) |
| `2` | Could not run: connection failed, HTTP error, timed out, or (`eval` only) the run itself ended `Failed`/`Cancelled` |
| `3` | Ran, but the answer is bad: (`eval`) missed the quality gate, or (`state-check`) found state this build cannot read |
| `4` | (`eval` only) Ran, but could not be compared against `--baseline` |

`migrate`, `migrate status`, and `health` never return `3` or `4`;
`state-check` never returns `4`.

## Why `migrate` and `state-check` talk to the database directly, not over HTTP

The moment `migrate` matters most is before the application has ever started
— there is no HTTP endpoint to call yet, and adding one would open a new,
unauthenticated-by-necessity attack surface for a database-writing operation.
`state-check` is the same story from the other side: the whole point is to ask
the question while the new build is *not* running.
`migrate`, `migrate status`, and `state-check` reference `Tracon.PostgreSql`,
`Tracon.SqlServer`, and `Tracon.Sqlite` directly instead. This adds
weight to the **tool's own** package, not to a consumer's dependency graph —
a global tool is not referenced, it is installed and run standalone.

## Links

- Guide: <https://tracon.dev/guides/cli/>
- `Tracon.Client` (the package `health` and `eval` are built on): <https://tracon.dev/api/>

Licence: PolyForm Small Business 1.0.0 - free below 100 people and 1,000,000 USD
(2019, inflation adjusted) revenue; a commercial licence applies above that. Terms
ship in the package as LICENSE.md. Details: <https://tracon.dev/reference/licensing/>
