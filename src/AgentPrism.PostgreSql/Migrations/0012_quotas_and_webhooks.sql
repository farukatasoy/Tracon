-- Phase 21 -- quota, rate limit counters and event publishing (webhook).

-- ---------------------------------------------------------------------------
-- Job queue: attempt limit per job
-- ---------------------------------------------------------------------------
-- Webhook delivery uses a different ladder (5 attempts) from the general
-- `AgentPrism:Scheduling:MaxAttempts` setting. NULL = the general setting holds (K-160).
ALTER TABLE {schema}.jobs ADD COLUMN IF NOT EXISTS max_attempts smallint;

-- ---------------------------------------------------------------------------
-- Quota definitions
-- ---------------------------------------------------------------------------
-- The rate limit (second/minute) and the quota (day/month) are SEPARATE concepts:
-- the rate limit lives in memory, the quota here. See docs/21-KOTA-VE-OLAY-YAYINI.md, 21.2.
CREATE TABLE IF NOT EXISTS {schema}.quotas (
    id           uuid           NOT NULL PRIMARY KEY,
    tenant_id    text           NOT NULL,
    agent_name   text,                            -- NULL = the whole tenant
    period       smallint       NOT NULL,         -- 0=Daily 1=Monthly
    max_runs     bigint,
    max_tokens   bigint,
    max_cost     numeric(20,10),                  -- binary floating point is not used in money arithmetic
    enabled      boolean        NOT NULL DEFAULT true,
    created_at   timestamptz    NOT NULL,
    updated_at   timestamptz    NOT NULL
);

-- 🚨 In PostgreSQL NULLs are not equal to each other: a plain UNIQUE would let
-- the same rule with a NULL agent_name be added endlessly. The skill_scripts
-- lesson of phase 11 (docs/hafiza/postgresql.md).
CREATE UNIQUE INDEX IF NOT EXISTS quotas_scope_uq
    ON {schema}.quotas (tenant_id, COALESCE(agent_name, ''), period);

-- ---------------------------------------------------------------------------
-- Quota usage counters
-- ---------------------------------------------------------------------------
-- agent_name is NOT NULL DEFAULT '' here -- it cannot be NULL because it is part
-- of the primary key; '' means tenant wide. A run increases BOTH the agent row
-- AND the tenant wide row.
CREATE TABLE IF NOT EXISTS {schema}.quota_usage (
    tenant_id     text           NOT NULL,
    agent_name    text           NOT NULL DEFAULT '',
    period        smallint       NOT NULL,
    period_start  date           NOT NULL,
    runs          bigint         NOT NULL DEFAULT 0,
    tokens        bigint         NOT NULL DEFAULT 0,
    cost          numeric(20,10) NOT NULL DEFAULT 0,
    updated_at    timestamptz    NOT NULL,
    PRIMARY KEY (tenant_id, agent_name, period, period_start)
);

-- ---------------------------------------------------------------------------
-- Webhook subscriptions
-- ---------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS {schema}.webhook_subscriptions (
    id                       uuid        NOT NULL PRIMARY KEY,
    tenant_id                text        NOT NULL,
    name                     text        NOT NULL,
    url                      text        NOT NULL,
    events                   text[]      NOT NULL,
    -- 🚨 KEY NAME -- NOT A SECRET (K-059). The signing secret DOES NOT STAY in
    -- the database; only the name of the configuration key that the value is read
    -- from stays, and the value is resolved at run time through IConfiguration.
    secret_configuration_key text,
    headers                  jsonb       NOT NULL DEFAULT '{}'::jsonb,
    enabled                  boolean     NOT NULL DEFAULT true,
    -- Counter of consecutive failed deliveries; when it passes the threshold the
    -- subscription disables itself and the fact is written to the audit trail.
    consecutive_failures     integer     NOT NULL DEFAULT 0,
    created_at               timestamptz NOT NULL,
    updated_at               timestamptz NOT NULL,
    CONSTRAINT webhook_subscriptions_tenant_name_uq UNIQUE (tenant_id, name)
);

-- ---------------------------------------------------------------------------
-- Delivery history
-- ---------------------------------------------------------------------------
-- This table IS NOT A QUEUE: scheduling and leasing live in the `jobs` table of
-- phase 17 (K-160). The `attempt` here only reports history.
CREATE TABLE IF NOT EXISTS {schema}.webhook_deliveries (
    id              uuid        NOT NULL PRIMARY KEY,
    subscription_id uuid        NOT NULL REFERENCES {schema}.webhook_subscriptions (id) ON DELETE CASCADE,
    tenant_id       text        NOT NULL,
    event_type      text        NOT NULL,
    payload         text        NOT NULL,
    status          smallint    NOT NULL,      -- 0=Pending 1=Delivered 2=Failed 3=Dropped
    attempt         smallint    NOT NULL DEFAULT 0,
    response_code   integer,
    error           text,
    created_at      timestamptz NOT NULL,
    delivered_at    timestamptz
);

CREATE INDEX IF NOT EXISTS webhook_deliveries_subscription_idx
    ON {schema}.webhook_deliveries (subscription_id, created_at DESC);

CREATE INDEX IF NOT EXISTS webhook_deliveries_tenant_created_idx
    ON {schema}.webhook_deliveries (tenant_id, created_at DESC);
