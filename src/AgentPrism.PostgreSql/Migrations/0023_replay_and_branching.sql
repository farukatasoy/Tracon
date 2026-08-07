-- ---------------------------------------------------------------------------
-- 0023 — Yeniden oynatma ve konusma dallandirma (Faz 47)
--
-- Uc degisiklik:
--   1. run_inputs        — calistirmanin GIRDISI. Bugune kadar hicbir yerde
--                          saklanmiyordu; yeniden oynatmanin kaynagidir.
--   2. runs.replay_of_run_id — soy bagi. Bir oynatmanin kaynagini gosterir.
--   3. conversations.parent_conversation_id / branch_from_seq — dal isaretcisi.
--
-- Gerekce: docs/47-YENIDEN-OYNATMA-VE-DALLANDIRMA.md
-- ---------------------------------------------------------------------------

-- ---------------------------------------------------------------------------
-- Calistirma girdileri
-- ---------------------------------------------------------------------------
-- 🚨 `messages` bilerek `json`, `jsonb` DEGIL. ChatMessage icerikleri
-- polimorfiktir ve System.Text.Json'un `$type` ayraci nesnenin ILK ozelligi
-- olmak zorundadir; `jsonb` anahtarlari once uzunluga sonra bayt sirasina gore
-- YENIDEN SIRALAR ve okuma JsonException ile patlar. Ayni gerekce
-- sessions.state, conversation_items.item ve workflow_checkpoints.state icin de
-- gecerlidir. Karar K-027 — bu, o kararin DORDUNCU uygulamasidir.
--
-- Girdi ayri bir tablodadir, `runs`'a sutun olarak EKLENMEZ: `runs` en sicak
-- tablodur ve her liste/istatistik sorgusu onu okur. Ayni gerekceyle
-- conversation_items da ayri bir tabloya konmustu (0001).
--
-- ON DELETE CASCADE: girdi calistirmadan uzun yasamaz.

CREATE TABLE IF NOT EXISTS {schema}.run_inputs (
    run_id     uuid        NOT NULL PRIMARY KEY
               REFERENCES {schema}.runs (id) ON DELETE CASCADE,
    tenant_id  text        NOT NULL,
    messages   json        NOT NULL,
    created_at timestamptz NOT NULL
);

-- Saklama politikasi (RetentionTargets.RunInputs) kiraci + zaman ile tarar.
CREATE INDEX IF NOT EXISTS run_inputs_tenant_created_idx
    ON {schema}.run_inputs (tenant_id, created_at DESC);

-- ---------------------------------------------------------------------------
-- Yeniden oynatma soy bagi
-- ---------------------------------------------------------------------------
-- Sutun SONA eklenir: PostgresRunStore.ReadRun sabit sutun indeksiyle okur ve
-- araya sokulan bir sutun butun indeksleri kaydirirdi (docs/hafiza/postgresql.md).
--
-- Yabanci anahtar YOKTUR: kaynak calistirma saklama politikasiyla silinebilir
-- ve oynatmanin kendisi bundan etkilenmemelidir. Ayni gerekce run_scores'ta da
-- gecerlidir.

ALTER TABLE {schema}.runs
    ADD COLUMN IF NOT EXISTS replay_of_run_id uuid;

-- "Bu calistirmanin tekrarlari" sorgusu icin. Kismi indeks: satirlarin buyuk
-- cogunlugu NULL'dur ve indekse hic girmez.
CREATE INDEX IF NOT EXISTS runs_replay_of_idx
    ON {schema}.runs (tenant_id, replay_of_run_id)
    WHERE replay_of_run_id IS NOT NULL;

-- ---------------------------------------------------------------------------
-- Konusma dallandirma
-- ---------------------------------------------------------------------------
-- Dal, ogeleri KOPYALAR; bu iki sutun yalnizca KOKEN bilgisidir. Isaretci
-- zinciri secilseydi her gecmis okumasi ozyinelemeli olurdu ve
-- SqlChatHistoryProvider (her agent turunda calisan en sicak okuma yolu)
-- ozyinelemeli CTE'ye donerdi.
--
-- 🚨 YABANCI ANAHTAR YOKTUR ve bu bilinclidir. Plan `ON DELETE SET NULL`
-- ongoruyordu; olculdu: SQL Server KENDINE REFERANS VEREN bir yabanci anahtarda
-- SET NULL kabul etmez (hata 1785, "may cause cycles or multiple cascade
-- paths"). Kisit yalnizca PostgreSQL ve SQLite'a konsaydi ayni silme islemi uc
-- saglayicida UC FARKLI sonuc verirdi -- bu repoda kabul edilmeyen tek seydir
-- (K-184'un dersi: sema farki, DAVRANIS farkina donusturulmez).
--
-- Sonuc her saglayicida aynidir: ana konusma silinirse DAL YASAMAYA DEVAM EDER;
-- isaretci yalnizca artik cozulemeyen bir kokeni gosterir. CASCADE zaten
-- reddedilmisti: bir dali ana konusmanin saklama politikasina baglar ve
-- kullanicinin kaydettigi bir dali habersiz silerdi.
--
-- Isaretci hicbir okuma yolunda JOIN'lenmez; kopyalama tasarimi (47.4) geri
-- donuk cozumleme gerektirmez.

ALTER TABLE {schema}.conversations
    ADD COLUMN IF NOT EXISTS parent_conversation_id uuid;

ALTER TABLE {schema}.conversations
    ADD COLUMN IF NOT EXISTS branch_from_seq bigint;

CREATE INDEX IF NOT EXISTS conversations_parent_idx
    ON {schema}.conversations (parent_conversation_id)
    WHERE parent_conversation_id IS NOT NULL;
