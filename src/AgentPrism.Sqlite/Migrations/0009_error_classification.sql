-- Faz 44 -- hata siniflandirmasi ve ariza kumeleme.
--
-- Gerekce ve sutun anlamlari icin PostgreSQL 0021_error_classification.sql'e
-- bakin. SQLite'ta `ALTER TABLE ... ADD COLUMN` icin `IF NOT EXISTS` YOKTUR;
-- guvenlik migration kosucusundan gelir (ayni dosya ikinci kez calismaz).
--
-- 🚨 Indeks adi TABLO ONEKINI tasir (K-193): SQLite'ta indeks adlari veritabani
-- genelinde tek ad alanini paylasir.

ALTER TABLE {schema}runs ADD COLUMN error_class       INTEGER NULL;
ALTER TABLE {schema}runs ADD COLUMN error_fingerprint TEXT    NULL;

CREATE INDEX IF NOT EXISTS {schema}runs_error_class_idx
    ON {schema}runs (tenant_id, error_class, started_at DESC)
    WHERE error_class IS NOT NULL;
