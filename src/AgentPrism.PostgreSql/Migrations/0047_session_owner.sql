-- ---------------------------------------------------------------------------
-- 0047 — Per-user session ownership
--
-- A SECOND boundary drawn UNDER the tenant, not a replacement for it. Until
-- now AgentPrism knew of no owner narrower than `tenant_id`, so "show this
-- user their own conversations" could not be built on top of it: the session
-- list was either fully open or, when a consumer's IRunAuthorizationHandler
-- said no, fully rejected. Filtering it server side was refused because the
-- filter would have been applied AFTER paging, cutting rows out of an already
-- built page. With the owner in the row the filter becomes a WHERE clause and
-- that objection disappears.
--
-- 🚨 owner_id is an OPAQUE string carrying the SAME meaning as runs.user_id
-- (phase 68): AgentPrism neither resolves nor validates it, stores no personal
-- detail of its own, and the consumer decides what it identifies. It is never
-- read from a request body -- that would let any client open a session under
-- another user's name, the same forgery the attribution column already
-- refuses.
--
-- NO foreign key, for the same reason tenant_id carries none (K-030):
-- AgentPrism owns no user catalogue and must not require one.
--
-- NULLABLE, and null means UNOWNED. Every row written before this migration
-- reads back unowned, and AgentPrism cannot invent an owner for a session it
-- did not watch being opened. Those rows drop out of owner-filtered listings
-- and stay in the management listing; ownership is safe to turn on over
-- existing data but it is NOT retroactive.
--
-- The default is NULL, so no table rewrite is expected -- but that is a claim
-- to MEASURE on a production sized table, not to assert here (manual case 11).
-- ---------------------------------------------------------------------------

ALTER TABLE {schema}.sessions ADD COLUMN IF NOT EXISTS owner_id text;

-- Partial index, exactly like runs_tenant_user_idx: an unowned row takes no
-- space in it, so a deployment that never turns ownership on pays nothing.
-- The two existing indexes are NOT dropped -- with ownership off the listing
-- query carries no owner predicate and keeps using sessions_tenant_updated_idx
-- unchanged.
CREATE INDEX IF NOT EXISTS sessions_tenant_owner_updated_idx
    ON {schema}.sessions (tenant_id, owner_id, updated_at DESC)
    WHERE owner_id IS NOT NULL;
