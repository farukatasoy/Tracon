-- Faz 56 -- kanarya yayini ve otomatik geri alma.
--
-- Gerekce ve sutun anlamlari icin PostgreSQL 0028_experiment_canary.sql'e
-- bakin. SQLite'ta `ALTER TABLE ... ADD COLUMN` icin `IF NOT EXISTS` YOKTUR;
-- guvenlik migration kosucusundan gelir (ayni dosya ikinci kez calismaz).
--
-- 🚨 Indeks adi TABLO ONEKINI tasir (K-193): SQLite'ta indeks adlari veritabani
-- genelinde tek ad alanini paylasir.

ALTER TABLE {schema}experiments ADD COLUMN canary_policy   TEXT;
ALTER TABLE {schema}experiments ADD COLUMN rollback_reason TEXT;

CREATE INDEX IF NOT EXISTS {schema}experiments_running_canary_idx
    ON {schema}experiments (status)
    WHERE status = 1 AND canary_policy IS NOT NULL;
