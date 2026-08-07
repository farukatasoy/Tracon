-- Faz 47 -- yeniden oynatma ve konusma dallandirma.
--
-- Gerekce ve sutun anlamlari icin PostgreSQL 0023_replay_and_branching.sql'e
-- bakin.
--
-- 🚨 `messages` sutunu nvarchar(max)'tir ve ISJSON kisiti TASIMAZ: polimorfik
-- yuk PostgreSQL'de de `json` (jsonb DEGIL) olarak saklanir ve anahtar sirasi
-- korunmalidir. Davranis esitligi icin burada da dogrulama yapilmaz.

IF OBJECT_ID(N'{schema}.run_inputs', N'U') IS NULL
CREATE TABLE {schema}.run_inputs (
    run_id     uniqueidentifier NOT NULL,
    tenant_id  nvarchar(200)    NOT NULL,
    messages   nvarchar(max)    NOT NULL,
    created_at datetimeoffset   NOT NULL,
    CONSTRAINT run_inputs_pk PRIMARY KEY (run_id),
    CONSTRAINT run_inputs_run_fk FOREIGN KEY (run_id)
        REFERENCES {schema}.runs (id) ON DELETE CASCADE
);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'run_inputs_tenant_created_idx' AND object_id = OBJECT_ID(N'{schema}.run_inputs'))
CREATE INDEX run_inputs_tenant_created_idx
    ON {schema}.run_inputs (tenant_id, created_at DESC);

-- Yeniden oynatma soy bagi. Sutun SONA eklenir; okuyucu sabit sutun indeksi
-- kullanir.
IF COL_LENGTH(N'{schema}.runs', N'replay_of_run_id') IS NULL
ALTER TABLE {schema}.runs ADD replay_of_run_id uniqueidentifier NULL;

-- 🚨 EXEC ile sarilir: replay_of_run_id yukarida AYNI toplu islemde ALTER
-- TABLE ile eklenir; EXEC olmadan "Invalid column name" verir (bkz. 0003_tool_usage.sql).
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'runs_replay_of_idx' AND object_id = OBJECT_ID(N'{schema}.runs'))
EXEC(N'CREATE INDEX runs_replay_of_idx
    ON {schema}.runs (tenant_id, replay_of_run_id)
    WHERE replay_of_run_id IS NOT NULL;');

-- Konusma dal isaretcisi.
--
-- 🚨 Yabanci anahtar YOKTUR; gerekce ve olculen SQL Server kisiti (hata 1785)
-- PostgreSQL 0023'te yazilidir. Isaretci yalnizca koken bilgisidir ve hicbir
-- okuma yolunda JOIN'lenmez.
IF COL_LENGTH(N'{schema}.conversations', N'parent_conversation_id') IS NULL
ALTER TABLE {schema}.conversations ADD parent_conversation_id uniqueidentifier NULL;

IF COL_LENGTH(N'{schema}.conversations', N'branch_from_seq') IS NULL
ALTER TABLE {schema}.conversations ADD branch_from_seq bigint NULL;

-- 🚨 EXEC ile sarilir: parent_conversation_id yukarida AYNI toplu islemde
-- ALTER TABLE ile eklenir; EXEC olmadan "Invalid column name" verir (bkz. 0003_tool_usage.sql).
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'conversations_parent_idx' AND object_id = OBJECT_ID(N'{schema}.conversations'))
EXEC(N'CREATE INDEX conversations_parent_idx
    ON {schema}.conversations (parent_conversation_id)
    WHERE parent_conversation_id IS NOT NULL;');
