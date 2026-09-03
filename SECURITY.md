# Security policy

## Supported versions

AgentPrism ships 20 NuGet packages and one npm package from a single version
line. Only the **most recent published version** receives security fixes; a
preview that has been superseded is not patched in place. See
[Versions and upgrades](https://agentprism.doayen.web.tr/reference/versioning/)
for the pinning and upgrade contract.

| Version line | Supported |
|---|---|
| Latest `1.0.0-preview.*` | Yes |
| Any earlier preview | No — upgrade to the latest preview |

## Reporting a vulnerability

Report suspected vulnerabilities privately to **hfarukatasoy@gmail.com**. Do not
open a public issue, and do not include a working exploit against a system you do
not own.

Include what a maintainer needs to reproduce it:

- the exact AgentPrism package versions from `dotnet list package`;
- the .NET target framework and deployment runtime;
- the storage engine, and whether multi-tenancy is enabled;
- the affected surface — an HTTP endpoint, a .NET API, an MCP or A2A route, or a
  background service;
- a minimal reproduction, and the impact you observed.

Never attach credentials, connection strings, raw API keys, customer data, or
unreviewed prompt and tool content. Redact them first.

Expect an acknowledgement within **72 hours** and an assessment within **seven
days**. A confirmed vulnerability is fixed in a new version rather than by
replacing a published one — NuGet.org artifacts are permanent. If the defect
affects data integrity or a security boundary, the affected versions are also
deprecated on NuGet.org with a pointer to the fixed version.

## Scope

In scope: tenant isolation, credential handling (including per-tenant
bring-your-own-key), the audit trail, egress policy, tool approval and
authorization, content guards, script sandboxing, and any path that writes a
secret to storage, a log, or a response.

Out of scope: model output quality, prompt injection that a configured guard is
not enabled to stop, and denial of service produced by quotas you configured.
