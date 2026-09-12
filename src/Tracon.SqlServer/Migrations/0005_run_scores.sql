-- Phase 31 -- human (or judge) score per run and per message.
--
-- For the rationale and the column meanings see PostgreSQL 0017_run_scores.sql.
--
-- 🚨 NULL uniqueness works the OTHER WAY on SQL Server (K-184): a UNIQUE index
-- treats NULLs as EQUAL TO EACH OTHER (unlike PostgreSQL). That is EXACTLY the
-- behaviour we want for message_id (no COALESCE needed) but it is the OPPOSITE
-- for author: when author is NULL (an installation without identity) every call
-- should open a NEW row, not CLASH with the other NULL. The fix: the index is
-- FILTERED with `WHERE author IS NOT NULL` -- when author is NULL the index never
-- takes effect and the `author = @author` comparison of the UPDATE branch (an
-- UNKNOWN that never matches NULL) falls to INSERT anyway.

IF OBJECT_ID(N'{schema}.run_scores', N'U') IS NULL
CREATE TABLE {schema}.run_scores (
    id          uniqueidentifier NOT NULL PRIMARY KEY,
    tenant_id   nvarchar(200)    NOT NULL,
    run_id      uniqueidentifier NOT NULL,
    message_id  nvarchar(200)    NULL,
    kind        smallint         NOT NULL,
    value       int              NOT NULL,
    comment     nvarchar(max)    NULL,
    source      nvarchar(50)     NOT NULL,
    author      nvarchar(200)    NULL,
    created_at  datetimeoffset   NOT NULL
);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'run_scores_target_author_idx' AND object_id = OBJECT_ID(N'{schema}.run_scores'))
CREATE UNIQUE INDEX run_scores_target_author_idx
    ON {schema}.run_scores (tenant_id, run_id, message_id, author)
    WHERE author IS NOT NULL;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'run_scores_run_idx' AND object_id = OBJECT_ID(N'{schema}.run_scores'))
CREATE INDEX run_scores_run_idx
    ON {schema}.run_scores (tenant_id, run_id);
