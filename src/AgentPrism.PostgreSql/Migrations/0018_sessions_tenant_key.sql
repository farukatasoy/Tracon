-- ---------------------------------------------------------------------------
-- 0018 — Oturum birincil anahtari kiraciyi da kapsar (Faz 41)
--
-- 🚨 GUVENLIK DUZELTMESI. `sessions.id` cagiran tarafindan verilen bir metindir
-- (AgentSession kimligi, /v1/responses konusma kimligi). Tek basina birincil
-- anahtar oldugu icin kimlik BUTUN KIRACILAR arasinda benzersizdi ve
-- `ON CONFLICT (id) DO UPDATE SET tenant_id = EXCLUDED.tenant_id` bir kiracinin
-- baska bir kiracinin oturumunu UZERINE YAZMASINA izin veriyordu: durum
-- kayboluyor ve satirin sahipligi el degistiriyordu.
--
-- Anahtar (tenant_id, id) olur. Ayni kimlik iki kiracida bagimsiz yasar.
-- Yabanci anahtar referansi yoktur; degisiklik bu tabloyla sinirlidir.
-- ---------------------------------------------------------------------------

ALTER TABLE {schema}.sessions DROP CONSTRAINT sessions_pkey;

ALTER TABLE {schema}.sessions ADD CONSTRAINT sessions_pkey PRIMARY KEY (tenant_id, id);
