-- Phase 209 -- the inbox of workflow requests (F-319, K-128 reopened).
--
-- For the rationale and the column meanings see PostgreSQL 0061_workflow_requests.sql.
-- Index key sizes: the logical index is 3 x nvarchar(200) + datetimeoffset = 1210 bytes,
-- under the 1700 of a nonclustered key. The expiry index is filtered; '=' and IS NOT NULL
-- are allowed in a filter.

IF OBJECT_ID(N'{schema}.workflow_requests', N'U') IS NULL
CREATE TABLE {schema}.workflow_requests (
    run_id               uniqueidentifier  NOT NULL
        CONSTRAINT workflow_requests_run_fk REFERENCES {schema}.runs (id) ON DELETE CASCADE,
    request_id           nvarchar(200)     NOT NULL,
    tenant_id            nvarchar(200)     NOT NULL,
    session_id           nvarchar(200)     NOT NULL,
    workflow_name        nvarchar(200)     NOT NULL,
    port_id              nvarchar(200)     NOT NULL,
    event_seq            bigint            NOT NULL,
    requested_at         datetimeoffset(7) NOT NULL,
    expires_at           datetimeoffset(7) NULL,
    status               smallint          NOT NULL,
    answered_at          datetimeoffset(7) NULL,
    last_response_run_id uniqueidentifier  NULL,
    closed_at            datetimeoffset(7) NULL,
    CONSTRAINT workflow_requests_pk PRIMARY KEY (run_id, request_id)
);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'workflow_requests_inbox_idx' AND object_id = OBJECT_ID(N'{schema}.workflow_requests'))
CREATE INDEX workflow_requests_inbox_idx ON {schema}.workflow_requests (tenant_id, status, requested_at);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'workflow_requests_logical_idx' AND object_id = OBJECT_ID(N'{schema}.workflow_requests'))
CREATE INDEX workflow_requests_logical_idx ON {schema}.workflow_requests (tenant_id, session_id, request_id, requested_at);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'workflow_requests_expiry_idx' AND object_id = OBJECT_ID(N'{schema}.workflow_requests'))
CREATE INDEX workflow_requests_expiry_idx ON {schema}.workflow_requests (expires_at)
    WHERE status = 0 AND expires_at IS NOT NULL;
