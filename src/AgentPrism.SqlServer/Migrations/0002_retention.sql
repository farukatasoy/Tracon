-- Faz 25 -- veri saklama politikasi ve arsivleme.
--
-- Varsayilan politika EKLENMEZ: bos politika tablosu = hicbir sey silinmez.

IF OBJECT_ID(N'{schema}.retention_policies', N'U') IS NULL
CREATE TABLE {schema}.retention_policies (
    id           uniqueidentifier  NOT NULL CONSTRAINT retention_policies_pk PRIMARY KEY,
    tenant_id    nvarchar(200)     NOT NULL,          -- N'*' = tum kiracilar
    target       nvarchar(200)     NOT NULL,          -- N'run_events', N'spans', ...
    max_age_days int               NULL,
    max_rows     bigint            NULL,
    archive      bit               NOT NULL CONSTRAINT retention_policies_archive_default DEFAULT 0,
    enabled      bit               NOT NULL CONSTRAINT retention_policies_enabled_default DEFAULT 1,
    created_at   datetimeoffset(7) NOT NULL,
    updated_at   datetimeoffset(7) NOT NULL,
    CONSTRAINT retention_policies_uq UNIQUE (tenant_id, target)
);

-- tenant_id doc'un ilk taslaginda YOKTU; kiraci bazli politika (K-198)
-- kosularin da kiraciya gore filtrelenebilmesini gerektirir. Yuksek hacimli
-- olabilecegi icin birincil anahtar NONCLUSTERED, kumelenmis indeks zaman
-- sutununa kurulur (K-180 ile ayni gerekce).
IF OBJECT_ID(N'{schema}.retention_runs', N'U') IS NULL
CREATE TABLE {schema}.retention_runs (
    id            uniqueidentifier  NOT NULL CONSTRAINT retention_runs_pk PRIMARY KEY NONCLUSTERED,
    tenant_id     nvarchar(200)     NOT NULL,
    target        nvarchar(200)     NOT NULL,
    deleted_rows  bigint            NOT NULL CONSTRAINT retention_runs_deleted_default DEFAULT 0,
    archived_rows bigint            NOT NULL CONSTRAINT retention_runs_archived_default DEFAULT 0,
    started_at    datetimeoffset(7) NOT NULL,
    completed_at  datetimeoffset(7) NULL,
    error         nvarchar(max)     NULL
);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'retention_runs_cx' AND object_id = OBJECT_ID(N'{schema}.retention_runs'))
CREATE UNIQUE CLUSTERED INDEX retention_runs_cx ON {schema}.retention_runs (started_at, id);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'retention_runs_tenant_idx' AND object_id = OBJECT_ID(N'{schema}.retention_runs'))
CREATE INDEX retention_runs_tenant_idx ON {schema}.retention_runs (tenant_id, started_at DESC);
