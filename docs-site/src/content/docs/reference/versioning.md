---
title: Versions and upgrades
description: Understand which AgentPrism release these docs describe, pin preview packages safely, and upgrade the full package family without drift.
---

AgentPrism is a pre-1.0 package family. Treat version selection as part of your
application architecture, not as a restore detail.

All 19 packages, the npm client included, are cut from the same `v*` tag and share
one version line: there is no split between a stable subset and a preview subset.
The public surface carries no compatibility promise for as long as that line stays
pre-1.0 - a narrowing or a reshaped type is not treated as a breaking change until
the family reaches `1.0.0`.

## Which version do these docs describe?

This site is built from the repository's `main` branch. The .NET reference is generated
from the assemblies built from that same source, and the HTTP reference is generated from
the OpenAPI snapshot in that source tree.

That makes the site the best description of the next build. It can also document a public
API that is newer than the preview package you installed. When exact reproducibility
matters, pin every AgentPrism package and read the documentation from the matching source
tag or commit.

:::caution[Preview contract]
AgentPrism has not shipped `1.0`. Public APIs, migrations, configuration keys, and
provider behavior can change between previews. Review the source diff and this site's
compatibility matrices before each upgrade.
:::

## Install the newest preview

Use NuGet's pre-release selection explicitly:

```bash
dotnet add package AgentPrism --prerelease
```

The project template resolves pre-release packages by default, but pinning it makes a
team build reproducible:

```bash
AGENTPRISM_VERSION=1.0.0-preview.N # replace N with the published preview
dotnet new install "AgentPrism.Templates@$AGENTPRISM_VERSION"
dotnet new agentprism-api --AgentPrismVersion "$AGENTPRISM_VERSION"
```

## Pin the whole package family

Do not mix AgentPrism preview versions. The packages share public contracts, DI
registrations, database migrations, and generated code. A mixed graph can restore but
fail during startup or at runtime.

With Central Package Management, keep the versions in one place:

```xml
<ItemGroup>
  <PackageVersion Include="AgentPrism" Version="1.0.0-preview.N" />
  <PackageVersion Include="AgentPrism.PostgreSql" Version="1.0.0-preview.N" />
  <PackageVersion Include="AgentPrism.OpenAI" Version="1.0.0-preview.N" />
  <PackageVersion Include="AgentPrism.UI" Version="1.0.0-preview.N" />
</ItemGroup>
```

If you reference individual packages directly, pin each one to the same version. Avoid
floating ranges in production.

### The typed client and the server it calls

`AgentPrism.Client` is generated from the exact same OpenAPI document the server
build carries — both come from the same repository build, so they cannot drift
apart at a given version the way a hand-written client could. A caller on a
newer preview than the server it targets sees only the operations the server
actually serves; calling one the server does not yet have returns a `404`.
`AgentPrism.Cli` follows the same version family, since it wraps `AgentPrism.Client`.

The npm package `@agentprism/client` is cut from the same `v*` git tag as every
NuGet package above — there is no separate npm version scheme. `@agentprism/client
1.0.0-preview.N` and `AgentPrism.Client 1.0.0-preview.N` always describe the
identical OpenAPI document.

## Upgrade safely

1. Create a branch and update all AgentPrism packages together.
2. Read the source diff for public API, configuration, and migration changes.
3. Build with warnings as errors and run the full test suite.
4. Start a disposable environment against a copy of production-shaped data.
5. Inspect `/api/meta`, health checks, provider health, and migration diagnostics.
6. Exercise one synchronous run, one streamed run, every enabled background service, and
   your approval and guard paths.
7. Back up the database before the production migration. Deploy API and worker processes
   from the same artifact.
8. Watch run failures, provider latency, queue depth, webhook delivery, and cost after the
   rollout.

Database migrations are forward-only. Do not assume that rolling back the application
also rolls back the schema. See [Production deployment](/guides/production/)
for the migration and backup contract.

## Record the version in incident reports

Include these facts when you report a defect:

- the exact AgentPrism package versions from `dotnet list package`;
- the .NET target framework and deployment runtime;
- the model provider, model or Azure deployment name, and provider SDK version;
- the storage engine and migration state;
- the failing endpoint or .NET API and a minimal reproduction;
- redacted diagnostics, logs, trace ids, and run ids.

Never attach credentials, connection strings, raw API keys, or unreviewed prompt and tool
content.

## Read next

- [Compatibility matrices](/reference/compatibility/) — the framework, runtime, and protocol versions each release supports
- [Choosing packages](/packages/) — which packages you actually take a version of
- [Configuration](/reference/configuration/) — the options an upgrade can move
