-- Idempotency yanit basliklarinin saklanmasi (MT-JOB-083 / HATA-S3-008).
--
-- Gerekce icin PostgreSQL 0029_idempotency_response_headers.sql'e bakin.

IF COL_LENGTH(N'{schema}.idempotency_keys', N'headers') IS NULL
ALTER TABLE {schema}.idempotency_keys ADD headers nvarchar(max) NULL;
