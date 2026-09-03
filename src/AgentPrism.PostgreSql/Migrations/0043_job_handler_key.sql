-- ---------------------------------------------------------------------------
-- 0043 — The job's kind becomes an open handler key (Phase 137)
--
-- `kind` was a closed smallint enum with nine values, and all nine had a
-- built-in handler. A consumer could therefore never add a job type: the
-- worker picked its handler by enum equality in DI REGISTRATION ORDER, so a
-- third-party handler either never ran or shadowed a built-in one.
--
-- `handler_key` replaces it as BOTH the classification and the dispatch
-- identity; there is no second column. AgentPrism's own keys live in the
-- reserved `agentprism.` namespace, a consumer picks its own prefix.
--
-- The backfill maps all nine values; the order is JobKind's own numbering, so
-- an upgraded row keeps meaning exactly what it meant before. `kind` is
-- dropped only after the new column is NOT NULL, so the statement order here
-- is also the rollback-safety order: a failure before the DROP leaves both
-- columns populated and consistent.
-- ---------------------------------------------------------------------------

ALTER TABLE {schema}.jobs           ADD COLUMN IF NOT EXISTS handler_key text;
ALTER TABLE {schema}.job_schedules  ADD COLUMN IF NOT EXISTS handler_key text;

UPDATE {schema}.jobs SET handler_key = CASE kind
    WHEN 0 THEN 'agentprism.agent-batch'
    WHEN 1 THEN 'agentprism.workflow'
    WHEN 2 THEN 'agentprism.eval'
    WHEN 3 THEN 'agentprism.webhook-delivery'
    WHEN 4 THEN 'agentprism.retention'
    WHEN 5 THEN 'agentprism.agent-run'
    WHEN 6 THEN 'agentprism.online-eval'
    WHEN 7 THEN 'agentprism.approval-resume'
    WHEN 8 THEN 'agentprism.run-continuation'
END
WHERE handler_key IS NULL;

UPDATE {schema}.job_schedules SET handler_key = CASE kind
    WHEN 0 THEN 'agentprism.agent-batch'
    WHEN 1 THEN 'agentprism.workflow'
    WHEN 2 THEN 'agentprism.eval'
    WHEN 3 THEN 'agentprism.webhook-delivery'
    WHEN 4 THEN 'agentprism.retention'
    WHEN 5 THEN 'agentprism.agent-run'
    WHEN 6 THEN 'agentprism.online-eval'
    WHEN 7 THEN 'agentprism.approval-resume'
    WHEN 8 THEN 'agentprism.run-continuation'
END
WHERE handler_key IS NULL;

ALTER TABLE {schema}.jobs           ALTER COLUMN handler_key SET NOT NULL;
ALTER TABLE {schema}.job_schedules  ALTER COLUMN handler_key SET NOT NULL;

ALTER TABLE {schema}.jobs           DROP COLUMN IF EXISTS kind;
ALTER TABLE {schema}.job_schedules  DROP COLUMN IF EXISTS kind;
