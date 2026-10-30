-- What a run spends that is not tokens.
--
-- For the rationale see PostgreSQL 0058_non_token_cost.sql. `non_token_unsummed IS NULL`
-- is the presence marker of the snapshot: NULL means the run reported nothing, and the
-- existing rows stay NULL on purpose.
--
-- 🚨 SQLite has no `ADD COLUMN IF NOT EXISTS`. The guard is the migration ledger: a
-- numbered file runs exactly once, so a plain ADD COLUMN is correct. The new `runs`
-- columns are added AT THE END: the reader uses fixed column ordinals.

ALTER TABLE {schema}runs ADD COLUMN non_token_cost      NUMERIC NULL;
ALTER TABLE {schema}runs ADD COLUMN non_token_currency  TEXT    NULL;
ALTER TABLE {schema}runs ADD COLUMN non_token_estimated INTEGER NULL;
ALTER TABLE {schema}runs ADD COLUMN non_token_unsummed  INTEGER NULL;

ALTER TABLE {schema}tool_invocations ADD COLUMN usage_provider TEXT NULL;
ALTER TABLE {schema}tool_invocations ADD COLUMN usage_model    TEXT NULL;

CREATE TABLE IF NOT EXISTS {schema}run_non_token_usage (
    id         TEXT    NOT NULL PRIMARY KEY,
    run_id     TEXT    NOT NULL REFERENCES {schema}runs (id) ON DELETE CASCADE,
    node_name  TEXT    NULL,
    unit       TEXT    NOT NULL,
    quantity   TEXT    NOT NULL,
    cost       TEXT    NULL,
    currency   TEXT    NULL,
    estimated  INTEGER NOT NULL,
    provider   TEXT    NULL,
    model      TEXT    NULL,
    created_at TEXT    NOT NULL
);

CREATE INDEX IF NOT EXISTS {schema}run_non_token_usage_run_idx ON {schema}run_non_token_usage (run_id, created_at);
