-- Phase 261 (F-759) -- per-tenant tool policy.
--
-- For the rationale and column meanings see PostgreSQL 0068_tenant_tool_policies.sql.
--
-- 🚨 The table name carries the PREFIX (K-193): SQLite has no schema and object
-- names share a single database-wide namespace.

CREATE TABLE IF NOT EXISTS {schema}tenant_tool_policies (
    tenant_id            TEXT NOT NULL PRIMARY KEY,
    allowed_tools        TEXT NOT NULL,
    granted_permissions  TEXT NOT NULL,
    updated_at           TEXT NOT NULL
);
