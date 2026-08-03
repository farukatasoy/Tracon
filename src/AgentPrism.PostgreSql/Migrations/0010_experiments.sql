-- Faz 19 -- surum karsilastirma, diff ve A/B deneyleri.

-- ---------------------------------------------------------------------------
-- Calistirmalarin surum ve deney bilgisi
-- ---------------------------------------------------------------------------
-- agent_version: bu calistirmanin olctugu tanim surumu. Deney disi
-- calistirmalarda da doldurulur (RunRecordingAgentDecorator, descriptor.Version'i
-- varsayilan olarak verir).
-- experiment_id / variant: yalniz bir A/B deneyi tarafindan atanmis
-- calistirmalarda dolu.

ALTER TABLE {schema}.runs ADD COLUMN IF NOT EXISTS agent_version integer;
ALTER TABLE {schema}.runs ADD COLUMN IF NOT EXISTS experiment_id uuid;
ALTER TABLE {schema}.runs ADD COLUMN IF NOT EXISTS variant       text;

-- Surum bazli kirilim (RunStatistics.ByVersion) ve "bu surum ne kadar calisti"
-- sorgusu icin. Kismi indeks: agent_version dolu olmayan satirlar (Faz 19
-- oncesi kayitlar) indekste yer kaplamaz.
CREATE INDEX IF NOT EXISTS runs_agent_version_idx
    ON {schema}.runs (tenant_id, agent_name, agent_version, started_at DESC)
    WHERE agent_version IS NOT NULL;

-- Deney sonuc sorgusu (varyant bazinda GROUP BY) icin.
CREATE INDEX IF NOT EXISTS runs_experiment_idx
    ON {schema}.runs (experiment_id, variant)
    WHERE experiment_id IS NOT NULL;

-- ---------------------------------------------------------------------------
-- Deneyler
-- ---------------------------------------------------------------------------
-- variants tek bir jsonb sutununda tutulur: bir deneyin kollari her zaman
-- butun olarak okunup yazilir (EvalSuite.checks ile ayni gerekce). Her eleman
-- {"name": "...", "version": N, "weight": N} bicimindedir.

CREATE TABLE IF NOT EXISTS {schema}.experiments (
    id             uuid        NOT NULL PRIMARY KEY,
    tenant_id      text        NOT NULL,
    name           text        NOT NULL,
    agent_name     text        NOT NULL,
    variants       jsonb       NOT NULL,
    status         smallint    NOT NULL DEFAULT 0,  -- 0=Draft 1=Running 2=Stopped
    assignment_key text,
    started_at     timestamptz,
    ended_at       timestamptz,
    updated_at     timestamptz NOT NULL,
    CONSTRAINT experiments_tenant_name_uq UNIQUE (tenant_id, name)
);

-- Ayni agent icin ayni anda TEK Running deney olabilir. Kismi benzersiz indeks
-- bu kurali uygulama kodu race'ine birakmadan veritabaninda zorlar.
CREATE UNIQUE INDEX IF NOT EXISTS experiments_running_agent_uq
    ON {schema}.experiments (tenant_id, agent_name)
    WHERE status = 1;

CREATE INDEX IF NOT EXISTS experiments_tenant_agent_idx
    ON {schema}.experiments (tenant_id, agent_name);
