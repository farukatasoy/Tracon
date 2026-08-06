-- Faz 31 -- calistirma ve mesaj basina insan (veya yargic) puani.
--
-- Gerekce ve sutun anlamlari icin PostgreSQL 0017_run_scores.sql'e bakin.
--
-- 🚨 NULL benzersizligi SQL Server'da TERS calisir (K-184): bir UNIQUE
-- indeks NULL'lari BIRBIRINE ESIT sayar (PostgreSQL'in aksine). Bu, message_id
-- icin TAM istedigimiz davranistir (COALESCE gerekmez) ama author icin
-- TERSIDIR: author NULL oldugunda (kimliksiz kurulum) her cagrinin YENI bir
-- satir acmasi istenir, iki NULL'un CATISMASI degil. Cozum: indeks
-- `WHERE author IS NOT NULL` ile FILTRELENIR -- author NULL oldugunda indeks
-- hic devreye girmez ve UPDATE dalinin `author = @author` karsilastirmasi
-- (NULL ile hicbir zaman eslesmeyen bir UNKNOWN) zaten INSERT'e duser.

IF OBJECT_ID(N'{schema}.run_scores', N'U') IS NULL
CREATE TABLE {schema}.run_scores (
    id          uniqueidentifier NOT NULL PRIMARY KEY,
    tenant_id   nvarchar(200)    NOT NULL,
    run_id      uniqueidentifier NOT NULL,
    message_id  nvarchar(200)    NULL,
    kind        smallint         NOT NULL,
    value       int              NOT NULL,
    comment     nvarchar(max)    NULL,
    source      nvarchar(50)     NOT NULL,
    author      nvarchar(200)    NULL,
    created_at  datetimeoffset   NOT NULL
);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'run_scores_target_author_idx' AND object_id = OBJECT_ID(N'{schema}.run_scores'))
CREATE UNIQUE INDEX run_scores_target_author_idx
    ON {schema}.run_scores (tenant_id, run_id, message_id, author)
    WHERE author IS NOT NULL;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'run_scores_run_idx' AND object_id = OBJECT_ID(N'{schema}.run_scores'))
CREATE INDEX run_scores_run_idx
    ON {schema}.run_scores (tenant_id, run_id);
