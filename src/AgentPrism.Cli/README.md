# AgentPrism.Cli

The `agentprism` global tool. Applies migrations as a separate deployment
step, and reads model provider health over HTTP.

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

`--connection` and `--token` can come from the `AGENTPRISM_CONNECTION` and
`AGENTPRISM_TOKEN` environment variables instead — useful in a CI/CD step
where a literal secret in a command line would show up in shell history and
process listings. Neither is ever read from a configuration file, and neither
is ever printed back.

`--url` is the application root **plus** the `MapAgentPrism` prefix, for
example `http://localhost:5080/agentprism` for the default prefix, or
`http://localhost:5080/control` for an app that called
`MapAgentPrism("/control")`.

## Why `migrate` talks to the database directly, not over HTTP

The moment `migrate` matters most is before the application has ever started
— there is no HTTP endpoint to call yet, and adding one would open a new,
unauthenticated-by-necessity attack surface for a database-writing operation.
`migrate` and `migrate status` reference `AgentPrism.PostgreSql`,
`AgentPrism.SqlServer`, and `AgentPrism.Sqlite` directly instead. This adds
weight to the **tool's own** package, not to a consumer's dependency graph —
a global tool is not referenced, it is installed and run standalone.

## Exit codes

`0` on success. `1` for a usage error (missing or invalid argument, unknown
command). `2` for a runtime failure (could not connect, HTTP error, timeout).

## Links

- Guide: <https://agentprism.doayen.web.tr/guides/cli/>
- `AgentPrism.Client` (the package `health` is built on): <https://agentprism.doayen.web.tr/api/>
