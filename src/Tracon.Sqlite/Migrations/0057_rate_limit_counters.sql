-- Phase 263 (F-92) -- shared rate-limit counter.
--
-- For the rationale and the column meaning see PostgreSQL 0070_rate_limit_counters.sql. There is NO
-- tenant column: the tenant is part of the partition key.
--
-- 🚨 The table name carries the PREFIX (K-193): in SQLite object names share a single database wide
-- namespace. window_start is the fixed-width UTC text SqliteDialect.AddTimestamp writes
-- (yyyy-MM-ddTHH:mm:ss.fffffffZ), so comparing it and MAX() order by time. This table has no index.

CREATE TABLE IF NOT EXISTS {schema}rate_limit_counters (
    partition_key TEXT    NOT NULL PRIMARY KEY,
    window_start  TEXT    NOT NULL,
    permit_count  INTEGER NOT NULL
);
