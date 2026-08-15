-- Phase 55 -- asynchronous approval inbox.
--
-- For the rationale and the column meanings see PostgreSQL 0027_pending_approvals.sql.

IF OBJECT_ID(N'{schema}.pending_approvals', N'U') IS NULL
CREATE TABLE {schema}.pending_approvals (
    id          uniqueidentifier NOT NULL CONSTRAINT pending_approvals_pk PRIMARY KEY,
    tenant_id   nvarchar(200)    NOT NULL,
    run_id      uniqueidentifier NOT NULL
        CONSTRAINT pending_approvals_run_fk REFERENCES {schema}.runs (id) ON DELETE CASCADE,
    session_id  nvarchar(200)    NOT NULL,
    request_id  nvarchar(200)    NOT NULL,
    tool_name   nvarchar(200)    NOT NULL,
    arguments   nvarchar(max),
    status      smallint         NOT NULL,
    decided_by  nvarchar(200),
    decided_at  datetimeoffset,
    expires_at  datetimeoffset   NOT NULL,
    created_at  datetimeoffset   NOT NULL
);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'pending_approvals_tenant_status_idx' AND object_id = OBJECT_ID(N'{schema}.pending_approvals'))
CREATE INDEX pending_approvals_tenant_status_idx ON {schema}.pending_approvals (tenant_id, status, created_at);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'pending_approvals_expiry_idx' AND object_id = OBJECT_ID(N'{schema}.pending_approvals'))
CREATE INDEX pending_approvals_expiry_idx ON {schema}.pending_approvals (status, expires_at);
