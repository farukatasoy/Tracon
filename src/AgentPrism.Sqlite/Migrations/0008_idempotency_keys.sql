-- Faz 43 -- Idempotency-Key destegi.
--
-- Gerekce ve sutun anlamlari icin PostgreSQL 0020_idempotency_keys.sql'e bakin.
--
-- 🚨 Tablo ve indeks adlari ONEK tasir (K-193). `key` SQLite'ta AYRILMIS bir
-- sozcuktur; her yerde "key" ile cift tirnaklanir.

CREATE TABLE IF NOT EXISTS {schema}idempotency_keys (
    tenant_id    TEXT    NOT NULL,
    "key"        TEXT    NOT NULL,
    fingerprint  TEXT    NOT NULL,
    state        INTEGER NOT NULL,
    status_code  INTEGER,
    content_type TEXT,
    body         TEXT,
    run_id       TEXT,
    created_at   TEXT    NOT NULL,
    completed_at TEXT,
    PRIMARY KEY (tenant_id, "key")
);

CREATE INDEX IF NOT EXISTS {schema}idempotency_keys_created_idx
    ON {schema}idempotency_keys (tenant_id, created_at DESC);
