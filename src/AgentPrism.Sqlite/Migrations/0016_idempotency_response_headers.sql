-- Idempotency yanit basliklarinin saklanmasi (MT-JOB-083 / HATA-S3-008).
--
-- Gerekce icin PostgreSQL 0029_idempotency_response_headers.sql'e bakin.
-- SQLite'ta `ALTER TABLE ... ADD COLUMN` icin `IF NOT EXISTS` YOKTUR; guvenlik
-- migration kosucusundan gelir (ayni dosya ikinci kez calismaz).

ALTER TABLE {schema}idempotency_keys ADD COLUMN headers TEXT;
