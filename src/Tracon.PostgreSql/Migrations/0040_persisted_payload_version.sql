-- ---------------------------------------------------------------------------
-- 0040 — Persisted payload version stamps (Phase 126)
--
-- `sessions.schema_version` already existed (0001_initial.sql) and has
-- always been stamped — there is no "unstamped" era for it. It is renamed
-- to `state_schema_version` only for naming consistency with the new
-- `workflow_checkpoints` column below; its NOT NULL constraint and meaning
-- (the Tracon envelope generation that wrote `state`) are unchanged.
--
-- `state_maf_version` is genuinely new on BOTH tables: it records which
-- Microsoft Agent Framework package version produced the opaque `state`
-- payload, so a failed restore can report what was recorded instead of
-- guessing. NULL means the row was written before this column existed.
--
-- `workflow_checkpoints` never had an envelope generation column at all, so
-- `state_schema_version` there is NULL for pre-existing rows — unlike the
-- sessions column, which stays NOT NULL because it was always stamped.
-- ---------------------------------------------------------------------------

ALTER TABLE {schema}.sessions
    RENAME COLUMN schema_version TO state_schema_version;

ALTER TABLE {schema}.sessions
    ADD COLUMN IF NOT EXISTS state_maf_version text;

ALTER TABLE {schema}.workflow_checkpoints
    ADD COLUMN IF NOT EXISTS state_schema_version integer,
    ADD COLUMN IF NOT EXISTS state_maf_version text;
