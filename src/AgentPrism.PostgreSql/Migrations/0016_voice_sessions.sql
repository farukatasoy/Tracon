-- Faz 29 -- gercek zamanli konusma baglantilarinin ozet kaydi.
--
-- 🚨 Ses ICERIGI BU TABLODA DURMAZ. Tablo yalnizca olcum ve gozlemlenebilirlik
-- icindir: kim, ne zaman, kac tur, ne kadar ses. Konusmanin sesi saklaniyorsa
-- (varsayilan HAYIR) baytlar `attachments` tablosundadir.
--
-- Her konusma turu ayrica normal bir `runs` satiri uretir. Ses, calistirma
-- yolunu degistirmez; yalnizca girdi ve cikti bicimini degistirir. Bu yuzden
-- token maliyeti, span'ler ve tool onaylari ayni yerdedir ve burada TEKRAR
-- EDILMEZ.
--
-- Gerekce: docs/29-KONUSMA-KATMANI.md, bolum 29.4.

CREATE TABLE IF NOT EXISTS {schema}.voice_sessions (
    id            uuid        NOT NULL PRIMARY KEY,
    tenant_id     text        NOT NULL,

    -- Konusmanin yurudugu agent oturumu. Yabanci anahtar DEGILDIR: oturum
    -- silinse bile konusmanin yapildigi gercegi kalir (`attachments.run_id`
    -- ile ayni gerekce, sema 0006).
    session_id    text        NOT NULL,
    agent_name    text        NOT NULL,
    started_at    timestamptz NOT NULL,

    -- Baglanti hala aciksa NULL. Sunucu cokerse NULL kalir; bu, kapanmamis bir
    -- baglantinin izidir ve bilerek silinmez.
    ended_at      timestamptz,
    turns         integer     NOT NULL DEFAULT 0,

    -- Cozulen toplam ses suresi. Saglayici sure bildirmediyse NULL kalir --
    -- SIFIR degil. AgentPrism olcum uydurmaz (K-032).
    input_seconds numeric(12,3),
    output_chars  bigint,

    -- VoiceSessionEndReason. smallint: enum degerleri kararlidir ve JSON
    -- bicimindeki degisiklik saklanan veriyi etkilemez.
    end_reason    smallint,
    created_by    text
);

-- Kiraci icinde en yeniden eskiye listeleme -- tek erisim deseni budur.
CREATE INDEX IF NOT EXISTS voice_sessions_tenant_started_idx
    ON {schema}.voice_sessions (tenant_id, started_at DESC);
