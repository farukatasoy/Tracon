-- Faz 18 -- degerlendirme (eval): takim, vaka, kosu ve vaka sonucu tablolari.

CREATE TABLE IF NOT EXISTS {schema}.eval_suites (
    id          uuid        NOT NULL PRIMARY KEY,
    tenant_id   text        NOT NULL,
    name        text        NOT NULL,
    description text,
    agent_name  text        NOT NULL,
    checks      jsonb       NOT NULL DEFAULT '[]',
    created_at  timestamptz NOT NULL,
    updated_at  timestamptz NOT NULL,
    CONSTRAINT eval_suites_tenant_name_uq UNIQUE (tenant_id, name)
);

CREATE TABLE IF NOT EXISTS {schema}.eval_cases (
    id              uuid    NOT NULL PRIMARY KEY,
    suite_id        uuid    NOT NULL REFERENCES {schema}.eval_suites (id) ON DELETE CASCADE,
    seq             integer NOT NULL,
    query           text    NOT NULL,
    expected_output text,
    expected_tools  text,          -- virgulle ayrilmis tool adlari
    context         text,
    CONSTRAINT eval_cases_suite_seq_uq UNIQUE (suite_id, seq)
);

CREATE TABLE IF NOT EXISTS {schema}.eval_runs (
    id             uuid        NOT NULL PRIMARY KEY,
    tenant_id      text        NOT NULL,
    suite_id       uuid        NOT NULL REFERENCES {schema}.eval_suites (id) ON DELETE CASCADE,
    job_id         uuid,                       -- kosuyu yuruten is kaydi
    agent_version  integer,                     -- hangi surum olculdu
    model_id       text,
    status         smallint    NOT NULL,        -- 0=Pending 1=Running 2=Completed 3=Failed 4=Cancelled
    total          integer     NOT NULL DEFAULT 0,
    passed         integer     NOT NULL DEFAULT 0,
    failed         integer     NOT NULL DEFAULT 0,
    input_tokens   bigint,
    output_tokens  bigint,
    started_at     timestamptz NOT NULL,
    completed_at   timestamptz
);

CREATE INDEX IF NOT EXISTS eval_runs_suite_started_idx
    ON {schema}.eval_runs (tenant_id, suite_id, started_at DESC);

CREATE INDEX IF NOT EXISTS eval_runs_job_idx
    ON {schema}.eval_runs (tenant_id, job_id);

CREATE TABLE IF NOT EXISTS {schema}.eval_case_results (
    id             uuid    NOT NULL PRIMARY KEY,
    eval_run_id    uuid    NOT NULL REFERENCES {schema}.eval_runs (id) ON DELETE CASCADE,
    -- case_id KASITLI OLARAK yabanci anahtar tasimaz: bir vaka sonradan
    -- degistirilse veya takimdan silinse bile gecmis sonuc kaydi anlasilir
    -- kalir (append-only ruh, ayni gerekce run_events'in run_id'si icin gecerli).
    case_id        uuid    NOT NULL,
    run_id         uuid,                       -- olusan calistirma
    passed         boolean NOT NULL,
    output         text,
    scores         jsonb   NOT NULL DEFAULT '[]',
    failure_reason text
);

CREATE INDEX IF NOT EXISTS eval_case_results_run_idx
    ON {schema}.eval_case_results (eval_run_id);
