-- ---------------------------------------------------------------------------
-- 0048 — The score's name and shape (phase 152)
--
-- Two limits are lifted at once.
--
-- (a) THE NAME. Until now the uniqueness key was
--     (tenant_id, run_id, COALESCE(message_id, ''), author), so one author
--     could write ONE score per target: a reviewer could not score the same
--     run for both "helpfulness" and "accuracy" — the second write silently
--     overwrote the first. `name` joins the key and the limit disappears.
--
-- (b) THE SHAPE. `value integer NOT NULL` could hold neither a decimal (0.87)
--     nor a categorical label ("minor"). It becomes `double precision NULL`
--     and `text_value` arrives beside it. NULL means NO MEASUREMENT WAS MADE,
--     not zero — the rule RunJudgment.Score already states.
--
-- 🚨 author IS STILL NOT COALESCED, deliberately: with an identity-less setup
-- (author NULL) no NULL equals another NULL in PostgreSQL, uniqueness never
-- takes effect and every call opens a new row. 0017_run_scores.sql wrote that
-- down and this migration preserves it — only `name` is added to the key.
--
-- 🚨 ORDER MATTERS. The new unique index cannot be created before the old one
-- is dropped and `name` is filled, and `name` cannot be made NOT NULL before
-- every existing row carries one.
--
-- Rationale: docs/152-SKORUN-ADI-VE-SEKLI.md, sections 152.2, 152.3, 152.5.
-- ---------------------------------------------------------------------------

-- DEFAULT 'overall' is kept on the column, not dropped after the backfill: it
-- is the same default the HTTP endpoint applies when a caller sends no name,
-- and all three providers carry it, so a name-less insert means the same thing
-- everywhere.
ALTER TABLE {schema}.run_scores
    ADD COLUMN IF NOT EXISTS name text NOT NULL DEFAULT 'overall';

ALTER TABLE {schema}.run_scores
    ADD COLUMN IF NOT EXISTS text_value text;

-- A judge row already carries its judge name inside author ('judge:{name}');
-- it is lifted into `name` so an existing judge score keeps a meaningful name
-- instead of collapsing into 'overall' together with the human rows.
UPDATE {schema}.run_scores
   SET name = substring(author from 7)
 WHERE left(author, 6) = 'judge:'
   AND length(author) > 6;

ALTER TABLE {schema}.run_scores
    ALTER COLUMN value TYPE double precision,
    ALTER COLUMN value DROP NOT NULL;

DROP INDEX IF EXISTS {schema}.run_scores_target_author_idx;

CREATE UNIQUE INDEX IF NOT EXISTS run_scores_target_author_name_idx
    ON {schema}.run_scores (tenant_id, run_id, COALESCE(message_id, ''), author, name);
