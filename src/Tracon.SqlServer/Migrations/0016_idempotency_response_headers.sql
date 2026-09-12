-- Storage of idempotency response headers (MT-JOB-083 / HATA-S3-008).
--
-- For the rationale see PostgreSQL 0029_idempotency_response_headers.sql.

IF COL_LENGTH(N'{schema}.idempotency_keys', N'headers') IS NULL
ALTER TABLE {schema}.idempotency_keys ADD headers nvarchar(max) NULL;
