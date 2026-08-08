-- Faz 54 -- oksuz calistirma uzlastirmasi.
--
-- Gerekce ve sutun anlami icin PostgreSQL 0026_run_heartbeat.sql'e bakin.
-- SQLite'ta `ALTER TABLE ... ADD COLUMN` icin `IF NOT EXISTS` YOKTUR; guvenlik
-- migration kosucusundan gelir (ayni dosya ikinci kez calismaz). Indeks adi
-- TABLO ONEKINI tasir (K-193): SQLite'ta indeks adlari veritabani genelinde
-- tek ad alanini paylasir.

ALTER TABLE {schema}runs ADD COLUMN heartbeat_at TEXT NULL;

CREATE INDEX IF NOT EXISTS {schema}runs_running_heartbeat_idx
    ON {schema}runs (heartbeat_at)
    WHERE status = 0;
