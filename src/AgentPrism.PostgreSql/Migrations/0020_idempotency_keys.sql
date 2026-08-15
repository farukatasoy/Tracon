-- ---------------------------------------------------------------------------
-- 0020 — Idempotency-Key support (phase 43)
--
-- Holds the requests reserved with an Idempotency-Key header and the response
-- they store when they complete. Only two states are persistent: 0=Reserved (the
-- request is still being processed), 2=Completed (the response is stored).
-- InProgress/FingerprintMismatch are derived at read (docs/43-IDEMPOTENCY-KEY.md, 43.2).
--
-- The stored body carries NO SECRET (K-059): it is the same response that was
-- ALREADY sent to the client, only its lifetime gets longer (section 43.5).
-- ---------------------------------------------------------------------------

CREATE TABLE IF NOT EXISTS {schema}.idempotency_keys (
    tenant_id    text        NOT NULL,
    "key"        text        NOT NULL,
    fingerprint  text        NOT NULL,
    state        smallint    NOT NULL,
    status_code  integer,
    content_type text,
    body         text,
    run_id       uuid,
    created_at   timestamptz NOT NULL,
    completed_at timestamptz,
    CONSTRAINT idempotency_keys_pkey PRIMARY KEY (tenant_id, key)
);

CREATE INDEX IF NOT EXISTS idempotency_keys_created_idx
    ON {schema}.idempotency_keys (tenant_id, created_at DESC);
