-- ---------------------------------------------------------------------------
-- 0006 — Oturum birincil anahtari kiraciyi da kapsar (Faz 41)
--
-- 🚨 GUVENLIK DUZELTMESI. Gerekce PostgreSQL 0018 ile aynidir: `sessions.id`
-- cagiran tarafindan verilir ve tek basina birincil anahtar oldugunda bir
-- kiraci baska bir kiracinin oturumunu uzerine yazabiliyordu.
--
-- Anahtar uzunlugu: nvarchar(200) + nvarchar(200) = 400 bayt; kumelenmis
-- indeks anahtari icin 900 bayt sinirinin altindadir.
-- ---------------------------------------------------------------------------

ALTER TABLE {schema}.sessions DROP CONSTRAINT sessions_pk;

ALTER TABLE {schema}.sessions ADD CONSTRAINT sessions_pk PRIMARY KEY (tenant_id, id);
