-- Faz 21 -- kota, hiz siniri sayaclari ve olay yayini (webhook).

-- ---------------------------------------------------------------------------
-- Is kuyrugu: is basina deneme siniri
-- ---------------------------------------------------------------------------
-- Webhook teslimi genel `AgentPrism:Scheduling:MaxAttempts` ayarindan farkli bir
-- merdiven kullanir (5 deneme). NULL = genel ayar gecerli (K-160).
ALTER TABLE {schema}.jobs ADD COLUMN IF NOT EXISTS max_attempts smallint;

-- ---------------------------------------------------------------------------
-- Kota tanimlari
-- ---------------------------------------------------------------------------
-- Hiz siniri (saniye/dakika) ve kota (gun/ay) AYRI kavramlardir: hiz siniri
-- bellekte yasar, kota burada. Bkz. docs/21-KOTA-VE-OLAY-YAYINI.md bolum 21.2.
CREATE TABLE IF NOT EXISTS {schema}.quotas (
    id           uuid           NOT NULL PRIMARY KEY,
    tenant_id    text           NOT NULL,
    agent_name   text,                            -- NULL = kiracinin tumu
    period       smallint       NOT NULL,         -- 0=Daily 1=Monthly
    max_runs     bigint,
    max_tokens   bigint,
    max_cost     numeric(20,10),                  -- para hesabinda ikili kayan nokta kullanilmaz
    enabled      boolean        NOT NULL DEFAULT true,
    created_at   timestamptz    NOT NULL,
    updated_at   timestamptz    NOT NULL
);

-- 🚨 PostgreSQL'de NULL'lar birbirine esit sayilmaz: duz bir UNIQUE, agent_name
-- NULL olan ayni kuralin sinirsiz kez eklenmesine izin verirdi. Faz 11'in
-- skill_scripts dersi (docs/hafiza/postgresql.md).
CREATE UNIQUE INDEX IF NOT EXISTS quotas_scope_uq
    ON {schema}.quotas (tenant_id, COALESCE(agent_name, ''), period);

-- ---------------------------------------------------------------------------
-- Kota kullanim sayaclari
-- ---------------------------------------------------------------------------
-- agent_name burada NOT NULL DEFAULT '' -- birincil anahtarin parcasi oldugu
-- icin NULL olamaz; '' kiraci geneli anlamina gelir. Bir calistirma HEM agent
-- satirini HEM kiraci geneli satirini artirir.
CREATE TABLE IF NOT EXISTS {schema}.quota_usage (
    tenant_id     text           NOT NULL,
    agent_name    text           NOT NULL DEFAULT '',
    period        smallint       NOT NULL,
    period_start  date           NOT NULL,
    runs          bigint         NOT NULL DEFAULT 0,
    tokens        bigint         NOT NULL DEFAULT 0,
    cost          numeric(20,10) NOT NULL DEFAULT 0,
    updated_at    timestamptz    NOT NULL,
    PRIMARY KEY (tenant_id, agent_name, period, period_start)
);

-- ---------------------------------------------------------------------------
-- Webhook abonelikleri
-- ---------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS {schema}.webhook_subscriptions (
    id                       uuid        NOT NULL PRIMARY KEY,
    tenant_id                text        NOT NULL,
    name                     text        NOT NULL,
    url                      text        NOT NULL,
    events                   text[]      NOT NULL,
    -- 🚨 ANAHTAR ADI -- SIR DEGIL (K-059). Imzalama sirri veritabaninda
    -- DURMAZ; yalnizca degerin okunacagi yapilandirma anahtarinin adi durur ve
    -- deger calisma aninda IConfiguration uzerinden cozulur.
    secret_configuration_key text,
    headers                  jsonb       NOT NULL DEFAULT '{}'::jsonb,
    enabled                  boolean     NOT NULL DEFAULT true,
    -- Ust uste basarisiz teslim sayaci; esigi asinca abonelik kendiliginden
    -- devre disi kalir ve denetim izine yazilir.
    consecutive_failures     integer     NOT NULL DEFAULT 0,
    created_at               timestamptz NOT NULL,
    updated_at               timestamptz NOT NULL,
    CONSTRAINT webhook_subscriptions_tenant_name_uq UNIQUE (tenant_id, name)
);

-- ---------------------------------------------------------------------------
-- Teslim gecmisi
-- ---------------------------------------------------------------------------
-- Bu tablo bir KUYRUK DEGILDIR: zamanlama ve kiralama Faz 17'nin `jobs`
-- tablosunda yasar (K-160). Buradaki `attempt` yalniz gecmisi raporlar.
CREATE TABLE IF NOT EXISTS {schema}.webhook_deliveries (
    id              uuid        NOT NULL PRIMARY KEY,
    subscription_id uuid        NOT NULL REFERENCES {schema}.webhook_subscriptions (id) ON DELETE CASCADE,
    tenant_id       text        NOT NULL,
    event_type      text        NOT NULL,
    payload         text        NOT NULL,
    status          smallint    NOT NULL,      -- 0=Pending 1=Delivered 2=Failed 3=Dropped
    attempt         smallint    NOT NULL DEFAULT 0,
    response_code   integer,
    error           text,
    created_at      timestamptz NOT NULL,
    delivered_at    timestamptz
);

CREATE INDEX IF NOT EXISTS webhook_deliveries_subscription_idx
    ON {schema}.webhook_deliveries (subscription_id, created_at DESC);

CREATE INDEX IF NOT EXISTS webhook_deliveries_tenant_created_idx
    ON {schema}.webhook_deliveries (tenant_id, created_at DESC);
