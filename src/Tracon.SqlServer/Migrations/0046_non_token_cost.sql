-- What a run spends that is not tokens (phase 199, F-299).
--
-- For the rationale see PostgreSQL 0058_non_token_cost.sql. `non_token_unsummed IS NULL`
-- is the presence marker of the snapshot: NULL means the run reported nothing, and the
-- existing rows stay NULL on purpose.
--
-- The new `runs` columns are added AT THE END: the reader uses fixed column ordinals.

IF COL_LENGTH(N'{schema}.runs', N'non_token_cost') IS NULL
ALTER TABLE {schema}.runs ADD non_token_cost decimal(20,10) NULL;

IF COL_LENGTH(N'{schema}.runs', N'non_token_currency') IS NULL
ALTER TABLE {schema}.runs ADD non_token_currency nvarchar(16) NULL;

IF COL_LENGTH(N'{schema}.runs', N'non_token_estimated') IS NULL
ALTER TABLE {schema}.runs ADD non_token_estimated bit NULL;

IF COL_LENGTH(N'{schema}.runs', N'non_token_unsummed') IS NULL
ALTER TABLE {schema}.runs ADD non_token_unsummed int NULL;

IF COL_LENGTH(N'{schema}.tool_invocations', N'usage_provider') IS NULL
ALTER TABLE {schema}.tool_invocations ADD usage_provider nvarchar(100) NULL;

IF COL_LENGTH(N'{schema}.tool_invocations', N'usage_model') IS NULL
ALTER TABLE {schema}.tool_invocations ADD usage_model nvarchar(200) NULL;

-- One row per reported item; ON DELETE CASCADE removes the rows with the run.
IF OBJECT_ID(N'{schema}.run_non_token_usage', N'U') IS NULL
CREATE TABLE {schema}.run_non_token_usage (
    id         uniqueidentifier  NOT NULL CONSTRAINT run_non_token_usage_pk PRIMARY KEY NONCLUSTERED,
    run_id     uniqueidentifier  NOT NULL
        CONSTRAINT run_non_token_usage_run_fk REFERENCES {schema}.runs (id) ON DELETE CASCADE,
    node_name  nvarchar(200)     NULL,
    unit       nvarchar(64)      NOT NULL,
    quantity   decimal(20,10)    NOT NULL,
    cost       decimal(20,10)    NULL,
    currency   nvarchar(16)      NULL,
    estimated  bit               NOT NULL,
    provider   nvarchar(100)     NULL,
    model      nvarchar(200)     NULL,
    created_at datetimeoffset(7) NOT NULL
);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'run_non_token_usage_cx' AND object_id = OBJECT_ID(N'{schema}.run_non_token_usage'))
CREATE UNIQUE CLUSTERED INDEX run_non_token_usage_cx ON {schema}.run_non_token_usage (run_id, created_at, id);
