# Contributing

Thanks for looking. This file gets you from a fresh clone to a change that
passes the same gates the maintainer runs.

Start with [`ARCHITECTURE.md`](ARCHITECTURE.md) if you have not read it — it is
short, and it explains why the code is shaped the way it is. Tracon is a
family of NuGet packages other people depend on, so the bar is a little higher
than for an application: public API changes are expensive, and every public
member needs XML documentation.

## What you need

- **.NET SDK 10.0.100+**, plus the **.NET 8 and .NET 9 runtimes** — a
  representative set of test projects runs on `net8.0`, `net9.0`, and `net10.0`
  (`tests/Directory.Build.props`). The gate runner stops early if one is missing.
- **Node.js 20.19+** — `dotnet build` builds the React console as part of the
  build
- **Docker** — the integration tests start real databases with Testcontainers

The console's end-to-end tests download Chromium themselves on first run.

```bash
git clone https://github.com/farukatasoy/Tracon.git
cd Tracon
dotnet build Tracon.slnx -c Release
```

## The four gates

A change is finished when all four are clean. Not three.

```bash
python3 scripts/kapi.py ic-dongu     # build + the affected test projects (fast inner loop)
python3 scripts/kapi.py tarama       # sync-copy and secret scan (seconds)
python3 scripts/kapi.py kapanis --taban <commit before your work>   # everything, cheapest first
```

The runner prints every command it runs, stops at the first red, and records
timings. `--komutlari-bas` lists the commands without running any of them.

Underneath, the four are `dotnet build`, `dotnet test`, `dotnet pack`, and
`dotnet format --verify-no-changes`. Each catches something the others do not:
`format` catches analyzer diagnostics the build does not surface, and `pack`
validates the **packaged** surface — the README that ships inside the package,
and the public API files.

`TreatWarningsAsErrors` is on. There are no warnings, only errors. Before you
suppress an analyzer rule, understand why it fired; if a suppression really is
right, the reason belongs both in the code and in the decision ledger.

Two environment details that will otherwise cost you an hour:

- Set `MSBUILDDISABLENODEREUSE=1` when you run `dotnet` by hand. Orphaned
  MSBuild nodes hold a pipe open and make `dotnet test` hang for minutes.
  `kapi.py` adds it to every command it runs.
- The test projects run on the **Microsoft Testing Platform**, not VSTest.
  `dotnet test --filter` does not exist there: it is swallowed silently, the
  whole suite runs, and you think you narrowed it down. Use
  `python3 scripts/kapi.py test --proje <Project> --sinif "*Name*"`.

If you are not touching the console, `-p:TraconFrontendEnabled=false` skips
the npm, Vite, and Vitest steps. Do **not** pass it when you are touching the
console: the end-to-end tests look for embedded assets and the run will hang.

## Picking the right test level

Believing a unit test was enough has cost this repository real defects eight
times. The rule:

> If a behavior crosses a **boundary**, test it at that boundary. Boundaries:
> DI scope, HTTP, tenancy, streaming, the store, the process, the package.

| Behavior | Right level |
|---|---|
| Pure calculation, formatting, validation | Unit |
| A store contract — writing, reading, isolation | Contract test (runs in memory **and** against all three SQL providers) |
| HTTP behavior, DI registration, authorization | Functional (`Tracon.AspNetCore.FunctionalTests`) |
| Spans, scopes, `AsyncLocal`, streaming paths | Functional **and** a real run of a sample application |
| Screens, routes, both languages | End-to-end (Playwright) |
| What a packaged consumer sees | A sample application built against the packed package |

A green test at the wrong level is worse than no test: it manufactures
confidence. And for every new code path, answer five questions — cancellation,
concurrency, empty or oversized input, another tenant's record, and a subsystem
failure — then write the ones that apply.

## Public API changes

Public API tracking is on. When you add, remove, or change a public member, the
build fails until the corresponding `PublicAPI.Unshipped.txt` entry exists. The
IDE offers a code fix; from the command line:

```bash
dotnet format analyzers Tracon.slnx --diagnostics RS0016
```

Run it more than once. Each pass resolves roughly one project's worth of
diagnostics, and `Formatted N of M files` can look finished while it is not —
the real proof is a clean `dotnet build`.

Public members need XML documentation with a real `<summary>`, documented
parameters, and the exceptions they throw. This is enforced, and it is not
box-ticking: those comments become the published API reference and the
IntelliSense a consumer reads.

## Language

Two rules, and they do not overlap:

- **Everything that ships or runs is English** — code, comments, XML
  documentation, commit messages, package READMEs, and the `docs-site/`
  product documentation.
- **The development journal in `docs/` is Turkish**, along with the agent
  skills in `.agents/` and the scripts. It is the maintainer's record. You are
  not expected to write it, and a pull request does not need to touch it.

If your change alters something a consumer sees — a public type, an HTTP
endpoint, a screen, a package — the `docs-site/` page for it changes in the same
pull request.

## Secrets

Connection strings and API keys live in `dotnet user-secrets`, never in a file
and never in the database. What a record stores is the **name of the
configuration key**, never its value. `python3 scripts/kapi.py tarama` scans for
leaks; when you need a fake secret in a test, pick a value that does not match
the scanner's pattern.

## Branches and commits

Work on `main` is fine for small changes — this is a single-maintainer
repository. Open a branch for anything experimental or expensive to undo.
Commit messages are English, in the imperative, and say what changed and why.

Before you open a pull request, run
`python3 scripts/kapi.py kapanis --taban <commit you branched from>`, and say in
the description which gates you ran.
A change that needs a database or the console has to have been run, not just
compiled.

## Reporting a defect

Open an issue with the smallest reproduction you can manage: the registration
chain (`AddTracon()...`), the request, what you expected, and what happened.
For anything storage-related, say which provider. `GET /api/diagnostics` prints
a summary of an installation — providers, storage, pending migrations,
extension points — and it contains no secrets, so it is safe to paste. The route
is off by default: turn it on with `EnableDiagnosticsEndpoint`, and note that it
then sits behind the `Admin` policy.
