---
title: Security policy
description: How to report a vulnerability in Tracon privately, what is in scope, which versions get security fixes, and what response to expect.
---

Report a suspected vulnerability privately to <hfarukatasoy@gmail.com>. Do not open a
public issue, and do not run an exploit against a system you do not own.

Expect an acknowledgement within **72 hours** and an assessment within **seven days**.

## What to include

A maintainer needs enough to reproduce the problem:

- the exact package versions from `dotnet list package`;
- the .NET target framework and the deployment runtime;
- the storage engine, and whether multi-tenancy is enabled;
- the affected surface — an HTTP endpoint, a .NET API, an MCP or A2A route, or a
  background service;
- a minimal reproduction, and the impact you observed.

Redact first. Never attach credentials, connection strings, raw API keys, customer
data, or unreviewed prompt and tool content.

## Scope

| In scope | Out of scope |
|---|---|
| Tenant isolation | Model output quality |
| Credential handling, including per-tenant bring-your-own-key | Prompt injection that a configured guard is not enabled to stop |
| The audit trail | Denial of service produced by quotas you configured |
| Egress policy | |
| Tool approval and authorization | |
| Content guards | |
| Script sandboxing | |
| Any path that writes a secret to storage, a log, or a response | |

The audit trail is in scope as a security boundary, and that boundary has a documented
shape: six operations refuse to proceed without their audit entry, and every other
audit write is best-effort. [What is guaranteed to be
written](/concepts/governance/#what-is-guaranteed-to-be-written) is the division. A
report that a best-effort record was lost on a store failure is expected behaviour, not
a vulnerability; a report that a fail-closed operation proceeded without its record is.

The [threat model](/reference/threat-model/) is why this list draws the line where it
does: it names the attackers this scope assumes, maps each one against the boundaries
above, and states plainly which combinations are accepted risk rather than a covered
boundary.

## Supported versions

Tracon ships its packages from a single version line. Only the **most recent published
version** receives security fixes; a superseded preview is not patched in place.

| Version line | Supported |
|---|---|
| Latest `1.0.0-preview.*` | Yes |
| Any earlier preview | No — upgrade to the latest preview |

A confirmed vulnerability is fixed in a new version rather than by replacing a
published one, because NuGet.org artifacts are permanent. When the defect affects data
integrity or a security boundary, the affected versions are also deprecated on
NuGet.org with a pointer to the fixed version. See [Versions and
upgrades](/reference/versioning/) for the pinning and upgrade contract.

## Read next

- [Securing the endpoints](/getting-started/security/) — the layers you configure
- [Governance](/concepts/governance/) — tenancy, audit, approvals, and guards
- [Production deployment](/guides/production/) — the production-sensitive defaults
