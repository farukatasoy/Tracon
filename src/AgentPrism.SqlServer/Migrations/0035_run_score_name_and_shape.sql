-- The score's name and shape (phase 152).
--
-- For the rationale — why `name` joins the uniqueness key and why `value`
-- becomes a nullable float — see PostgreSQL 0048_run_score_name_and_shape.sql.
--
-- 🚨 The unique index is FILTERED with `WHERE author IS NOT NULL` (K-184) and
-- compares message_id directly, because SQL Server treats NULLs as EQUAL to one
-- another — the OPPOSITE of PostgreSQL. 0005_run_scores.sql wrote that down and
-- this migration preserves it; only `name` is added to the key.
--
-- 🚨 nvarchar(64) / nvarchar(256), not nvarchar(max): `name` is INDEXED below
-- and SQL Server cannot index nvarchar(max). The bounds are the ones
-- RunScoreRules enforces before the write, so nothing writable is truncated.

IF COL_LENGTH(N'{schema}.run_scores', N'name') IS NULL
ALTER TABLE {schema}.run_scores
    ADD name nvarchar(64) NOT NULL CONSTRAINT DF_run_scores_name DEFAULT N'overall';

IF COL_LENGTH(N'{schema}.run_scores', N'text_value') IS NULL
ALTER TABLE {schema}.run_scores ADD text_value nvarchar(256) NULL;

-- 🚨 Wrapped in EXEC: `name` is added by the ALTER above in the SAME batch, and
-- SQL Server compiles the WHOLE batch before running it -- without EXEC this
-- fails with "Invalid column name 'name'" (the trap 0021_run_attribution.sql
-- records).
EXEC(N'UPDATE {schema}.run_scores
          SET name = SUBSTRING(author, 7, LEN(author))
        WHERE LEFT(author, 6) = ''judge:''
          AND LEN(author) > 6;');

ALTER TABLE {schema}.run_scores ALTER COLUMN value float NULL;

IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'run_scores_target_author_idx' AND object_id = OBJECT_ID(N'{schema}.run_scores'))
DROP INDEX run_scores_target_author_idx ON {schema}.run_scores;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'run_scores_target_author_name_idx' AND object_id = OBJECT_ID(N'{schema}.run_scores'))
EXEC(N'CREATE UNIQUE INDEX run_scores_target_author_name_idx
    ON {schema}.run_scores (tenant_id, run_id, message_id, author, name)
    WHERE author IS NOT NULL;');
