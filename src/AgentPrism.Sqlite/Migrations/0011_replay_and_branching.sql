-- Faz 47 -- yeniden oynatma ve konusma dallandirma.
--
-- Gerekce ve sutun anlamlari icin PostgreSQL 0023_replay_and_branching.sql'e
-- bakin.
--
-- 🚨 Tablo ve indeks adlari ONEK tasir (K-193): SQLite'ta sema yoktur ve indeks
-- adlari veritabani genelinde tek ad alanini paylasir.
--
-- 🚨 `ALTER TABLE ... ADD COLUMN` icin `IF NOT EXISTS` YOKTUR; guvenlik
-- migration kosucusundan gelir (ayni dosya ikinci kez calismaz).

CREATE TABLE IF NOT EXISTS {schema}run_inputs (
    run_id     TEXT NOT NULL PRIMARY KEY
               REFERENCES {schema}runs (id) ON DELETE CASCADE,
    tenant_id  TEXT NOT NULL,
    messages   TEXT NOT NULL,
    created_at TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS {schema}run_inputs_tenant_created_idx
    ON {schema}run_inputs (tenant_id, created_at DESC);

-- Yeniden oynatma soy bagi. Sutun SONA eklenir; okuyucu sabit sutun indeksi
-- kullanir.
ALTER TABLE {schema}runs ADD COLUMN replay_of_run_id TEXT NULL;

CREATE INDEX IF NOT EXISTS {schema}runs_replay_of_idx
    ON {schema}runs (tenant_id, replay_of_run_id)
    WHERE replay_of_run_id IS NOT NULL;

-- Konusma dal isaretcisi. Yabanci anahtar YOKTUR; gerekce PostgreSQL 0023'te.
ALTER TABLE {schema}conversations ADD COLUMN parent_conversation_id TEXT NULL;
ALTER TABLE {schema}conversations ADD COLUMN branch_from_seq INTEGER NULL;

CREATE INDEX IF NOT EXISTS {schema}conversations_parent_idx
    ON {schema}conversations (parent_conversation_id)
    WHERE parent_conversation_id IS NOT NULL;
