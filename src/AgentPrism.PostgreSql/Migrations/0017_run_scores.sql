-- Faz 31 -- calistirma ve mesaj basina insan (veya yargic) puani.
--
-- Puan `runs` tablosuna sutun olarak EKLENMEZ: bir calistirma birden fazla
-- puan alabilir (farkli yazar/zaman), puan mesaj duzeyinde de verilebilir ve
-- `runs` her calistirmada yazilan sicak yoldur -- puan seyrek gelir ve ayri
-- bir yasam dongusu izler.
--
-- `source` sutunu bugunden konur: F-71 (cevrimici degerlendirme) yargic
-- puanini AYNI tabloya yazacak ve insan puanindan ayirt edilmelidir. Sutunu
-- sonradan eklemek uc migration daha demektir; bugun koymak bedavadir.
--
-- Yabanci anahtar YOKTUR -- bu tabloda depoda hicbir "olay/ozet" tablosu
-- (run_events, tool_invocations, voice_sessions) `runs`'a FK tasimaz.
--
-- Gerekce: docs/31-GERI-BILDIRIM-VE-PUANLAMA.md, bolum 31.1.

CREATE TABLE IF NOT EXISTS {schema}.run_scores (
    id          uuid        NOT NULL PRIMARY KEY,
    tenant_id   text        NOT NULL,
    run_id      uuid        NOT NULL,

    -- NULL ise puan TUM calistirmaya aittir; doluysa tek bir mesaja aittir.
    message_id  text,

    -- RunScoreKind. smallint: enum degerleri kararlidir, JSON bicimindeki
    -- degisiklik saklanan veriyi etkilemez.
    kind        smallint    NOT NULL,

    -- Binary icin 0/1, Stars icin 1..5.
    value       integer     NOT NULL,
    comment     text,

    -- human | api | judge. Bugun tek deger 'human'dir.
    source      text        NOT NULL,
    author      text,
    created_at  timestamptz NOT NULL
);

-- Bir yazar bir hedefi (calistirma veya mesaj) bir kez puanlar; ikinci yazim
-- GUNCELLER. message_id NULL oldugunda COALESCE ile '' sayilir ki calistirma
-- duzeyindeki puan da ayni kurala uysun (K-023'un kota deseniyle aynidir).
--
-- 🚨 author BILEREK COALESCE EDILMEZ: PostgreSQL'de NULL hicbir NULL'a esit
-- sayilmadigi icin author bos oldugunda (kimliksiz kurulum) benzersizlik
-- kisiti hic devreye girmez ve her cagri yeni bir satir acar. Bu KASITLIDIR
-- (docs/31-GERI-BILDIRIM-VE-PUANLAMA.md, acik soru 4).
CREATE UNIQUE INDEX IF NOT EXISTS run_scores_target_author_idx
    ON {schema}.run_scores (tenant_id, run_id, COALESCE(message_id, ''), author);

-- Bir calistirmanin puanlarini listeleme -- tek erisim deseni budur.
CREATE INDEX IF NOT EXISTS run_scores_run_idx
    ON {schema}.run_scores (tenant_id, run_id);
