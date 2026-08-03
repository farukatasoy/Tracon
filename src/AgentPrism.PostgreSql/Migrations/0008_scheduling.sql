-- Faz 17 -- toplu ve zamanlanmis calistirma: is kuyrugu ve zamanlama tanimlari.

-- Zamanlama TANIMLARI. Kuyruktaki fiili isler `jobs` tablosundadir; bu ayrim
-- `workflows` / `workflow_checkpoints` ayrimiyla aynidir (Faz 15).
CREATE TABLE IF NOT EXISTS {schema}.job_schedules (
    id            uuid        NOT NULL PRIMARY KEY,
    tenant_id     text        NOT NULL,
    name          text        NOT NULL,
    kind          smallint    NOT NULL,        -- 0=AgentBatch, 1=Workflow, 2=Eval
    target_name   text        NOT NULL,        -- agent veya workflow adi
    cron          text,                        -- NULL = yalniz elle tetiklenir
    time_zone     text        NOT NULL DEFAULT 'UTC',
    payload       jsonb       NOT NULL,        -- girdi kumesi veya parametreler
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
    -- Yaz saati gecisinde ayni zamanlamanin iki kez tetiklenmesine karsi ikinci
    -- savunma hatti (birincisi TryClaimNextRunAsync'in atomik CAS'i): NULL
    -- schedule_id PostgreSQL'de birbirinden ayirt edilir, bu yuzden elle
    -- olusturulan (zamanlamasiz) isler bu kisitla cakismaz.
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
    run_id      uuid,                          -- olusan calistirma
    status      smallint    NOT NULL,
    error       text,
    CONSTRAINT job_items_job_seq_uq UNIQUE (job_id, seq)
);
