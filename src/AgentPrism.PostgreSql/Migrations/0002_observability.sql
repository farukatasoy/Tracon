-- Faz 6 — gozlemlenebilirlik, tool onayi, MCP ve kiraci yonetimi.
--
-- 0001'de sema kurulmus ancak DOLDURULMAMIS tablolar bu migration ile
-- kullanilabilir hale gelir: tool_invocations, traces, spans.
--
-- Kurallar 0001 ile ayni:
--   * `{schema}` yer tutucusu calisma aninda degistirilir.
--   * Zaman alanlari `timestamptz`, her zaman UTC.
--   * Birincil anahtarlar `uuid`; uygulama uretir.

-- ---------------------------------------------------------------------------
-- Calistirmalar: model adi
-- ---------------------------------------------------------------------------
-- Maliyet ve model kirilimi raporlari model adini gerektirir. 0001'de bu sutun
-- yoktu ve /api/stats maliyeti bilerek gostermiyordu.
--
-- Sutun NULL kabul eder: kodda tanimli agent'larin model baglantisi katalog
-- ozetinde bilinmeyebilir. Bilinmeyen model kirilima girmez, toplamlarda sayilir.

ALTER TABLE {schema}.runs ADD COLUMN IF NOT EXISTS model_id text;

CREATE INDEX IF NOT EXISTS runs_tenant_model_started_idx
    ON {schema}.runs (tenant_id, model_id, started_at DESC)
    WHERE model_id IS NOT NULL;

-- ---------------------------------------------------------------------------
-- Tool cagrilari
-- ---------------------------------------------------------------------------
-- `arguments` ve `result` sutunlari `jsonb` idi; `text` yapiliyor.
--
-- Sebep run_events.payload ile ayni: RunRecordingAgent tool argumanlarini AOT
-- uyumlu kalmak icin ELLE bicimlendirir (`ad=deger, ad=deger`) ve cikti gecerli
-- JSON degildir. `jsonb` sutunu bu durumda calistirmayi kesen bir hata uretirdi.
-- Tablo bu migration'a kadar hic doldurulmadigi icin donusum veri kaybetmez.

ALTER TABLE {schema}.tool_invocations
    ALTER COLUMN arguments TYPE text USING arguments::text,
    ALTER COLUMN result    TYPE text USING result::text;

-- Tool'un kaynagi. Kodda tanimli tool'larda NULL; MCP tool'larinda sunucu adi.
ALTER TABLE {schema}.tool_invocations ADD COLUMN IF NOT EXISTS source text;

-- Tool kullanim ozeti (ToolUsageQuery) tool adina gore gruplar.
CREATE INDEX IF NOT EXISTS tool_invocations_tool_created_idx
    ON {schema}.tool_invocations (tool_name, created_at DESC);

-- ---------------------------------------------------------------------------
-- Span'ler
-- ---------------------------------------------------------------------------
-- W3C span kimligi ayrica saklanir: kullanici ayni span'i kendi APM sisteminde
-- (Jaeger, Application Insights) bulabilmelidir. `spans.id` ise bu kimlikten
-- TURETILIR (SHA-256), boylece ust span'in kimligi haritasiz hesaplanabilir ve
-- ayni span iki kez yazilirsa cakisma tekrar uretmez.
--
-- NOT NULL degil: 0001 doneminden kalan (bos) satirlar icin varsayilan yok.

ALTER TABLE {schema}.spans ADD COLUMN IF NOT EXISTS span_id text;

CREATE INDEX IF NOT EXISTS traces_run_idx
    ON {schema}.traces (run_id)
    WHERE run_id IS NOT NULL;

-- ---------------------------------------------------------------------------
-- Tool onay kurallari
-- ---------------------------------------------------------------------------
-- Kullanicinin "bir daha sorma" dedigi kalici onaylar.
--
-- Kural kiraci + agent + tool ucglusune baglidir. `agent_name` NULL ise kural
-- kiracinin tum agent'larini kapsar. `arguments_hash` NULL ise tool'un her
-- cagrisini; dolu ise yalnizca ayni argumanlarla yapilan cagriyi kapsar.
--
-- Benzersizlik kisiti NULL tasiyan sutunlar iceriyor ve PostgreSQL'de NULL'lar
-- birbirine esit sayilmaz; bu yuzden kisit yerine COALESCE'li bir ifade indeksi
-- kullaniliyor. Aksi halde ayni kural sinirsiz kez eklenebilirdi.

CREATE TABLE IF NOT EXISTS {schema}.tool_approval_rules (
    id             uuid        NOT NULL PRIMARY KEY,
    tenant_id      text        NOT NULL,
    agent_name     text,
    tool_name      text        NOT NULL,
    arguments_hash text,
    created_by     text,
    created_at     timestamptz NOT NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS tool_approval_rules_scope_uq
    ON {schema}.tool_approval_rules (
        tenant_id,
        COALESCE(agent_name, ''),
        tool_name,
        COALESCE(arguments_hash, '')
    );

CREATE INDEX IF NOT EXISTS tool_approval_rules_tenant_created_idx
    ON {schema}.tool_approval_rules (tenant_id, created_at DESC);

-- ---------------------------------------------------------------------------
-- MCP sunuculari
-- ---------------------------------------------------------------------------
-- Uzak MCP sunuculari. Tool'lari baglanti aninda kesfedilir.
--
-- 🚨 SIR TASIMAZ. Kimlik dogrulama basliginin DEGERI burada saklanmaz; yalnizca
-- degerin okunacagi yapilandirma anahtarinin ADI saklanir
-- (`authorization_configuration_key`). Deger calisma aninda IConfiguration
-- uzerinden cozulur ve `dotnet user-secrets` veya ortam degiskeninde kalir.
-- Boylece veritabani yedegi, denetim izi veya arayuz yaniti hicbir zaman sir
-- tasimaz. Gerekce: docs/KARARLAR.md, karar K-059.
--
-- `transport` smallint: 0 = StreamableHttp, 1 = SSE. Stdio BILEREK YOKTUR —
-- sunucuda surec baslatmak tasarim kurali K2'yi bozar (karar K-058).

CREATE TABLE IF NOT EXISTS {schema}.mcp_servers (
    id                              uuid        NOT NULL PRIMARY KEY,
    tenant_id                       text        NOT NULL,
    name                            text        NOT NULL,
    description                     text,
    endpoint                        text        NOT NULL,
    transport                       smallint    NOT NULL DEFAULT 0,
    authorization_configuration_key text,
    headers                         jsonb       NOT NULL DEFAULT '{}'::jsonb,
    enabled                         boolean     NOT NULL DEFAULT true,
    requires_approval               boolean     NOT NULL DEFAULT true,
    created_at                      timestamptz NOT NULL,
    updated_at                      timestamptz NOT NULL,
    CONSTRAINT mcp_servers_tenant_name_uq UNIQUE (tenant_id, name)
);
