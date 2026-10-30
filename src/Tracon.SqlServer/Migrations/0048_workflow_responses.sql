-- The answers to workflow requests.
--
-- For the rationale and the column meanings see PostgreSQL 0060_workflow_responses.sql.
-- The claim index is a filtered unique index; '<>' is allowed in its filter (measured on
-- SQL Server 2022). Key size: 3 x nvarchar(200) = 1200 bytes, under 1700.

IF OBJECT_ID(N'{schema}.workflow_responses', N'U') IS NULL
CREATE TABLE {schema}.workflow_responses (
    response_run_id uniqueidentifier NOT NULL CONSTRAINT workflow_responses_pk PRIMARY KEY,
    tenant_id       nvarchar(200)    NOT NULL,
    source_run_id   uniqueidentifier NOT NULL
        CONSTRAINT workflow_responses_source_fk REFERENCES {schema}.runs (id) ON DELETE CASCADE,
    session_id      nvarchar(200)    NOT NULL,
    request_id      nvarchar(200)    NOT NULL,
    claim_scope     nvarchar(200),
    answer_hash     char(64)         NOT NULL,
    status          smallint         NOT NULL,
    responded_by    nvarchar(200),
    responded_at    datetimeoffset   NOT NULL
);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'workflow_responses_claim_idx' AND object_id = OBJECT_ID(N'{schema}.workflow_responses'))
CREATE UNIQUE INDEX workflow_responses_claim_idx ON {schema}.workflow_responses (tenant_id, claim_scope, request_id)
    WHERE claim_scope IS NOT NULL AND status <> 2;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'workflow_responses_source_idx' AND object_id = OBJECT_ID(N'{schema}.workflow_responses'))
CREATE INDEX workflow_responses_source_idx ON {schema}.workflow_responses (tenant_id, source_run_id);
