-- Closes F-215: the run_scores upsert raced and its NULL/'' semantics
-- deviated from PostgreSQL/SQLite.
--
-- Root cause (docs/hafiza/sql-server-tuzaklari.md): 0035's UPDLOCK/SERIALIZABLE
-- predicate wrapped the indexed column, `ISNULL(message_id, N'') = ISNULL(@message_id, N'')`.
-- A wrapped column is NOT sargable, so SQL Server cannot take a range lock on
-- `run_scores_target_author_name_idx` for it; two concurrent upserts of the
-- SAME target both see zero matching rows and both INSERT. The SAME index also
-- keyed the RAW column, so a NULL row and a '' row never conflicted with each
-- other in the first place -- unlike PostgreSQL/SQLite, which key
-- `COALESCE(message_id, '')`.
--
-- The fix is a single sargable surface: `message_key` mirrors the predicate's
-- own normalization as a real, indexed column, so the index can seek (and
-- range-lock) on it directly.
--
-- 🚨 Existing rows CAN already carry a NULL-message_id row and a
-- ''-message_id row for the same (tenant_id, run_id, author, name) -- exactly
-- the deviation this migration closes, produced by the race above. The new
-- unique index cannot be created over such a pair, so the newer of the two
-- (by created_at) is kept, same precedent as 0025_provider_name_case.sql. Only
-- `author IS NOT NULL` rows are considered: the old index (and the new one)
-- is filtered the same way, so an author-less row was never subject to
-- uniqueness and dropping one here would destroy data the constraint never
-- protected.
;WITH duplicates AS (
    SELECT ROW_NUMBER() OVER (
               PARTITION BY tenant_id, run_id, ISNULL(message_id, N''), author, name
               ORDER BY created_at DESC, id DESC) AS rank
      FROM {schema}.run_scores
     WHERE author IS NOT NULL)
DELETE FROM duplicates WHERE rank > 1;

IF COL_LENGTH(N'{schema}.run_scores', N'message_key') IS NULL
ALTER TABLE {schema}.run_scores
    ADD message_key AS ISNULL(message_id, N'') PERSISTED;

IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'run_scores_target_author_name_idx' AND object_id = OBJECT_ID(N'{schema}.run_scores'))
DROP INDEX run_scores_target_author_name_idx ON {schema}.run_scores;

-- 🚨 Wrapped in EXEC: `message_key` is added by the ALTER above in the SAME
-- batch, and SQL Server compiles the whole batch before running it -- without
-- EXEC this fails with "Invalid column name 'message_key'" (the trap
-- 0021_run_attribution.sql records, repeated by 0035_run_score_name_and_shape.sql
-- for `name`).
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'run_scores_target_author_name_idx' AND object_id = OBJECT_ID(N'{schema}.run_scores'))
EXEC(N'CREATE UNIQUE INDEX run_scores_target_author_name_idx
    ON {schema}.run_scores (tenant_id, run_id, message_key, author, name)
    WHERE author IS NOT NULL;');
