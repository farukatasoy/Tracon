---
title: Sign the audit chain with a key
description: Turn on the HMAC key that signs the audit trail's hash chain, rotate it, switch it off, and know what it does and does not protect against.
---

The audit trail is a per-tenant hash chain: each entry carries a hash of its own content and
of the entry before it, and `GET /api/audit/verify` walks it (see [Tamper
detection](/concepts/governance/#tamper-detection) for the four answers it gives). Without a key
that hash is a plain SHA-256 of a public form, so anyone who can write to the audit table can change
an entry, recompute every hash after it and still get `Valid`. With a chain key the hash is an
HMAC-SHA256 under a value only your host holds, and each entry names the key's **id**, never its value.
The console's Audit screen runs the same walk with its **Verify chain** button.

## Turn the key on

The key value lives in configuration and nowhere else; the entry stores only the key's id.

```json
{
  "Tracon": {
    "Audit": { "ChainKeyId": "2026-10" },
    "AuditChainKeys": { "2026-10": "<base64, at least 32 bytes>" }
  }
}
```

```bash
openssl rand -base64 32
dotnet user-secrets set 'Tracon:AuditChainKeys:2026-10' '<the value>'
```

- A key decodes to at least 32 bytes. An id is 1 to 64 characters of lowercase letters, digits,
  `.`, `_` and `-`; lowercase only, because configuration keys ignore case and the id is stored as
  written.
- The host reads every key at start and refuses to start when one is missing a value, is not
  base64, is too short or has an id outside the pattern. The message names the configuration key and
  never carries a value. A change needs a restart.
- The value is never written to the database, a log, the diagnostics report or a response.
- Every replica carries the same set of keys. During a rollout two replicas may sign with different
  ids; both ids must be in both replicas' configuration.
- Entries written before the key was set keep their plain SHA-256 and are checked with it. They are
  not re-signed, because re-signing would rewrite history. The walk counts them in `unkeyedEntries`.
  After the first signed entry, an unsigned entry is a downgrade and the walk reports `Broken`.

## Turn the key on across replicas

Upgrade every replica to this release **first**, with no key. Then give every replica the same `ChainKeyId` and the
same values and restart them together. A replica that writes without the key after another has written a signed entry
for the same tenant leaves an unsigned entry behind a signed one: the walk reads that as a downgrade and reports
`Broken` from that entry on, for good, because the entry cannot be re-signed without rewriting history. Two things
produce it: a replica that has not restarted yet, and a replica still on an older release. Tracon's own tests pin
this (`AuditLogContract.Two_writers_with_a_mixed_key_configuration_leave_the_chain_broken_at_the_unsigned_entry_for_good`).

## Rotate the key

Add a new key under a new id, set `ChainKeyId` to it and restart. Keep the old
value in configuration: the entries it signed are checked with it for as long as they exist. Remove it
and those entries report `UnknownKey`. The same answer appears when a row's `key_id` was changed by hand to an id
no one holds, and the walk cannot tell the two apart: it is never `Valid`, so compare the id the row names with the
history of your keys.

## Turn the key off

Remove `ChainKeyId` and restart. Audit entries are still written (an audit trail
must not lose a record), unsigned; the host logs one warning per tenant, and the walk reports `Broken`
from the first unsigned entry on.

## Ask for it in production

`RequireProductionProfile()` asks about the key as `UnkeyedAuditChain`. It is not asked while the audit
log is Tracon's in-memory one, because that chain lives in the process and goes with it; a database
audit log, or one you registered, is asked. See [Make a production decision
required](/guides/embedding/#make-a-production-decision-required).

## What the key protects against, and what it does not

The key protects against an actor who can **write to the audit table but cannot read this host's
configuration**: a database administrator, a restored backup, another application's connection. They
can no longer change an entry and recompute the hashes after it, because a valid hash needs the key.

It does not protect against an actor who can read the configuration **and** write the table: they hold
the key. It does not anchor the chain outside the database, so an attacker who rewrites every entry from
the first signed one onward without a key leaves a chain the walk calls `Valid`; the signal is
`unkeyedEntries` equal to `entriesChecked`, which a host that has been signed from its first entry never
shows. `?after=` and `?before=` walk a range and cannot see a signed entry before it: the whole-history walk
is the one to trust. Without a key the chain still catches a change made without recomputing it (a manual
edit, a partial restore, a bug in another writer) and nothing more: anyone who can write the table can
recompute every hash, and the walk reports `Valid`.

## Read next

- [Governance](/concepts/governance/#tamper-detection) — the four answers of `GET /api/audit/verify` and what each means
- [Make a production decision required](/guides/embedding/#make-a-production-decision-required) — `UnkeyedAuditChain` among the decisions the profile asks
- [Threat model](/reference/threat-model/) — who can write to the audit table and what the key does about it
