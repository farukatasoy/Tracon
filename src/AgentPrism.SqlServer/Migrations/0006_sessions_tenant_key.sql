-- ---------------------------------------------------------------------------
-- 0006 — The session primary key also covers the tenant (phase 41)
--
-- 🚨 SECURITY FIX. The rationale is the same as PostgreSQL 0018: `sessions.id`
-- is given by the caller and while it was the primary key on its own, one tenant
-- could overwrite the session of another tenant.
--
-- Key length: nvarchar(200) + nvarchar(200) = 400 bytes; below the 900 byte
-- limit for a clustered index key.
-- ---------------------------------------------------------------------------

ALTER TABLE {schema}.sessions DROP CONSTRAINT sessions_pk;

ALTER TABLE {schema}.sessions ADD CONSTRAINT sessions_pk PRIMARY KEY (tenant_id, id);
