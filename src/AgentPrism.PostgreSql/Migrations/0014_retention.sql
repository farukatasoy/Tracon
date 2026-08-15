-- Phase 25 -- data retention policy and archiving.
--
-- NO default policy IS ADDED: an empty policy table = nothing is deleted.
-- A version upgrade must not start a deletion that nobody asked for.

CREATE TABLE IF NOT EXISTS {schema}.retention_policies (
    id           uuid        NOT NULL PRIMARY KEY,
    tenant_id    text        NOT NULL,          -- '*' = all tenants
    target       text        NOT NULL,          -- 'run_events', 'spans', ...
    max_age_days integer,
    max_rows     bigint,
    archive      boolean     NOT NULL DEFAULT false,
    enabled      boolean     NOT NULL DEFAULT true,
    created_at   timestamptz NOT NULL,
    updated_at   timestamptz NOT NULL,
    CONSTRAINT retention_policies_uq UNIQUE (tenant_id, target)
);

-- tenant_id WAS NOT in the first draft of the doc; the tenant based policy
-- (K-198) needs the runs to be filterable by tenant as well.
CREATE TABLE IF NOT EXISTS {schema}.retention_runs (
    id            uuid        NOT NULL PRIMARY KEY,
    tenant_id     text        NOT NULL,
    target        text        NOT NULL,
    deleted_rows  bigint      NOT NULL DEFAULT 0,
    archived_rows bigint      NOT NULL DEFAULT 0,
    started_at    timestamptz NOT NULL,
    completed_at  timestamptz,
    error         text
);

CREATE INDEX IF NOT EXISTS retention_runs_tenant_started_idx
    ON {schema}.retention_runs (tenant_id, started_at DESC);
