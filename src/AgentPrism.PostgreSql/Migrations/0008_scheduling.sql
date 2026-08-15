-- Phase 17 -- batch and scheduled runs: the job queue and schedule definitions.

-- Schedule DEFINITIONS. The actual jobs in the queue are in the `jobs` table;
-- this split is the same as the `workflows` / `workflow_checkpoints` split (phase 15).
CREATE TABLE IF NOT EXISTS {schema}.job_schedules (
    id            uuid        NOT NULL PRIMARY KEY,
    tenant_id     text        NOT NULL,
    name          text        NOT NULL,
    kind          smallint    NOT NULL,        -- 0=AgentBatch, 1=Workflow, 2=Eval
    target_name   text        NOT NULL,        -- agent or workflow name
    cron          text,                        -- NULL = triggered only by hand
    time_zone     text        NOT NULL DEFAULT 'UTC',
    payload       jsonb       NOT NULL,        -- input set or parameters
    enabled       boolean     NOT NULL DEFAULT true,
    next_run_at   timestamptz,
    last_run_at   timestamptz,
    created_by    text,
    created_at    timestamptz NOT NULL,
    updated_at    timestamptz NOT NULL,
    CONSTRAINT job_schedules_tenant_name_uq UNIQUE (tenant_id, name)
);

CREATE TABLE IF NOT EXISTS {schema}.jobs (
    id            uuid        NOT NULL PRIMARY KEY,
    tenant_id     text        NOT NULL,
    schedule_id   uuid        REFERENCES {schema}.job_schedules (id) ON DELETE SET NULL,
    kind          smallint    NOT NULL,
    target_name   text        NOT NULL,
    status        smallint    NOT NULL,        -- 0=Pending 1=Leased 2=Running 3=Completed 4=Failed 5=Cancelled
    payload       jsonb       NOT NULL,
    total_items   integer     NOT NULL DEFAULT 0,
    done_items    integer     NOT NULL DEFAULT 0,
    failed_items  integer     NOT NULL DEFAULT 0,
    attempt       smallint    NOT NULL DEFAULT 0,
    lease_owner   text,
    lease_until   timestamptz,
    scheduled_for timestamptz NOT NULL,
    started_at    timestamptz,
    completed_at  timestamptz,
    error_message text,
    created_at    timestamptz NOT NULL,
    -- Second line of defence against the same schedule firing twice over a
    -- daylight saving change (the first is the atomic CAS of
    -- TryClaimNextRunAsync): PostgreSQL tells NULL schedule_id values apart, so
    -- jobs created by hand (with no schedule) do not clash with this constraint.
    CONSTRAINT jobs_schedule_scheduled_uq UNIQUE (schedule_id, scheduled_for)
);

CREATE INDEX IF NOT EXISTS jobs_claim_idx
    ON {schema}.jobs (status, scheduled_for)
    WHERE status IN (0, 1);

CREATE INDEX IF NOT EXISTS jobs_tenant_created_idx
    ON {schema}.jobs (tenant_id, created_at DESC);

CREATE TABLE IF NOT EXISTS {schema}.job_items (
    id          uuid        NOT NULL PRIMARY KEY,
    job_id      uuid        NOT NULL REFERENCES {schema}.jobs (id) ON DELETE CASCADE,
    seq         integer     NOT NULL,
    input       text        NOT NULL,
    run_id      uuid,                          -- the run that was created
    status      smallint    NOT NULL,
    error       text,
    CONSTRAINT job_items_job_seq_uq UNIQUE (job_id, seq)
);
