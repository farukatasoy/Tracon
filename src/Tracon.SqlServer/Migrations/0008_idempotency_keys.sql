-- Phase 43 -- Idempotency-Key support.
--
-- For the rationale and the column meanings see PostgreSQL 0020_idempotency_keys.sql.
--
-- 🚨 `key` is a RESERVED word in T-SQL; it is put in square brackets as [key]
-- everywhere.

IF OBJECT_ID(N'{schema}.idempotency_keys', N'U') IS NULL
CREATE TABLE {schema}.idempotency_keys (
    tenant_id    nvarchar(200)  NOT NULL,
    [key]        nvarchar(200)  NOT NULL,
    fingerprint  nvarchar(64)   NOT NULL,
    state        smallint       NOT NULL,
    status_code  int            NULL,
    content_type nvarchar(100)  NULL,
    body         nvarchar(max)  NULL,
    run_id       uniqueidentifier NULL,
    created_at   datetimeoffset NOT NULL,
    completed_at datetimeoffset NULL,
    CONSTRAINT idempotency_keys_pk PRIMARY KEY (tenant_id, [key])
);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'idempotency_keys_created_idx' AND object_id = OBJECT_ID(N'{schema}.idempotency_keys'))
CREATE INDEX idempotency_keys_created_idx
    ON {schema}.idempotency_keys (tenant_id, created_at DESC);
