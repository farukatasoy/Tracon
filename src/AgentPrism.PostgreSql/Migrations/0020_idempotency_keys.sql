-- ---------------------------------------------------------------------------
-- 0020 — Idempotency-Key destegi (Faz 43)
--
-- Bir Idempotency-Key basligiyla ayrilan istekleri ve tamamlandiklarinda
-- sakladiklari yaniti tutar. Yalniz iki durum kalicidir: 0=Reserved (istek
-- hala isleniyor), 2=Completed (yanit saklandi). InProgress/FingerprintMismatch
-- okuma aninda turetilir (docs/43-IDEMPOTENCY-KEY.md, bolum 43.2).
--
-- Saklanan gövde bir SECRET tasimaz (K-059): istemciye ZATEN gonderilmis olan
-- yanitin aynisidir, yalniz omru uzar (bolum 43.5).
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
