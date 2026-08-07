-- Faz 42 -- tek yurutucu secimi kira tablosu.
--
-- Gerekce icin PostgreSQL 0019_singleton_leases.sql'e bakin. Kiraci sutunu
-- YOKTUR: tek yurutucu secimi kurulum genelinde bir isletim kavramidir.

IF OBJECT_ID(N'{schema}.singleton_leases', N'U') IS NULL
CREATE TABLE {schema}.singleton_leases (
    name       nvarchar(200)  NOT NULL CONSTRAINT singleton_leases_pk PRIMARY KEY,
    owner_id   nvarchar(200)  NOT NULL,
    expires_at datetimeoffset NOT NULL,
    updated_at datetimeoffset NOT NULL
);
