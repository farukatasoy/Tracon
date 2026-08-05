-- Faz 29 -- gercek zamanli konusma baglantilarinin ozet kaydi.
--
-- Gerekce ve sutun anlamlari icin PostgreSQL 0016_voice_sessions.sql'e bakin.
--
-- 🚨 Tablo ve indeks adlari ONEK tasir (K-193): SQLite'ta nesne adlari
-- veritabani genelinde tek ad alanini paylasir. Onek yazilmazsa ayni `.db`
-- dosyasini paylasan iki farkli TablePrefix degeri catisir.
--
-- `numeric` icin ozel islem gerekmez: surucu TEXT yazar ve kulturden
-- bagimsizdir (SQL Server'in Precision/Scale zorunlulugu burada YOKTUR).

CREATE TABLE IF NOT EXISTS {schema}voice_sessions (
    id            TEXT    NOT NULL PRIMARY KEY,
    tenant_id     TEXT    NOT NULL,
    session_id    TEXT    NOT NULL,
    agent_name    TEXT    NOT NULL,
    started_at    TEXT    NOT NULL,
    ended_at      TEXT    NULL,
    turns         INTEGER NOT NULL DEFAULT 0,
    input_seconds TEXT    NULL,
    output_chars  INTEGER NULL,
    end_reason    INTEGER NULL,
    created_by    TEXT    NULL
);

CREATE INDEX IF NOT EXISTS {schema}voice_sessions_tenant_started_idx
    ON {schema}voice_sessions (tenant_id, started_at DESC);
