-- Faz 31 -- calistirma ve mesaj basina insan (veya yargic) puani.
--
-- Gerekce ve sutun anlamlari icin PostgreSQL 0017_run_scores.sql'e bakin.
--
-- 🚨 Tablo ve indeks adlari ONEK tasir (K-193): SQLite'ta nesne adlari
-- veritabani genelinde tek ad alanini paylasir.
--
-- Upsert PostgreSQL ile birebir aynidir (K-194): message_id COALESCE(…, '')
-- ile esitlenir, author BILEREK COALESCE EDILMEZ -- SQLite de PostgreSQL
-- gibi NULL'lari birbirine esit SAYMAZ, bu yuzden author bos oldugunda
-- (kimliksiz kurulum) her cagri yeni bir satir acar.

CREATE TABLE IF NOT EXISTS {schema}run_scores (
    id          TEXT    NOT NULL PRIMARY KEY,
    tenant_id   TEXT    NOT NULL,
    run_id      TEXT    NOT NULL,
    message_id  TEXT    NULL,
    kind        INTEGER NOT NULL,
    value       INTEGER NOT NULL,
    comment     TEXT    NULL,
    source      TEXT    NOT NULL,
    author      TEXT    NULL,
    created_at  TEXT    NOT NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS {schema}run_scores_target_author_idx
    ON {schema}run_scores (tenant_id, run_id, COALESCE(message_id, ''), author);

CREATE INDEX IF NOT EXISTS {schema}run_scores_run_idx
    ON {schema}run_scores (tenant_id, run_id);
