-- ---------------------------------------------------------------------------
-- 0039 — Optimistic concurrency for sessions
--
-- 🚨 HATA-004 was closed for the FIRST write of a session only: TryCreateAsync
-- made two concurrent first turns safe, and every write after it kept going
-- through the unconditional upsert. Two concurrent LATER turns on the same
-- existing session therefore both reported success and the loser's turn was
-- silently overwritten — the same defect class, the other half of it.
--
-- `version` carries the row's write generation. TryUpdateAsync advances it and
-- matches on it in one statement, so the check and the write are atomic.
--
-- DEFAULT 1 covers rows written before this column existed: they read back as
-- generation 1 and take part in concurrency control from their next write on.
-- ---------------------------------------------------------------------------

ALTER TABLE {schema}.sessions
    ADD COLUMN IF NOT EXISTS version bigint NOT NULL DEFAULT 1;
