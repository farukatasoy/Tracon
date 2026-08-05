-- Faz 28 -- tool cagrisi basina token DISI olcum ve maliyet.
--
-- Gerekce ve sutun anlamlari icin PostgreSQL 0015_tool_usage.sql'e bakin.
--
-- SQLite'ta `ALTER TABLE ... ADD COLUMN` icin `IF NOT EXISTS` YOKTUR. Guvenlik
-- migration kosucusundan gelir: uygulanmis migration'lar kaydedilir ve ayni
-- dosya ikinci kez calistirilmaz.
--
-- `decimal` icin ozel islem gerekmez: surucu her zaman TEXT yazar ve kulturden
-- bagimsizdir (SQL Server'in Precision/Scale zorunlulugu burada YOKTUR).
--
-- 🚨 Indeks adi TABLO ONEKINI tasir (K-193): SQLite'ta indeks adlari veritabani
-- genelinde tek ad alanini paylasir. Onek yazilmazsa ayni `.db` dosyasini
-- paylasan iki farkli TablePrefix degeri catisir ve ikinci indeks SESSIZCE
-- atlanir.

ALTER TABLE {schema}tool_invocations ADD COLUMN usage_unit      TEXT    NULL;
ALTER TABLE {schema}tool_invocations ADD COLUMN usage_quantity  TEXT    NULL;
ALTER TABLE {schema}tool_invocations ADD COLUMN usage_estimated INTEGER NULL;
ALTER TABLE {schema}tool_invocations ADD COLUMN cost            TEXT    NULL;
ALTER TABLE {schema}tool_invocations ADD COLUMN cost_currency   TEXT    NULL;

CREATE INDEX IF NOT EXISTS {schema}tool_invocations_usage_idx
    ON {schema}tool_invocations (usage_unit, created_at DESC)
    WHERE usage_unit IS NOT NULL;
