-- Storage of idempotency response headers.
--
-- For the rationale see PostgreSQL 0029_idempotency_response_headers.sql.

IF COL_LENGTH(N'{schema}.idempotency_keys', N'headers') IS NULL
ALTER TABLE {schema}.idempotency_keys ADD headers nvarchar(max) NULL;
