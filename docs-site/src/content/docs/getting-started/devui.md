---
title: Tracon and DevUI
description: What MAF's DevUI does, what its own documentation says it is not for, and which of the two belongs in a production host.
slug: getting-started/devui
sidebar:
  order: 2
---

Microsoft Agent Framework already ships a user interface, so the first question about
Tracon is usually why a second one exists. The two answer different questions, and
MAF's own documentation draws the line.

## What DevUI says about itself

[DevUI](https://learn.microsoft.com/en-us/agent-framework/integrations/by-component/ui/devui/)
introduces itself this way:

> DevUI is a **sample app** to help you visualize and debug your agents and workflows
> during development. It is **not** intended for production use.

Its [security and deployment page](https://learn.microsoft.com/en-us/agent-framework/integrations/by-component/ui/devui/security)
says what to do instead:

> For production deployments, build your own custom interface using the Agent Framework
> SDK with appropriate security measures.

Tracon is that interface, delivered as packages instead of as an instruction. It does
not replace DevUI: DevUI stays the fastest way to look at an agent while you write it.

## Where the two are documented to differ

Only the rows below are drawn from published documentation. DevUI does not document how
it stores state, so this page makes no claim about that.

| | DevUI | Tracon |
|---|---|---|
| Stated purpose | A sample app for local development | An operational layer for your production host |
| Stated production use | Not intended for it | The reason it exists |
| Network binding | Binds to `127.0.0.1` by default | Loopback by default; remote access is opt-in |
| Authentication | Optional bearer token | Bearer token, scoped API keys, and your own ASP.NET Core authorization policy |

## What the production layer has to carry

These are Tracon's own capabilities, not a judgement about DevUI. They are the work
that the recommendation above leaves to you.

- **Records that outlive the process.** Runs, events, tool calls, timings, and cost,
  written to PostgreSQL, SQL Server, or SQLite in a schema of their own.
- **Definitions you can move backwards.** An agent is data, versioned, with rollback.
- **A tenant boundary.** Optional. When you enable it, a `tenant_id` scopes every query.
- **An audit trail you can check.** `audit_log` is hash-chained, and
  `GET /api/audit/verify` reports whether the chain is intact.

## What this page does not claim

**DevUI is not deficient.** A sample app that says it is a sample app is doing its job.
The gap it describes is the gap Tracon fills.

**Tracon is not a hosted product either.** It is a package family that runs inside your
own process. You still own authentication, model providers, storage, and outbound
connections.

**Most governance features start switched off.** Tenancy, rate limiting, retention, and
at-rest protection are opt-in. `RequireProductionProfile()` turns each of those into a
decision you make at startup rather than one you discover later.

## Read next

- [What Tracon is](/getting-started/) — the packages, the four rules, and whether it fits.
- [Capability map](/capabilities/) — every feature with its package, storage need, and limit.
- [Security](/getting-started/security/) — the access layers and the tenant boundary in detail.
