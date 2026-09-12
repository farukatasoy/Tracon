-- Phase 42 -- single executor election lease table.
--
-- For the rationale see PostgreSQL 0019_singleton_leases.sql. There is NO tenant
-- column: single executor election is an operations concept for the installation.

IF OBJECT_ID(N'{schema}.singleton_leases', N'U') IS NULL
CREATE TABLE {schema}.singleton_leases (
    name       nvarchar(200)  NOT NULL CONSTRAINT singleton_leases_pk PRIMARY KEY,
    owner_id   nvarchar(200)  NOT NULL,
    expires_at datetimeoffset NOT NULL,
    updated_at datetimeoffset NOT NULL
);
