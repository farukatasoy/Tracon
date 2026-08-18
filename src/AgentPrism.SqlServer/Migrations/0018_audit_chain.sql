-- Phase 64 -- audit trail hash chain.
--
-- For the rationale (nullable columns, no backfill, filtered unique index) see
-- PostgreSQL 0031_audit_chain.sql.
--
-- 🚨 SQL Server treats NULL as EQUAL inside a unique index (K-184's flipped
-- case), so the index below needs no COALESCE the way PostgreSQL/SQLite do.
--
-- 🚨 `chain_seq` exists ONLY to find "the tenant's most recently written row"
-- (SqlAuditLog.SelectLastAuditHash/SelectAuditChain order by it, DESC/ASC).
-- For the full rationale (why ordering by `(created_at, id)` is WRONG — a
-- uuid v7's low bits are random, not insertion order, and a writer can be
-- reported as "last" forever) see PostgreSQL 0031_audit_chain.sql. SQL Server
-- has no `GENERATED ... AS IDENTITY` that can be added to an EXISTING table
-- (`IDENTITY` can only be set at table CREATE time); a plain `SEQUENCE` with
-- `DEFAULT NEXT VALUE FOR` gives the same "database-assigned, strictly
-- increasing" guarantee without that restriction.

IF COL_LENGTH(N'{schema}.audit_log', N'prev_hash') IS NULL
ALTER TABLE {schema}.audit_log ADD prev_hash nvarchar(64) NULL;

IF COL_LENGTH(N'{schema}.audit_log', N'hash') IS NULL
ALTER TABLE {schema}.audit_log ADD hash nvarchar(64) NULL;

IF NOT EXISTS (SELECT 1 FROM sys.sequences WHERE name = N'audit_chain_seq' AND schema_id = SCHEMA_ID(N'{schema}'))
EXEC(N'CREATE SEQUENCE {schema}.audit_chain_seq AS bigint START WITH 1 INCREMENT BY 1;');

IF COL_LENGTH(N'{schema}.audit_log', N'chain_seq') IS NULL
EXEC(N'ALTER TABLE {schema}.audit_log ADD chain_seq bigint NOT NULL DEFAULT (NEXT VALUE FOR {schema}.audit_chain_seq);');

-- 🚨 Wrapped in EXEC: columns are added above with ALTER TABLE in the SAME
-- batch; without EXEC it gives "Invalid column name" (see 0003_tool_usage.sql).
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'audit_log_tenant_prev_hash_idx' AND object_id = OBJECT_ID(N'{schema}.audit_log'))
EXEC(N'CREATE UNIQUE INDEX audit_log_tenant_prev_hash_idx
    ON {schema}.audit_log (tenant_id, prev_hash)
    WHERE hash IS NOT NULL;');

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'audit_log_tenant_chain_seq_idx' AND object_id = OBJECT_ID(N'{schema}.audit_log'))
EXEC(N'CREATE INDEX audit_log_tenant_chain_seq_idx
    ON {schema}.audit_log (tenant_id, chain_seq DESC);');
