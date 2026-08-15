-- Phase 25 -- data retention policy and archiving.
--
-- NO default policy IS ADDED: an empty policy table = nothing is deleted.

IF OBJECT_ID(N'{schema}.retention_policies', N'U') IS NULL
CREATE TABLE {schema}.retention_policies (
    id           uniqueidentifier  NOT NULL CONSTRAINT retention_policies_pk PRIMARY KEY,
    tenant_id    nvarchar(200)     NOT NULL,          -- N'*' = all tenants
    target       nvarchar(200)     NOT NULL,          -- N'run_events', N'spans', ...
    max_age_days int               NULL,
    max_rows     bigint            NULL,
    archive      bit               NOT NULL CONSTRAINT retention_policies_archive_default DEFAULT 0,
    enabled      bit               NOT NULL CONSTRAINT retention_policies_enabled_default DEFAULT 1,
    created_at   datetimeoffset(7) NOT NULL,
    updated_at   datetimeoffset(7) NOT NULL,
    CONSTRAINT retention_policies_uq UNIQUE (tenant_id, target)
);

-- tenant_id WAS NOT in the first draft of the doc; the tenant based policy
-- (K-198) needs the runs to be filterable by tenant as well. Because it can be
-- high volume the primary key is NONCLUSTERED and the clustered index is put on
-- the time column (the same reason as K-180).
IF OBJECT_ID(N'{schema}.retention_runs', N'U') IS NULL
CREATE TABLE {schema}.retention_runs (
    id            uniqueidentifier  NOT NULL CONSTRAINT retention_runs_pk PRIMARY KEY NONCLUSTERED,
    tenant_id     nvarchar(200)     NOT NULL,
    target        nvarchar(200)     NOT NULL,
    deleted_rows  bigint            NOT NULL CONSTRAINT retention_runs_deleted_default DEFAULT 0,
    archived_rows bigint            NOT NULL CONSTRAINT retention_runs_archived_default DEFAULT 0,
    started_at    datetimeoffset(7) NOT NULL,
    completed_at  datetimeoffset(7) NULL,
    error         nvarchar(max)     NULL
);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'retention_runs_cx' AND object_id = OBJECT_ID(N'{schema}.retention_runs'))
CREATE UNIQUE CLUSTERED INDEX retention_runs_cx ON {schema}.retention_runs (started_at, id);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'retention_runs_tenant_idx' AND object_id = OBJECT_ID(N'{schema}.retention_runs'))
CREATE INDEX retention_runs_tenant_idx ON {schema}.retention_runs (tenant_id, started_at DESC);
