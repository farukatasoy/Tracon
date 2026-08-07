-- Faz 45 -- uretimden eval vakasi terfisi (F-53).
--
-- Gerekce ve sutun anlamlari icin PostgreSQL 0022_eval_case_source.sql'e bakin.
-- SQLite'ta `ALTER TABLE ... ADD COLUMN` icin `IF NOT EXISTS` YOKTUR; guvenlik
-- migration kosucusundan gelir (ayni dosya ikinci kez calismaz).
--
-- 🚨 Indeks adi TABLO ONEKINI tasir (K-193): SQLite'ta indeks adlari veritabani
-- genelinde tek ad alanini paylasir.

ALTER TABLE {schema}eval_cases ADD COLUMN source_run_id TEXT;
ALTER TABLE {schema}eval_cases ADD COLUMN source_kind   INTEGER;
ALTER TABLE {schema}eval_cases ADD COLUMN promoted_at   TEXT;

CREATE UNIQUE INDEX IF NOT EXISTS {schema}eval_cases_source_run_uq
    ON {schema}eval_cases (suite_id, source_run_id)
    WHERE source_run_id IS NOT NULL;
