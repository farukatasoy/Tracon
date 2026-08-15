-- Phase 42 -- single executor election lease table.
--
-- For the rationale see PostgreSQL 0019_singleton_leases.sql. There is NO tenant
-- column: single executor election is an operations concept for the installation.
--
-- 🚨 The table name carries the PREFIX (K-193): in SQLite object names share a
-- single database wide namespace. This table has no index.

CREATE TABLE IF NOT EXISTS {schema}singleton_leases (
    name       TEXT NOT NULL PRIMARY KEY,
    owner_id   TEXT NOT NULL,
    expires_at TEXT NOT NULL,
    updated_at TEXT NOT NULL
);
