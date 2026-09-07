-- The score's name and shape (phase 152).
--
-- For the rationale — why `name` joins the uniqueness key and why `value`
-- becomes a nullable double — see PostgreSQL 0048_run_score_name_and_shape.sql.
--
-- 🚨 Index names carry the TABLE PREFIX (K-193): in SQLite object names share a
-- single database-wide namespace.
--
-- 🚨 SQLite has no ALTER COLUMN, so `value integer NOT NULL` cannot be widened
-- in place. The table is NOT rebuilt: a DROP TABLE under `foreign_keys = ON`
-- fires every ON DELETE CASCADE (K-666), and the recipe's escape hatch
-- (`PRAGMA foreign_keys = OFF`) is a no-op inside the transaction the migration
-- runner opens. `ALTER TABLE ... DROP COLUMN` is the right tool here — `value`
-- appears in no index, which is the condition that tool requires.
--
-- REAL affinity is not cosmetic: with the old INTEGER affinity SQLite folds a
-- whole number (4.0) back into an integer, and the reader asks for a double.

ALTER TABLE {schema}run_scores ADD COLUMN name TEXT NOT NULL DEFAULT 'overall';

ALTER TABLE {schema}run_scores ADD COLUMN text_value TEXT NULL;

-- A judge row already carries its judge name inside author ('judge:{name}').
UPDATE {schema}run_scores
   SET name = substr(author, 7)
 WHERE substr(author, 1, 6) = 'judge:'
   AND length(author) > 6;

ALTER TABLE {schema}run_scores ADD COLUMN value_real REAL NULL;
UPDATE {schema}run_scores SET value_real = value;
ALTER TABLE {schema}run_scores DROP COLUMN value;
ALTER TABLE {schema}run_scores RENAME COLUMN value_real TO value;

DROP INDEX IF EXISTS {schema}run_scores_target_author_idx;

CREATE UNIQUE INDEX IF NOT EXISTS {schema}run_scores_target_author_name_idx
    ON {schema}run_scores (tenant_id, run_id, COALESCE(message_id, ''), author, name);
