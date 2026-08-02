-- Faz 15 -- workflows: yurutme ve kalicilik.

-- Workflow calistirmalari icin AYRI BIR TABLO ACILMAZ. Runs ekrani, SSE akisi,
-- kiraci filtreleri, istatistikler ve waterfall zaten `runs` uzerine kurulu;
-- ikinci bir kayit hatti hepsini ikiye katlardi. Ayrim iki sutunla yapilir.
--
-- `kind` NOT NULL DEFAULT 0'dir: var olan her satir bir agent calistirmasidir
-- ve varsayilan onlari dogru siniflar.
ALTER TABLE {schema}.runs ADD COLUMN IF NOT EXISTS kind smallint NOT NULL DEFAULT 0;
ALTER TABLE {schema}.runs ADD COLUMN IF NOT EXISTS workflow_name text;

-- Kismi indeks: workflow satirlari toplamin kucuk bir yuzdesidir. Tam indeks,
-- agent satirlarinin her yazimini da pahalilastirirdi.
CREATE INDEX IF NOT EXISTS runs_workflow_idx
    ON {schema}.runs (tenant_id, workflow_name, started_at DESC)
    WHERE kind = 1;

-- Arayuzden tanimlanan workflow'lar. `definition` bir graf tanimidir, kod
-- degildir: yalnizca katalogdaki agent'larin ADLARINI ve bir deseni tasir.
-- Kodda tanimli workflow'lar burada YASAMAZ; onlar fabrika olarak kaydedilir.
--
-- Surum GECMISI tutulmaz (agent_definition_versions gibi bir tablo yoktur).
-- Bir workflow tanimi ad listesi ve desenden ibarettir; geri almak icin gereken
-- bilgi audit_log icinde zaten bulunur. Agent tanimi ise talimat METNI tasir ve
-- o metnin eski hali baska hicbir yerde yeniden kurulamaz -- fark budur.
CREATE TABLE IF NOT EXISTS {schema}.workflows (
    id          uuid        NOT NULL PRIMARY KEY,
    tenant_id   text        NOT NULL,
    name        text        NOT NULL,
    version     integer     NOT NULL,
    definition  jsonb       NOT NULL,
    created_at  timestamptz NOT NULL,
    updated_at  timestamptz NOT NULL,
    CONSTRAINT workflows_tenant_name_uq UNIQUE (tenant_id, name)
);

-- 🚨 `state` sutunu `json`'dur, `jsonb` DEGILDIR.
--
-- Microsoft Agent Framework'un kontrol noktasi yuku opak ve polimorfiktir:
-- icinde System.Text.Json'in `$type` ayraci bulunur ve o ayrac bulundugu
-- nesnenin ILK ozelligi olmak zorundadir. PostgreSQL `jsonb` anahtarlari once
-- uzunluga sonra bayta gore yeniden sirilar ve ayraci ilk olmaktan cikarir;
-- okuma "The metadata property ... is not the first property" ile patlar.
--
-- Olculdu (Faz 15): uc agent'li bir Sequential workflow'un 7.537 baytlik
-- kontrol noktasinda `{"$type":0,...}` ayraci 1260. bayttan itibaren gercekten
-- bulunuyor. Bu tam olarak K-027'nin anlattigi hatadir.
CREATE TABLE IF NOT EXISTS {schema}.workflow_checkpoints (
    id            uuid        NOT NULL PRIMARY KEY,
    tenant_id     text        NOT NULL,
    session_id    text        NOT NULL,
    checkpoint_id text        NOT NULL,
    parent_id     text,
    run_id        uuid,
    state         json        NOT NULL,
    created_at    timestamptz NOT NULL,
    CONSTRAINT workflow_checkpoints_uq UNIQUE (tenant_id, session_id, checkpoint_id)
);

CREATE INDEX IF NOT EXISTS workflow_checkpoints_session_idx
    ON {schema}.workflow_checkpoints (tenant_id, session_id, created_at);

CREATE INDEX IF NOT EXISTS workflow_checkpoints_run_idx
    ON {schema}.workflow_checkpoints (tenant_id, run_id, created_at)
    WHERE run_id IS NOT NULL;
