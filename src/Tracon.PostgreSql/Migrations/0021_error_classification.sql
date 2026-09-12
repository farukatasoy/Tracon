-- Phase 44 -- error classification and failure clustering.
--
-- Three separate concepts, three separate columns (the K-151 pattern): error_type
-- is the IDENTITY and does not change (present since 0001_initial.sql);
-- error_class is the CLASS (this migration); error_fingerprint is the CLUSTER
-- (this migration). Derived information is written BESIDE the raw information,
-- not over it -- error_type is not rewritten on past rows (K-014).
--
-- error_class can stay NULL: rows written before the error class was added
-- (K-014 -- no backfill is done). RunStatistics.ByErrorClass shows those rows
-- in the Unknown (0) bucket.
ALTER TABLE {schema}.runs ADD COLUMN IF NOT EXISTS error_class       smallint;
ALTER TABLE {schema}.runs ADD COLUMN IF NOT EXISTS error_fingerprint text;

-- Partial index: rows where error_class is NULL (the majority -- successful
-- runs) take no space in the index.
CREATE INDEX IF NOT EXISTS runs_error_class_idx
    ON {schema}.runs (tenant_id, error_class, started_at DESC)
    WHERE error_class IS NOT NULL;
