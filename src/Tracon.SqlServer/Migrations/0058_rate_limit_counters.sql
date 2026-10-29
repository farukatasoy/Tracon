-- Phase 263 (F-92) -- shared rate-limit counter.
--
-- For the rationale and the column meaning see PostgreSQL 0070_rate_limit_counters.sql. There is NO
-- tenant column: the tenant is part of the partition key.
--
-- partition_key is nvarchar(400): the primary key is an index key, and SQL Server caps an index key
-- at 900 bytes (nvarchar uses two bytes per character).

IF OBJECT_ID(N'{schema}.rate_limit_counters', N'U') IS NULL
CREATE TABLE {schema}.rate_limit_counters (
    partition_key nvarchar(400)  NOT NULL CONSTRAINT rate_limit_counters_pk PRIMARY KEY,
    window_start  datetimeoffset NOT NULL,
    permit_count  bigint         NOT NULL
);
