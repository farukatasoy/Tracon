-- Persisted payload version stamps (Phase 126).
--
-- For the rationale see PostgreSQL 0040_persisted_payload_version.sql.
-- SQL Server has no `ADD COLUMN IF NOT EXISTS`; the guards are explicit
-- catalog checks, the same shape the other SQL Server migrations use.

EXEC sp_rename N'{schema}.sessions.schema_version', N'state_schema_version', N'COLUMN';

IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID(N'{schema}.sessions') AND name = N'state_maf_version')
BEGIN
    ALTER TABLE {schema}.sessions
        ADD state_maf_version nvarchar(max) NULL;
END;

IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID(N'{schema}.workflow_checkpoints') AND name = N'state_schema_version')
BEGIN
    ALTER TABLE {schema}.workflow_checkpoints
        ADD state_schema_version int NULL;
END;

IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID(N'{schema}.workflow_checkpoints') AND name = N'state_maf_version')
BEGIN
    ALTER TABLE {schema}.workflow_checkpoints
        ADD state_maf_version nvarchar(max) NULL;
END;
