-- ---------------------------------------------------------------------------
-- 0018 — The session primary key also covers the tenant (phase 41)
--
-- 🚨 SECURITY FIX. `sessions.id` is a text given by the caller (the AgentSession
-- id, the /v1/responses conversation id). Because it was the primary key on its
-- own, the id was unique ACROSS ALL TENANTS and
-- `ON CONFLICT (id) DO UPDATE SET tenant_id = EXCLUDED.tenant_id` let one tenant
-- OVERWRITE the session of another tenant: the state was lost and the ownership
-- of the row changed hands.
--
-- The key becomes (tenant_id, id). The same id lives independently in two tenants.
-- There is no foreign key reference; the change is limited to this table.
-- ---------------------------------------------------------------------------

ALTER TABLE {schema}.sessions DROP CONSTRAINT sessions_pkey;

ALTER TABLE {schema}.sessions ADD CONSTRAINT sessions_pkey PRIMARY KEY (tenant_id, id);
