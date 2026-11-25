---
title: Retention
description: How Tracon deletes recorded data, with policies per target, the archive fail-safe, and how a retention job is queued and scoped.
sidebar:
  order: 10
---

Recorded runs accumulate, and retention is how they stop. This page says what a policy
covers, what a run of it deletes and keeps, and which part of the system queues the work. The
audit trail, tenancy and the other guarantees around it are on
[Governance](/concepts/governance/).

Recorded runs accumulate. A retention policy sets an age or row limit per target — run
events, tool calls, traces, jobs, webhook deliveries, eval results, checkpoints,
attachments, sessions, and more.

A database policy takes precedence, and an enabled one applies whether or not
retention is enabled in configuration. When a target has none and retention is
enabled in configuration, Tracon falls back to the default of every target at
once, including 30 days for run events and 14 days for spans; the flag cannot turn
on only some of them. With retention disabled and no policy, nothing is eligible
for deletion. A policy with `enabled: false` is configured but paused.

When a policy has `archive: true`, cleanup first sends its batch to the registered
`IArchiveSink`. If no sink exists, no rows are deleted. This fail-safe trades storage
growth for protection from silent data loss. A column that
[content protection](/concepts/governance/#content-protection) encrypts reaches the sink as plaintext, as
it does in an export: the sink decides how the archive is stored, so encrypt it there
if it must stay encrypted at rest.

An archiving run deletes exactly the rows it sent to the sink, and only after the
sink returns. If the sink throws, nothing in that batch is deleted. A row that
changes between the read and the delete (for example, a session that is used again)
stays, and a later run archives it again, so your sink can receive the same row more
than once. If you write your own `IRetentionStore`, override `ArchiveBatchAsync`: its
default reads and deletes in two separate calls, which can delete a row that was never
archived. `RetentionStoreContract` in `Tracon.Testing.Contracts.Xunit` checks this.

The `sessions` target also deletes the conversation memory of each expired session,
when memory is set up, the files of its [session workspace](/guides/context-and-memory/#give-an-agent-files-in-each-session)
and, on SQL storage, the chat history of the session (its conversation and items).
The batch is read first, the memory records, the files and the histories of its sessions are
deleted tenant by tenant, and only then are the session rows deleted. If a store cannot
delete, the run fails with the rows still in place, and the next run reads the same sessions.

The `attachments` target deletes only **orphaned** attachments: those with no
session id, or whose session id has no owner in the same tenant. An owner is a
session, or a [workflow](/concepts/workflows/#checkpoints) run with that session id
that waits for input, has not finished, or finished after the cutoff. A workflow's
session id never gets a session row, so without the run rule an image a workflow
step made would go while the run still waits for a human.

With an `IAttachmentStorage` registered, the bytes live outside the database.
Retention deletes the bytes first and the row second. If the byte delete fails,
the row stays, a warning is logged, and the next run tries again — the row is the
only record of where the bytes are. See
[production storage](/guides/multimodal/#persistence-and-production-storage) for
what your storage's `DeleteAsync` must do.

Without an `IAttachmentStorage`, retention keeps the attachment rows that point at
external content. Deleting such a row would leave the bytes in your store with nothing
that names them, so the row stays. Rows with their content in the database are deleted
as usual, and the preview does not count the kept rows. The last batch of a run logs one
warning with the number of rows kept — never a tenant or a location. Register the
`IAttachmentStorage` that wrote the bytes, and the next run deletes the bytes and the rows.

No endpoint deletes synchronously. Preview first — it is the only way to see the size
of a deletion before it happens — then run, which queues a job. Rows are deleted only
when the `tracon.retention` job runs, and nothing schedules it for you: call `run`,
or save a recurring schedule for the `tracon.retention` handler (see
[Keeping it from growing forever](/getting-started/persistence/#keeping-it-from-growing-forever)).
A host that runs no job worker deletes nothing.

A retention job is one tenant's: it reads that tenant's policy and deletes that tenant's rows.
The `*` policy is the default each tenant without a policy of its own is given, not a sweep.
One `tracon.retention.fanout` schedule in the default tenant queues the job for every tenant
([Cleaning every tenant](/getting-started/persistence/#cleaning-every-tenant)).

```bash
curl 'http://localhost:5081/tracon/api/retention/preview'
curl -X POST 'http://localhost:5081/tracon/api/retention/run'
curl 'http://localhost:5081/tracon/api/retention/history'
```

The history of what was deleted is itself never cleaned up.

## Read next

- [Keeping it from growing forever](/getting-started/persistence/#keeping-it-from-growing-forever): schedule the retention job and read what it did
- [Governance](/concepts/governance/): the audit trail, tenancy and approvals that retention works beside
- [Multimodal input and output](/guides/multimodal/#persistence-and-production-storage): what the byte storage of an attachment must do when retention deletes it
